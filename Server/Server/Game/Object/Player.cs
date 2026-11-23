using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class Player : GameObject
	{
		public ClientSession Session { get; set; }

		// 다음에 들어갈 룸. 접속 직후(삼천도)나 룸을 옮기는 중에 정해지고, 클라가 씬을 다 불러와 C_EnterGame을 보내면 들어간다
		public GameRoom PendingRoom { get; set; }

		public Player()
		{
			ObjectType = GameObjectType.Player;
		}

		const int HeroCharacterId = 1000001; // characterLevel 주인공 그룹(클라 이승·멀티 빌더와 같은 값)

		// 패킷(StatInfo)에 없는 전투 스탯. 서버 판정에만 쓴다.
		float _defence;
		float _critical;
		float _criticalAttack;
		float _attackSpeed = 1f;
		float _moveSpeed;
		float _damageTakenRate = 1f;
		public int BasicRange { get; private set; } = 1;
		public int BasicWidth { get; private set; } = 1;
		public float BasicCoef { get; private set; } = 1f;
		public float ThreatRate { get; private set; } = 1f;
		public float SpiritRegenRate { get; private set; } = 1f;

		public override float Defence => _defence;
		public override float CriticalRate => _critical;
		public override float CriticalMultiplier => _criticalAttack;
		protected override float BaseAttackSpeed => _attackSpeed;
		protected override float BaseMoveSpeed => _moveSpeed;
		protected override float DamageTakenRate => _damageTakenRate;

		// 캐릭터(무당) 스탯 = characterLevel(주인공 그룹, 레벨). 원혼에 빙의했으면 spirits 배율을 적용한다('원혼(직업) 시스템' 2-4).
		//   공격력·체력·이동 속도·공격 속도 = 캐릭터 값 × 배율, 치명타 확률 = 캐릭터 값 + 가산.
		// 룸에 들어갈 때마다 다시 정하고 체력·신력을 가득 채운다(삼천도 = 본모습, 던전 = 원혼).
		public void ApplyStats()
		{
			Table.CharacterLevelRow c = Table.TableManager.CharacterLevel.Get((HeroCharacterId, Stat.Level));
			float attack = c.attack, maxHp = c.maxHP, speed = c.moveSpeed, attackSpeed = c.attackSpeed, critical = c.critical;
			BasicRange = 1; BasicWidth = 1; BasicCoef = 1f; _damageTakenRate = 1f; ThreatRate = 1f; SpiritRegenRate = 1f;

			if (Info.SpiritId != 0 && Table.TableManager.Spirits.TryGet(Info.SpiritId, out Table.SpiritsRow s))
			{
				attack *= s.attackRate;
				maxHp *= s.hpRate;
				speed *= s.moveRate;
				attackSpeed *= s.attackSpeedRate;
				critical += s.criticalAdd;
				BasicRange = s.basicRange;
				BasicWidth = Math.Max(1, s.basicWidth);
				BasicCoef = s.basicCoef;
				_damageTakenRate = s.damageTakenRate;
				ThreatRate = s.threatRate;
				SpiritRegenRate = s.spiritRegenRate;
			}

			Status.Clear();
			ResetCombatState();

			Stat.Attack = (int)Math.Round(attack);
			Stat.MaxHp = (int)Math.Round(maxHp);
			Stat.Hp = Stat.MaxHp;
			_moveSpeed = speed;
			Stat.Speed = speed;
			_defence = c.defence;
			_critical = critical;
			_criticalAttack = c.criticalAttack;
			_attackSpeed = attackSpeed;

			Stat.MaxSpirit = c.maxSpirit;
			_spirit = Stat.MaxSpirit;
			Stat.Spirit = Stat.MaxSpirit;
		}

		// ---- 신력 ('캐릭터 조작 및 전투' 2-8) ----

		float _spirit;

		public float Spirit => _spirit;

		// 바뀐 정수 값만 본인에게 알린다(자연 회복이 매 틱 소수로 쌓이므로)
		public void AddSpirit(float amount)
		{
			_spirit = Math.Clamp(_spirit + amount, 0f, Stat.MaxSpirit);
			int value = (int)Math.Floor(_spirit);
			if (value == Stat.Spirit)
				return;
			Stat.Spirit = value;
			Session?.Send(new S_ChangeSpirit() { Spirit = value, MaxSpirit = Stat.MaxSpirit });
		}

		// 자연 회복: 최대 신력 × configs 20035 × 원혼 신력 회복 배율 / 초
		public void RegenSpirit(float deltaSec)
		{
			if (IsAlive == false || _spirit >= Stat.MaxSpirit)
				return;
			AddSpirit(Stat.MaxSpirit * CombatConfig.Get(CombatConfig.SpiritRegenRate) * SpiritRegenRate * deltaSec);
		}

		// ---- 평타·스킬 시간 검증 (Environment.TickCount64 기준) ----

		public long NextBasicTick { get; set; }
		public long GlobalCooldownTick { get; set; }
		readonly Dictionary<int, long> _cooldowns = new Dictionary<int, long>();

		public bool IsOnCooldown(int skillId, long now)
		{
			return _cooldowns.TryGetValue(skillId, out long ready) && ready > now;
		}

		public void StartCooldown(int skillId, long readyTick)
		{
			_cooldowns[skillId] = readyTick;
		}

		// 개발·테스트 명령(DEBUG_REFILL)
		public void ClearCooldowns()
		{
			_cooldowns.Clear();
			GlobalCooldownTick = 0;
			NextBasicTick = 0;
		}

		// 시전 중인 스킬(조준 사격·회혼술). 시전 중에는 이동할 수 없다('스킬 시스템' 2-3)
		public int CastSkillId { get; set; }
		public int CastTargetId { get; set; }
		public int CastToken { get; set; }   // 시전 완료 예약이 이 시전의 것인지 확인한다
		public bool IsCasting => CastSkillId != 0;

		public override bool CanMove => base.CanMove && IsCasting == false;

		// ---- 혼절 ('캐릭터 조작 및 전투' 2-11). 저승 던전에서 체력 0 → State Dead로 그 자리에 쓰러져 있다 ----

		public bool IsFainted => Room != null && State == CreatureState.Dead;
		public long FaintEndTick { get; set; }  // 이때까지 못 일어나면 마지막 불씨에서 부활

		// 적 특수기별 결박 재적용 면역(configs 20055, '망령(적) 시스템' 2-5-1). 특수기 ID → 면역이 끝나는 틱.
		// 결박이 끝나거나(만료) 풀린(해독) 순간부터 5초. 피해는 면역과 상관없이 들어간다.
		readonly Dictionary<int, long> _bindImmuneUntil = new Dictionary<int, long>();
		int _rootSkillId; // 지금 걸린 결박을 건 적 특수기(없으면 0)

		public bool IsBindImmune(int skillId, long now)
		{
			return _bindImmuneUntil.TryGetValue(skillId, out long until) && until > now;
		}

		// 적 특수기가 결박을 걸 때 부른다. 결박이 사라지면 그 스킬의 면역이 시작된다
		public void MarkRootSkill(int skillId)
		{
			_rootSkillId = skillId;
		}

		public override void OnStatusChanged(int statusId)
		{
			base.OnStatusChanged(statusId);

			if (statusId == StatusId.Root && Status.Has(StatusId.Root) == false && _rootSkillId != 0)
			{
				long now = Environment.TickCount64;
				_bindImmuneUntil[_rootSkillId] = now + (long)(1000 * CombatConfig.Get(CombatConfig.MonsterBindImmunity));
				_rootSkillId = 0;
			}
		}

		// F 홀드(혼 부르기·보스방 불씨)
		public ChannelKind ChannelKind { get; set; } = ChannelKind.ChannelNone;
		public int ChannelTargetId { get; set; }
		public int ChannelToken { get; set; }
		public bool IsChanneling => ChannelKind != ChannelKind.ChannelNone;

		// 비전투 회복: 마지막으로 교전한 시각과 아직 반영하지 않은 회복량
		public long LastCombatTick { get; set; }
		public float RegenCarry { get; set; }
		public long NextRegenSendTick { get; set; }

		protected override void OnDamageTaken(GameObject attacker, int damage)
		{
			LastCombatTick = Environment.TickCount64;
		}

		public void ResetCombatState()
		{
			_rootSkillId = 0; // 혼절·룸 이동으로 결박이 지워진 경우(면역 없이 끝)
			NextBasicTick = 0;
			GlobalCooldownTick = 0;
			_cooldowns.Clear();
			CastSkillId = 0;
			CastTargetId = 0;
			CastToken++;
			ChannelKind = ChannelKind.ChannelNone;
			ChannelTargetId = 0;
			ChannelToken++;
			FaintEndTick = 0;
			RegenCarry = 0f;
		}

		// 저승 던전에서는 혼절(룸이 처리), 그 밖에서는 기존처럼 그 자리에서 다시 들어온다
		public override void OnDead(GameObject attacker)
		{
			if (Room != null && Room.Kind == RoomKind.Dungeon)
			{
				Room.OnPlayerFainted(this, attacker);
				return;
			}
			ResetCombatState();
			base.OnDead(attacker);
		}
	}
}
