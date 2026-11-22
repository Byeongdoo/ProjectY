using Table;
using UnityEngine;

// 이승(싱글) 주인공 조작. 방향키 8방향 칸 이동, 두 키 동시 입력은 대각선(1칸), Shift+방향키는 제자리 방향 전환.
// 서버·패킷 없이 클라 단독으로 동작한다.
//
// 이동 중 입력 보정(대각선 전환·즉시 되돌아가기)은 StepInputAssist 규칙을 따른다.
[RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
public class IseungPlayerController : MonoBehaviour
{
	[SerializeField] IseungMap _map;
	[SerializeField] Vector3Int _cellPos;
	[SerializeField] IseungDir _dir = IseungDir.SOUTH;
	// 주인공 characterLevel 그룹. 레벨은 로컬 세이브가 생기면 거기서 읽는다(현재 1).
	[SerializeField] int _characterId = 1000001;
	[SerializeField] int _level = 1;

	const int ConfigDiagonalTimeScale = 20030;  // 대각선 1칸 소요 시간 배율
	const int ConfigDiagonalUpgradeWindow = 20048; // 이동 중 대각선 전환 허용 시간(초)

	enum State { Idle, Moving }

	Animator _animator;
	State _state = State.Idle;
	float _moveSpeed;            // 칸/초 (characterLevel.moveSpeed)
	float _diagonalTimeScale;
	float _upgradeWindow;
	Vector3Int _fromCell;        // 진행 중인 한 칸의 출발 칸
	float _stepElapsed;          // 진행 중인 한 칸을 시작한 뒤 지난 시간
	bool _stepAdjusted;          // 이번 칸에서 이미 대각선 전환·되돌아가기를 했는가
	int _inputX, _inputY;
	bool _turnOnly;
	string _playingClip;

	public Vector3Int CellPos => _cellPos;
	public IseungDir Dir => _dir;
	public bool IsMoving => _state == State.Moving;

	void Awake()
	{
		_animator = GetComponent<Animator>();
		if (TableManager.CharacterLevel == null)
			TableManager.LoadAll(new ResourcesTableReader());
		_moveSpeed = TableManager.CharacterLevel.Get((_characterId, _level)).moveSpeed;
		_diagonalTimeScale = TableManager.Configs.Get(ConfigDiagonalTimeScale).value[0];
		_upgradeWindow = TableManager.Configs.Get(ConfigDiagonalUpgradeWindow).value[0];
	}

	void Start()
	{
		transform.position = _map.CellCenter(_cellPos);
		PlayAnimation();
	}

	// 확인 창·전환 연출 중에는 조작을 막는다. 진행 중인 한 칸은 끝까지 간다.
	public bool InputLocked { get; set; }

	void Update()
	{
		ReadInput();

		if (InputLocked)
		{
			_inputX = 0; _inputY = 0; _turnOnly = false;
		}
		else if (_state == State.Idle && Input.GetKeyDown(KeyCode.F) && TryInteract())
		{
			_inputX = 0; _inputY = 0;
		}

		if (_state == State.Idle)
			TryStartStep();
		else
			TryAdjustStep();

		if (_state == State.Moving)
			UpdateMoving(Time.deltaTime);
	}

	// 반대 방향 키는 서로 상쇄된다.
	void ReadInput()
	{
		_inputX = 0; _inputY = 0;
		if (Input.GetKey(KeyCode.UpArrow)) _inputY += 1;
		if (Input.GetKey(KeyCode.DownArrow)) _inputY -= 1;
		if (Input.GetKey(KeyCode.LeftArrow)) _inputX -= 1;
		if (Input.GetKey(KeyCode.RightArrow)) _inputX += 1;
		_turnOnly = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
	}

	bool HasDirInput => _inputX != 0 || _inputY != 0;

	// F 상호작용: 바라보는 앞 칸의 대상(신단·NPC·조사 대상). 상호작용 칸 1칸을 앞에 비워 두는 레벨 규칙과 맞춘다.
	bool TryInteract()
	{
		IseungInteractable target = IseungInteractable.FindAt(_cellPos + IseungDirUtil.ToVec(_dir));
		if (target == null)
			return false;
		target.Interact(this);
		return true;
	}

	// 칸 경계에서 다음 칸을 정한다. 막힌 쪽을 누르면 그쪽을 바라보기만 한다.
	void TryStartStep()
	{
		if (HasDirInput == false)
		{
			// 칸 도착 직후에는 상태를 이미 Idle로 돌려 둔 채 들어오므로 클립을 직접 맞춘다.
			SetState(State.Idle);
			PlayAnimation();
			return;
		}

		_dir = IseungDirUtil.FromVec(_inputX, _inputY);
		Vector3Int dest = _cellPos + IseungDirUtil.ToVec(_dir);
		if (_turnOnly == false && _map.CanMove(_cellPos, dest))
		{
			_fromCell = _cellPos;
			_cellPos = dest;
			_stepElapsed = 0;
			_stepAdjusted = false;
			SetState(State.Moving);
		}
		else
		{
			SetState(State.Idle);
		}
		PlayAnimation();
	}

	// 칸 이동 도중의 입력 반영(규칙은 StepInputAssist, 멀티 컨트롤러와 공용).
	void TryAdjustStep()
	{
		if (_stepAdjusted || _turnOnly)
			return;

		var kind = StepInputAssist.TryAdjust(_fromCell, _cellPos, _inputX, _inputY, _stepElapsed, _upgradeWindow, out var from, out var dest);
		// 되돌아가는 칸은 방금 지나온 칸이라 막혀 있지 않다.
		if (kind == StepInputAssist.Kind.Reverse || (kind == StepInputAssist.Kind.Diagonal && _map.CanMove(from, dest)))
			Retarget(from, dest);
	}

	void Retarget(Vector3Int from, Vector3Int dest)
	{
		_fromCell = from;
		_cellPos = dest;
		_dir = IseungDirUtil.FromVec(dest.x - from.x, dest.y - from.y);
		_stepAdjusted = true;
		PlayAnimation();
	}

	// 칸/초 기준 진행 속도. 대각선은 configs 20030 배율(√2)만큼 시간이 걸려 화면상 속도가 직선과 같다.
	float CurrentStepSpeed()
	{
		Vector3Int step = _cellPos - _fromCell;
		bool diagonal = step.x != 0 && step.y != 0;
		return diagonal ? _moveSpeed / _diagonalTimeScale : _moveSpeed;
	}

	void UpdateMoving(float deltaTime)
	{
		_stepElapsed += deltaTime;
		Vector3 dest = _map.CellCenter(_cellPos);
		Vector3 delta = dest - transform.position;

		// 대각선도 1칸(기획)이라 축별 최대 거리(체비셰프)로 진행한다.
		float speed = CurrentStepSpeed();
		float dist = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
		float step = speed * deltaTime;
		if (dist > step)
		{
			transform.position += delta * (step / dist);
			return;
		}

		transform.position = dest;
		float leftoverTime = (step - dist) / speed;
		_state = State.Idle;
		TryStartStep();
		// 남은 시간을 다음 칸에 이어 써서 연속 이동 중 프레임마다 멈칫하지 않게 한다.
		if (_state == State.Moving && leftoverTime > 0)
			UpdateMoving(leftoverTime);
	}

	void SetState(State state)
	{
		if (_state == state) return;
		_state = state;
		PlayAnimation();
	}

	void PlayAnimation()
	{
		string clip = (_state == State.Moving ? "RUN_" : "IDLE_") + _dir;
		if (clip == _playingClip) return;
		_playingClip = clip;
		_animator.Play(clip);
	}
}
