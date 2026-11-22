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

		// 패킷(StatInfo)에 없는 전투 스탯. 서버 판정에만 쓴다.
		public float Defence { get; private set; }
		public float Critical { get; private set; }
		public float CriticalAttack { get; private set; }
		public float AttackSpeed { get; private set; } = 1f;
		public int BasicRange { get; private set; } = 1;
		public float BasicCoef { get; private set; } = 1f;
		public float DamageTakenRate { get; private set; } = 1f;
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

		// 받는 피해 배율은 피해 판정 결과(방어율 상한 뒤)에 곱한다('원혼(직업) 시스템' 2-4).
		// TODO 방어율·치명타·위협·신력은 전투 판정('캐릭터 조작 및 전투')을 서버에 옮길 때 위 값으로 적용한다.
		public override void OnDamaged(GameObject attacker, int damage)
		{
			base.OnDamaged(attacker, (int)Math.Round(damage * DamageTakenRate));
		}

		public override void OnDead(GameObject attacker)
		{
			base.OnDead(attacker);
		}
	}
}
