using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using UnityEngine;
using static Define;

public class BaseController : MonoBehaviour
{
	public int Id { get; set; }

	StatInfo _stat = new StatInfo();
	public virtual StatInfo Stat
	{
		get { return _stat; }
		set
		{
			if (_stat.Equals(value))
				return;

			_stat.Level = value.Level;
			_stat.Attack = value.Attack;
			_stat.Hp = value.Hp;
			_stat.MaxHp = value.MaxHp;
			_stat.Speed = value.Speed;
			_stat.Spirit = value.Spirit;
			_stat.MaxSpirit = value.MaxSpirit;
		}
	}

	public float Speed
	{
		get { return Stat.Speed; }
		set { Stat.Speed = value; }
	}

	public virtual int Hp
	{
		get { return Stat.Hp; }
		set
		{
			Stat.Hp = value;
		}
	}

	protected bool _updated = false;

	// 대각선 1칸 소요 시간 배율 (configs 20030)
	static float DiagonalTimeScale => Table.TableManager.Configs.Get(20030).value[0];

	PositionInfo _positionInfo = new PositionInfo();
	public PositionInfo PosInfo
	{
		get { return _positionInfo; }
		set
		{
			if (_positionInfo.Equals(value))
				return;

			CellPos = new Vector3Int(value.PosX, value.PosY, 0);
			State = value.State;
			Dir = value.MoveDir;
		}
	}

	public void SyncPos()
	{
		Vector3 destPos = Managers.Map.CurrentGrid.CellToWorld(CellPos) + new Vector3(0.5f, 0.5f);
		transform.position = destPos;
	}

	public Vector3Int CellPos
	{
		get
		{
			return new Vector3Int(PosInfo.PosX, PosInfo.PosY, 0);
		}

		set
		{
			if (PosInfo.PosX == value.x && PosInfo.PosY == value.y)
				return;

			PosInfo.PosX = value.x;
			PosInfo.PosY = value.y;
			_updated = true;
		}
	}

	protected Animator _animator;
	protected SpriteRenderer _sprite;

	public virtual CreatureState State
	{
		get { return PosInfo.State; }
		set
		{
			if (PosInfo.State == value)
				return;

			PosInfo.State = value;
			UpdateAnimation();
			_updated = true;
		}
	}

	public MoveDir Dir
	{
		get { return PosInfo.MoveDir; }
		set
		{
			if (PosInfo.MoveDir == value)
				return;

			PosInfo.MoveDir = value;

			UpdateAnimation();
			_updated = true;
		}
	}

	public static Vector3Int DirToVec(MoveDir dir)
	{
		switch (dir)
		{
			case MoveDir.Up: return new Vector3Int(0, 1, 0);
			case MoveDir.Down: return new Vector3Int(0, -1, 0);
			case MoveDir.Left: return new Vector3Int(-1, 0, 0);
			case MoveDir.Right: return new Vector3Int(1, 0, 0);
			case MoveDir.UpLeft: return new Vector3Int(-1, 1, 0);
			case MoveDir.UpRight: return new Vector3Int(1, 1, 0);
			case MoveDir.DownLeft: return new Vector3Int(-1, -1, 0);
			case MoveDir.DownRight: return new Vector3Int(1, -1, 0);
		}
		return Vector3Int.zero;
	}

	// 벡터의 부호만 보고 8방향 중 하나로 바꾼다. (0, 0)이면 Down
	public static MoveDir GetDirFromVec(Vector3Int dir)
	{
		int sx = System.Math.Sign(dir.x);
		int sy = System.Math.Sign(dir.y);

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

	public Vector3Int GetFrontCellPos()
	{
		return CellPos + DirToVec(Dir);
	}

	// 임시 스프라이트(Tiny RPG)는 앞·뒤·옆 3종뿐이라 대각선은 옆 모습으로 보여준다.
	// 8방향 스프라이트가 들어오면 이 매핑을 방향별 클립으로 교체한다.
	protected void GetAnimDir(out string suffix, out bool flipX)
	{
		switch (Dir)
		{
			case MoveDir.Up:
				suffix = "BACK"; flipX = false; return;
			case MoveDir.Down:
				suffix = "FRONT"; flipX = false; return;
			case MoveDir.Left:
			case MoveDir.UpLeft:
			case MoveDir.DownLeft:
				suffix = "RIGHT"; flipX = true; return;
			default:
				suffix = "RIGHT"; flipX = false; return;
		}
	}

	protected virtual void UpdateAnimation()
	{
		if (_animator == null || _sprite == null)
			return;

		string prefix;
		if (State == CreatureState.Idle)
			prefix = "IDLE_";
		else if (State == CreatureState.Moving)
			prefix = "WALK_";
		else if (State == CreatureState.Skill)
			prefix = "ATTACK_";
		else
			return;

		GetAnimDir(out string suffix, out bool flipX);
		_animator.Play(prefix + suffix);
		_sprite.flipX = flipX;
	}

	void Start()
	{
		Init();
	}

	void Update()
	{
		UpdateController();
	}

	protected virtual void Init()
	{
		_animator = GetComponent<Animator>();
		_sprite = GetComponent<SpriteRenderer>();
		Vector3 pos = Managers.Map.CurrentGrid.CellToWorld(CellPos) + new Vector3(0.5f, 0.5f);
		transform.position = pos;

		UpdateAnimation();
	}

	protected virtual void UpdateController()
	{
		switch (State)
		{
			case CreatureState.Idle:
				UpdateIdle();
				break;
			case CreatureState.Moving:
				UpdateMoving();
				break;
			case CreatureState.Skill:
				UpdateSkill();
				break;
			case CreatureState.Dead:
				UpdateDead();
				break;
		}
	}

	protected virtual void UpdateIdle()
	{
	}

	// 스르륵 이동하는 것을 처리
	protected virtual void UpdateMoving()
	{
		Vector3 destPos = Managers.Map.CurrentGrid.CellToWorld(CellPos) + new Vector3(0.5f, 0.5f);
		Vector3 moveDir = destPos - transform.position;

		// 대각선도 1칸이라 축별 최대 거리(체비셰프) 기준으로 진행한다.
		// 대각선 구간은 configs 20030 배율만큼 시간을 더 써서 화면상 속도를 직선과 맞춘다(서버 몬스터 이동 간격과 같은 배율)
		bool diagonal = Mathf.Abs(moveDir.x) > 0.001f && Mathf.Abs(moveDir.y) > 0.001f;
		float step = (diagonal ? Speed / DiagonalTimeScale : Speed) * Time.deltaTime;
		float dist = Mathf.Max(Mathf.Abs(moveDir.x), Mathf.Abs(moveDir.y));
		if (dist < step)
		{
			transform.position = destPos;
			MoveToNextPos();
		}
		else
		{
			transform.position += moveDir * (step / dist);
			State = CreatureState.Moving;
		}
	}

	protected virtual void MoveToNextPos()
	{

	}

	protected virtual void UpdateSkill()
	{

	}

	protected virtual void UpdateDead()
	{

	}
}
