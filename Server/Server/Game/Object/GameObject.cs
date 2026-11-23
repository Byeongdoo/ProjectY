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

		public StatusSet Status { get; }

		public GameObject()
		{
			Info.PosInfo = PosInfo;
			Info.StatInfo = Stat;
			Status = new StatusSet(this);
		}

		// ---- 전투 스탯 ('캐릭터 조작 및 전투' 2-6). 패킷(StatInfo)에 없는 값은 서버 판정에만 쓴다 ----

		public virtual bool IsBoss => false;
		public virtual float Defence => 0f;
		public virtual float CriticalRate => 0f;
		public virtual float CriticalMultiplier => CombatConfig.Get(CombatConfig.DefaultCritMultiplier);
		protected virtual float BaseAttackSpeed => 1f;
		protected virtual float BaseMoveSpeed => Stat.Speed;
		// 원혼 받는 피해 배율 등 받는 쪽 고유 배율
		protected virtual float DamageTakenRate => 1f;

		// 강화(공격력 +)
		public float AttackPower => Stat.Attack * (1f + Status.Value(StatusId.Empower));
		// 광기(공속 +)·둔화(공속 −)
		public float AttackSpeed => BaseAttackSpeed * (1f + Status.Value(StatusId.Frenzy)) * (1f - Status.Value(StatusId.Slow));

		public bool IsAlive => Room != null && Stat.Hp > 0 && State != CreatureState.Dead;
		// 기절이면 아무것도 못 한다
		public bool CanAct => IsAlive && Status.Has(StatusId.Stun) == false;
		// 결박·버팀이면 제자리에서 버틴다
		public virtual bool CanMove => CanAct && Status.Has(StatusId.Root) == false && Status.Has(StatusId.Brace) == false;

		// 상태이상이 바뀌면 이동 속도를 다시 정하고, 바뀌었으면 알린다(신속·둔화)
		public virtual void OnStatusChanged(int statusId)
		{
			if (statusId != StatusId.Haste && statusId != StatusId.Slow)
				return;
			RefreshMoveSpeed();
		}

		protected void RefreshMoveSpeed()
		{
			float speed = BaseMoveSpeed * (1f + Status.Value(StatusId.Haste)) * (1f - Status.Value(StatusId.Slow));
			if (Math.Abs(speed - Stat.Speed) < 0.0001f)
				return;
			Stat.Speed = speed;
			Room?.Broadcast(new S_ChangeStat() { ObjectId = Id, StatInfo = Stat });
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

		// 시계 방향 순서. RotateDir에서 45도 단위 회전에 쓴다.
		static readonly MoveDir[] ClockwiseDirs =
		{
			MoveDir.Up, MoveDir.UpRight, MoveDir.Right, MoveDir.DownRight,
			MoveDir.Down, MoveDir.DownLeft, MoveDir.Left, MoveDir.UpLeft,
		};

		// steps × 45도 회전한 방향. 양수 = 시계 방향(오른쪽), 음수 = 반시계 방향(왼쪽)
		public static MoveDir RotateDir(MoveDir dir, int steps)
		{
			int index = Array.IndexOf(ClockwiseDirs, dir);
			if (index < 0)
				return dir;
			int count = ClockwiseDirs.Length;
			return ClockwiseDirs[((index + steps) % count + count) % count];
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

		// 피해 판정(Combat.Damage)을 마친 피해를 받는다. 받는 쪽 배율은 여기서 곱한다(방어율 상한 뒤).
		//   × 받는 피해 배율(원혼) × (1 + 취약) × (1 − 버팀), 그다음 수호가 있으면 일부를 시전자가 대신 받는다.
		// redirected: 수호로 넘어온 피해. 다시 넘기지 않는다
		public virtual void OnDamaged(GameObject attacker, int damage, bool critical, bool redirected = false)
		{
			if (IsAlive == false)
				return;

			float scaled = damage * DamageTakenRate * (1f + Status.Value(StatusId.Vulnerable)) * (1f - Status.Value(StatusId.Brace));
			damage = Math.Max((int)CombatConfig.Get(CombatConfig.MinDamage), (int)Math.Round(scaled));

			StatusInstance guard = redirected ? null : Status.Get(StatusId.Guard);
			if (guard != null && guard.Source != null && guard.Source != this && guard.Source.IsAlive && guard.Source.Room == Room)
			{
				int shared = (int)Math.Round(damage * Status.Value(StatusId.Guard));
				if (shared > 0)
				{
					damage = Math.Max(0, damage - shared);
					guard.Source.OnDamaged(attacker, shared, critical, redirected: true);
				}
			}

			Stat.Hp = Math.Max(Stat.Hp - damage, 0);
			Room.Broadcast(new S_ChangeHp() { ObjectId = Id, Hp = Stat.Hp, Amount = damage, Critical = critical });

			if (Stat.Hp <= 0)
				OnDead(attacker);
		}

		// 회복. 최대 체력을 넘는 양은 버린다('캐릭터 조작 및 전투' 2-7)
		public virtual void OnHealed(GameObject healer, int amount, bool critical)
		{
			if (IsAlive == false)
				return;

			int healed = Math.Min(amount, Stat.MaxHp - Stat.Hp);
			Stat.Hp += healed;
			Room.Broadcast(new S_ChangeHp() { ObjectId = Id, Hp = Stat.Hp, Amount = healed, Critical = critical, Heal = true });
		}

		public virtual void OnDead(GameObject attacker)
		{
			if (Room == null)
				return;

			Status.Clear();

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
