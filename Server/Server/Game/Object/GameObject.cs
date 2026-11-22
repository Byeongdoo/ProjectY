using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class GameObject
	{
		public GameObjectType ObjectType { get; protected set; } = GameObjectType.None;
		public int Id
		{
			get { return Info.ObjectId; }
			set { Info.ObjectId = value; }
		}

		public GameRoom Room { get; set; }

		public ObjectInfo Info { get; set; } = new ObjectInfo();
		public PositionInfo PosInfo { get; private set; } = new PositionInfo();
		public StatInfo Stat { get; private set; } = new StatInfo();

		public float Speed
		{
			get { return Stat.Speed; }
			set { Stat.Speed = value; }
		}

		public int Hp
		{
			get { return Stat.Hp; }
			set { Stat.Hp = Math.Clamp(value, 0, Stat.MaxHp); }
		}

		public MoveDir Dir
		{
			get { return PosInfo.MoveDir; }
			set { PosInfo.MoveDir = value; }
		}

		public CreatureState State
		{
			get { return PosInfo.State; }
			set { PosInfo.State = value; }
		}

		public GameObject()
		{
			Info.PosInfo = PosInfo;
			Info.StatInfo = Stat;
		}

		public virtual void Update()
		{

		}

		public Vector2Int CellPos
		{
			get
			{
				return new Vector2Int(PosInfo.PosX, PosInfo.PosY);
			}

			set
			{
				PosInfo.PosX = value.x;
				PosInfo.PosY = value.y;
			}
		}

		public Vector2Int GetFrontCellPos()
		{
			return GetFrontCellPos(PosInfo.MoveDir);
		}

		public Vector2Int GetFrontCellPos(MoveDir dir)
		{
			return CellPos + DirToVec(dir);
		}

		public static Vector2Int DirToVec(MoveDir dir)
		{
			switch (dir)
			{
				case MoveDir.Up: return new Vector2Int(0, 1);
				case MoveDir.Down: return new Vector2Int(0, -1);
				case MoveDir.Left: return new Vector2Int(-1, 0);
				case MoveDir.Right: return new Vector2Int(1, 0);
				case MoveDir.UpLeft: return new Vector2Int(-1, 1);
				case MoveDir.UpRight: return new Vector2Int(1, 1);
				case MoveDir.DownLeft: return new Vector2Int(-1, -1);
				case MoveDir.DownRight: return new Vector2Int(1, -1);
			}
			return new Vector2Int(0, 0);
		}

		// 벡터의 부호만 보고 8방향 중 하나로 바꾼다. (0, 0)이면 Down
		public static MoveDir GetDirFromVec(Vector2Int dir)
		{
			int sx = Math.Sign(dir.x);
			int sy = Math.Sign(dir.y);

			if (sy > 0)
				return sx < 0 ? MoveDir.UpLeft : (sx > 0 ? MoveDir.UpRight : MoveDir.Up);
			if (sy < 0)
				return sx < 0 ? MoveDir.DownLeft : (sx > 0 ? MoveDir.DownRight : MoveDir.Down);
			if (sx < 0)
				return MoveDir.Left;
			if (sx > 0)
				return MoveDir.Right;
			return MoveDir.Down;
		}

		public virtual void OnDamaged(GameObject attacker, int damage)
		{
			if (Room == null)
				return;

			Stat.Hp = Math.Max(Stat.Hp - damage, 0);

			S_ChangeHp changePacket = new S_ChangeHp();
			changePacket.ObjectId = Id;
			changePacket.Hp = Stat.Hp;
			Room.Broadcast(changePacket);

			if (Stat.Hp <= 0)
			{
				OnDead(attacker);
			}
		}

		public virtual void OnDead(GameObject attacker)
		{
			if (Room == null)
				return;

			S_Die diePacket = new S_Die();
			diePacket.ObjectId = Id;
			diePacket.AttackerId = attacker.Id;
			Room.Broadcast(diePacket);

			GameRoom room = Room;
			room.LeaveGame(Id);

			Stat.Hp = Stat.MaxHp;
			PosInfo.State = CreatureState.Idle;
			PosInfo.MoveDir = MoveDir.Down;
			PosInfo.PosX = 0;
			PosInfo.PosY = 0;

			room.EnterGame(this);
		}
	}
}
