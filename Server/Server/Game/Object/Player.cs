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

		// 이동 속도(칸/초)는 이승과 같은 characterLevel.moveSpeed를 쓰고, 원혼에 빙의했으면 spirits.moveRate를 곱한다.
		// 룸에 들어갈 때마다 다시 정한다(삼천도 = 본모습, 던전 = 원혼).
		public void ApplyMoveSpeed()
		{
			float speed = Table.TableManager.CharacterLevel.Get((HeroCharacterId, Stat.Level)).moveSpeed;
			if (Info.SpiritId != 0 && Table.TableManager.Spirits.TryGet(Info.SpiritId, out Table.SpiritsRow spirit))
				speed *= spirit.moveRate;
			Stat.Speed = speed;
		}

		public override void OnDamaged(GameObject attacker, int damage)
		{
			base.OnDamaged(attacker, damage);
		}

		public override void OnDead(GameObject attacker)
		{
			base.OnDead(attacker);
		}
	}
}
