using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class Arrow : Projectile
	{
		public GameObject Owner { get; set; }

		long _nextMoveTick = 0;

		public override void Update()
		{
			if (Data == null || Data.projectile == null || Owner == null || Room == null)
				return;

			if (_nextMoveTick >= Environment.TickCount64)
				return;

			long tick = (long)(1000 / Data.projectile.speed);
			_nextMoveTick = Environment.TickCount64 + tick;

			Vector2Int destPos = GetFrontCellPos();
			if (Room.Map.CanGo(destPos))
			{
				CellPos = destPos;

				S_Move movePacket = new S_Move();
				movePacket.ObjectId = Id;
				movePacket.PosInfo = PosInfo;
				Room.Broadcast(movePacket);
			}
			else
			{
				// 쏜 플레이어의 공격력 × 평타 계수로 피해 공식 적용. PvP가 없으므로 망령만 맞는다.
				GameObject target = Room.Map.Find(destPos);
				if (target != null && target.ObjectType == GameObjectType.Monster)
				{
					float coef = Owner is Player p ? p.BasicCoef : 1f;
					Combat.Hit(Owner, target, coef);
				}

				// 소멸
				Room.Push(Room.LeaveGame, Id);
			}
		}
	}
}
