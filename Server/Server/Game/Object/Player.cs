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

		// 시전 중인 스킬(조준 사격·회혼술). 시전 중에는 이동할 수 없다('스킬 시스템' 2-3)
		public int CastSkillId { get; set; }
		public int CastTargetId { get; set; }
		public int CastToken { get; set; }   // 시전 완료 예약이 이 시전의 것인지 확인한다
		public bool IsCasting => CastSkillId != 0;

		public override bool CanMove => base.CanMove && IsCasting == false;

		void ResetCombatState()
		{
			NextBasicTick = 0;
			GlobalCooldownTick = 0;
			_cooldowns.Clear();
			CastSkillId = 0;
			CastTargetId = 0;
			CastToken++;
		}

		public override void OnDead(GameObject attacker)
		{
			ResetCombatState();
			base.OnDead(attacker);
		}
	}
}
