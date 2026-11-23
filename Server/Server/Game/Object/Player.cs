using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class Player : GameObject
	{
		public ClientSession Session { get; set; }

		// 스킬 쿨타임 검증용 (Environment.TickCount64 기준)
		public long NextSkillTick { get; set; } = 0;

		// 다음에 들어갈 룸. 접속 직후(삼천도)나 룸을 옮기는 중에 정해지고, 클라가 씬을 다 불러와 C_EnterGame을 보내면 들어간다
		public GameRoom PendingRoom { get; set; }

		public Player()
		{
			ObjectType = GameObjectType.Player;
		}

		const int HeroCharacterId = 1000001; // characterLevel 주인공 그룹(클라 이승·멀티 빌더와 같은 값)

		// 패킷(StatInfo)에 없는 전투 스탯. 서버 판정에만 쓴다. 방어율·치명타·공격 속도·받는 피해 배율은 GameObject에 있다.
		public int BasicRange { get; private set; } = 1;
		public float BasicCoef { get; private set; } = 1f;
		public float ThreatRate { get; private set; } = 1f;
		public float SpiritRegenRate { get; private set; } = 1f;

		// 캐릭터(무당) 스탯 = characterLevel(주인공 그룹, 레벨). 원혼에 빙의했으면 spirits 배율을 적용한다('원혼(직업) 시스템' 2-4).
		//   공격력·체력·이동 속도·공격 속도 = 캐릭터 값 × 배율, 치명타 확률 = 캐릭터 값 + 가산.
		// 룸에 들어갈 때마다 다시 정하고 체력을 가득 채운다(삼천도 = 본모습, 던전 = 원혼).
		public void ApplyStats()
		{
			Table.CharacterLevelRow c = Table.TableManager.CharacterLevel.Get((HeroCharacterId, Stat.Level));
			float attack = c.attack, maxHp = c.maxHP, speed = c.moveSpeed, attackSpeed = c.attackSpeed, critical = c.critical;
			BasicRange = 1; BasicCoef = 1f; DamageTakenRate = 1f; ThreatRate = 1f; SpiritRegenRate = 1f;

			if (Info.SpiritId != 0 && Table.TableManager.Spirits.TryGet(Info.SpiritId, out Table.SpiritsRow s))
			{
				attack *= s.attackRate;
				maxHp *= s.hpRate;
				speed *= s.moveRate;
				attackSpeed *= s.attackSpeedRate;
				critical += s.criticalAdd;
				BasicRange = s.basicRange;
				BasicCoef = s.basicCoef;
				DamageTakenRate = s.damageTakenRate;
				ThreatRate = s.threatRate;
				SpiritRegenRate = s.spiritRegenRate;
			}

			Stat.Attack = (int)Math.Round(attack);
			Stat.MaxHp = (int)Math.Round(maxHp);
			Stat.Hp = Stat.MaxHp;
			Stat.Speed = speed;
			Defence = c.defence;
			Critical = critical;
			CriticalAttack = c.criticalAttack;
			AttackSpeed = attackSpeed;
		}

		// 받는 피해 배율(DamageTakenRate)은 Combat.CalcDamage가 방어율 상한 뒤에 곱한다.
		// TODO 신력(평타 적중·처치 회복)은 신력 시스템을 서버에 옮길 때 적용한다.
		public override void OnDamaged(GameObject attacker, int damage)
		{
			// 혼절한 플레이어는 더 맞지 않는다
			if (State == CreatureState.Dead)
				return;
			base.OnDamaged(attacker, damage);
		}

		// 혼절 재부활 예약을 구분하는 번호. 부활·퇴장 뒤 남은 예약이 엉뚱하게 실행되지 않게 한다.
		public int FaintSerial { get; private set; }

		public override void OnDead(GameObject attacker)
		{
			if (Room == null)
				return;

			// 저승 던전: 체력 0 → 혼절('캐릭터 조작 및 전투' 2-11). 그 외 공간은 전투가 없다(레거시 처리 유지).
			if (Room.Kind != RoomKind.Dungeon)
			{
				base.OnDead(attacker);
				return;
			}

			FaintSerial++;
			State = CreatureState.Dead;
			Statuses.Clear();

			S_Die diePacket = new S_Die();
			diePacket.ObjectId = Id;
			diePacket.AttackerId = attacker.Id;
			Room.Broadcast(diePacket);

			Room.ScheduleFaintRevive(this, FaintSerial);
		}
	}
}
