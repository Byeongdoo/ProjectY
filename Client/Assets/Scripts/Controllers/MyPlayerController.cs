using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class MyPlayerController : PlayerController
{
	const int ConfigDiagonalUpgradeWindow = 20048; // 이동 중 대각선 전환 허용 시간(초)

	bool _moveKeyPressed = false;
	bool _turnOnly = false;
	int _inputX, _inputY;
	StepInputBuffer _inputBuffer; // 이동 중에 눌렀다 뗀 방향키(칸 도착 때 반영, 이승과 공용 규칙)

	// 진행 중인 한 칸. 이동 중 입력 보정(StepInputAssist, 이승과 공용)에 쓴다.
	Vector3Int _fromCell;
	float _stepElapsed;
	bool _stepAdjusted;
	float _upgradeWindow;

	protected override void Init()
	{
		base.Init();
		_upgradeWindow = Table.TableManager.Configs.Get(ConfigDiagonalUpgradeWindow).value[0];
	}

	protected override void UpdateController()
	{
		switch (State)
		{
			case CreatureState.Idle:
				GetDirInput();
				break;
			case CreatureState.Moving:
				GetDirInput();
				if (_moveKeyPressed)
					_inputBuffer.Record(_inputX, _inputY);
				_stepElapsed += Time.deltaTime;
				TryAdjustStep();
				break;
		}

		base.UpdateController();
	}

	// 클라는 서버 응답을 기다리지 않고 바로 목표 칸을 바꾼다(예측). 서버는 configs 20049 오차 안이면 받아들인다.
	void TryAdjustStep()
	{
		if (_stepAdjusted || _moveKeyPressed == false)
			return;

		var kind = StepInputAssist.TryAdjust(_fromCell, CellPos, _inputX, _inputY, _stepElapsed, _upgradeWindow, out var from, out var dest);
		if (kind == StepInputAssist.Kind.None)
			return;
		if (kind == StepInputAssist.Kind.Diagonal && (Managers.Map.CanMove(from, dest) == false || Managers.Object.FindBlocker(dest) != null))
			return;

		_fromCell = from;
		_stepAdjusted = true;
		_inputBuffer.Clear(); // 이번 칸을 바꾼 키는 이미 반영했다(되돌아가기가 두 칸이 되지 않게)
		Dir = GetDirFromVec(dest - from);
		CellPos = dest;
		CheckUpdatedFlag();
	}

	// 서버가 보낸 내 위치. 예측 위치와 허용 오차(configs 20049)보다 멀어졌을 때만 서버 위치로 맞춘다.
	public void OnServerPosition(PositionInfo serverPos, int tolerance)
	{
		Vector3Int server = new Vector3Int(serverPos.PosX, serverPos.PosY, 0);
		Vector3Int diff = server - CellPos;
		// 혼절에서 부활하면(서버 상태가 Dead가 아님) 거리와 상관없이 서버 위치로 옮긴다
		bool revived = State == CreatureState.Dead && serverPos.State != CreatureState.Dead;
		if (revived == false && Mathf.Max(Mathf.Abs(diff.x), Mathf.Abs(diff.y)) <= tolerance)
			return;

		CellPos = server;
		_fromCell = server;
		_inputBuffer.Clear();
		State = CreatureState.Idle;
		SyncPos();
		_updated = false;
	}

	protected override void UpdateIdle()
	{
		// 이동 상태로 갈지 확인. 막힌 쪽이면 그쪽을 바라보기만 한다(이승과 같은 반응, 벽 앞 제자리 달리기 방지)
		if (_moveKeyPressed)
		{
			Vector3Int destPos = CellPos + DirToVec(Dir);
			if (Managers.Map.CanMove(CellPos, destPos) && Managers.Object.FindBlocker(destPos) == null)
			{
				State = CreatureState.Moving;
				return;
			}
		}

		// Shift+방향키로 제자리에서 방향만 바꾼 경우 서버에 알린다
		CheckUpdatedFlag();

		// 나루터 원혼 선택 중에는 키 입력을 그 화면이 받는다
		if (SpiritSelectPanel.IsOpen)
			return;

		// F 상호작용: 바라보는 앞 칸의 대상(사공 등). 요청의 유효성(위치)은 서버가 다시 본다.
		if (Input.GetKeyDown(KeyCode.F))
		{
			NetInteractable target = NetInteractable.FindAt(GetFrontCellPos());
			if (target != null)
			{
				target.Interact();
				return;
			}
		}

		// 삼천도는 전투가 없다(Space·스킬 비활성). 서버도 로비 룸의 스킬 요청은 버린다.
		if (MultiScene.CombatEnabled == false)
			return;

		if (_coSkillCooltime == null && Input.GetKey(KeyCode.Space))
		{
			Debug.Log("Skill !");

			// 평타 요청. 원혼별 근접(앞 칸·3칸 베기)/원거리(화살) 형태는 서버가 spirits 테이블로 정해
			// S_Skill의 스킬 ID로 알려 주고, 연출은 그 ID를 따른다. 여기 ID는 원혼이 없을 때의 기본값이다.
			C_Skill skill = new C_Skill() { Info = new SkillInfo() };
			skill.Info.SkillId = 2;
			Managers.Network.Send(skill);

			_coSkillCooltime = StartCoroutine("CoInputCooltime", 0.2f);
		}
	}

	Coroutine _coSkillCooltime;
	IEnumerator CoInputCooltime(float time)
	{
		yield return new WaitForSeconds(time);
		_coSkillCooltime = null;
	}

	// 키보드 입력
	// 방향키 8방향: 두 키를 함께 누르면 대각선, 반대 방향 키는 서로 상쇄된다
	void GetDirInput()
	{
		int x = 0;
		int y = 0;
		if (Input.GetKey(KeyCode.UpArrow)) y += 1;
		if (Input.GetKey(KeyCode.DownArrow)) y -= 1;
		if (Input.GetKey(KeyCode.LeftArrow)) x -= 1;
		if (Input.GetKey(KeyCode.RightArrow)) x += 1;
		if (SpiritSelectPanel.IsOpen)
			x = y = 0;

		_inputX = x;
		_inputY = y;
		_turnOnly = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		if (x == 0 && y == 0)
		{
			_moveKeyPressed = false;
			return;
		}

		// 바라보는 방향은 칸 경계에서만 바꾼다(이동 중에는 진행 방향 유지, 이승과 같은 반응). 다음 칸 방향은 MoveToNextPos가 정한다.
		if (State == CreatureState.Idle)
			Dir = GetDirFromVec(new Vector3Int(x, y, 0));

		// Shift+방향키는 이동 없이 방향만 바꾼다 (평타가 바라보는 방향으로만 나가므로)
		_moveKeyPressed = !_turnOnly;
	}

	protected override void MoveToNextPos()
	{
		if (_turnOnly || SpiritSelectPanel.IsOpen)
		{
			_inputBuffer.Clear();
		}
		else
		{
			// 손을 뗐어도 이동 중에 누른 방향이 있으면 그 칸으로 이어 간다
			int x = _moveKeyPressed ? _inputX : 0;
			int y = _moveKeyPressed ? _inputY : 0;
			_inputBuffer.Resolve(CellPos - _fromCell, ref x, ref y);
			if (x != 0 || y != 0)
			{
				_inputX = x;
				_inputY = y;
				_moveKeyPressed = true;
			}
		}

		if (_moveKeyPressed == false)
		{
			State = CreatureState.Idle;
			CheckUpdatedFlag();
			return;
		}

		Dir = GetDirFromVec(new Vector3Int(_inputX, _inputY, 0));
		Vector3Int destPos = CellPos + DirToVec(Dir);

		if (Managers.Map.CanMove(CellPos, destPos))
		{
			if (Managers.Object.FindBlocker(destPos) == null)
			{
				_fromCell = CellPos;
				_stepElapsed = 0;
				_stepAdjusted = false;
				CellPos = destPos;
				CheckUpdatedFlag();
				return;
			}
		}

		// 막혔으면 그 방향을 바라보고 멈춘다
		State = CreatureState.Idle;
		CheckUpdatedFlag();
	}

	protected override void CheckUpdatedFlag()
	{
		if (_updated)
		{
			C_Move movePacket = new C_Move();
			movePacket.PosInfo = PosInfo;
			Managers.Network.Send(movePacket);
			_updated = false;
		}
	}
}
