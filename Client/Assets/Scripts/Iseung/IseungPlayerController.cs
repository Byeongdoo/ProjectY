using Table;
using UnityEngine;

// 이승(싱글) 주인공 조작. 방향키 8방향 칸 이동, 두 키 동시 입력은 대각선(1칸), Shift+방향키는 제자리 방향 전환.
// 서버·패킷 없이 클라 단독으로 동작한다.
[RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
public class IseungPlayerController : MonoBehaviour
{
	[SerializeField] IseungMap _map;
	[SerializeField] Vector3Int _cellPos;
	[SerializeField] IseungDir _dir = IseungDir.SOUTH;
	// 주인공 characterLevel 그룹. 레벨은 로컬 세이브가 생기면 거기서 읽는다(현재 1).
	[SerializeField] int _characterId = 1000001;
	[SerializeField] int _level = 1;

	enum State { Idle, Moving }

	Animator _animator;
	State _state = State.Idle;
	float _moveSpeed;           // 칸/초 (characterLevel.moveSpeed)
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
	}

	void Start()
	{
		transform.position = _map.CellCenter(_cellPos);
		PlayAnimation();
	}

	void Update()
	{
		ReadInput();

		if (_state == State.Idle)
			TryStartStep();

		if (_state == State.Moving)
			UpdateMoving();
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

	// 칸 경계에서만 다음 칸을 정한다. 막힌 쪽을 누르면 그쪽을 바라보기만 한다.
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
			_cellPos = dest;
			SetState(State.Moving);
		}
		else
		{
			SetState(State.Idle);
		}
		PlayAnimation();
	}

	void UpdateMoving()
	{
		Vector3 dest = _map.CellCenter(_cellPos);
		Vector3 delta = dest - transform.position;

		// 대각선도 직선과 같은 시간에 1칸(기획: 대각선도 1칸)이라 축별 최대 거리(체비셰프)로 진행한다.
		float step = _moveSpeed * Time.deltaTime;
		float dist = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
		if (dist <= step)
		{
			transform.position = dest;
			float leftover = step - dist;
			_state = State.Idle;
			TryStartStep();
			// 남은 이동량을 다음 칸에 이어 써서 연속 이동 중 프레임마다 멈칫하지 않게 한다.
			if (_state == State.Moving && leftover > 0)
				transform.position += StepToward(_map.CellCenter(_cellPos) - transform.position, leftover);
		}
		else
		{
			transform.position += StepToward(delta, step);
		}
	}

	static Vector3 StepToward(Vector3 delta, float step)
	{
		float dist = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
		return dist <= 0 ? Vector3.zero : delta * Mathf.Min(1, step / dist);
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
