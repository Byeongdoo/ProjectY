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
		if (MultiScene.CombatEnabled)
			CombatHud.Ensure();
	}

	// 대상 선택 중·시전 중·이동 불가 상태이상(기절·결박·버팀)이면 방향키로 움직이지 않는다
	bool MoveBlocked => TargetSelect.Active || MyCombat.IsCasting || IsMoveLocked;

	protected override void UpdateController()
	{
		MyCombat.Update();
		if (MultiScene.CombatEnabled)
		{
			// 이 프레임에 대상 선택이 키를 먼저 썼으면(같은 스킬 키로 확정 등) 전투 키로 다시 읽지 않는다
			bool selectingThisFrame = TargetSelect.Active;
			TargetSelect.Update();
			// 시전 중 Esc = 시전 취소(신력·쿨타임 소모 없음)
			if (MyCombat.IsCasting && TargetSelect.Active == false && Input.GetKeyDown(KeyCode.Escape))
				Managers.Network.Send(new C_CancelSkill());

			// F 홀드: 옆 칸의 혼절한 파티원 혼 부르기, 보스방 불씨. 무엇을 할지는 서버가 정한다. 떼면(또는 움직이면) 끊긴다
			if (IsFainted == false && TargetSelect.Active == false && Input.GetKeyDown(KeyCode.F))
				Managers.Network.Send(new C_HoldInteract() { Holding = true });
			if (Input.GetKeyUp(KeyCode.F))
				Managers.Network.Send(new C_HoldInteract() { Holding = false });

			// 전투 키는 서 있을 때뿐 아니라 걷는 중·동작 중에도 받아 둔다(선행 입력)
			if (SpiritSelectPanel.IsOpen == false && selectingThisFrame == false)
				UpdateCombatInput();

			UpdateDebugInput();
		}

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

	// 일어났다. 예측 이동 상태를 버리고 서버가 정한 칸에서 시작한다
	public override void OnRevived(int hp, PositionInfo posInfo)
	{
		base.OnRevived(hp, posInfo);
		_fromCell = CellPos;
		_inputBuffer.Clear();
		_updated = false;
	}

	// 서버가 보낸 내 위치. 예측 위치와 허용 오차(configs 20049)보다 멀어졌을 때만 서버 위치로 맞춘다.
	// forced(밀치기·돌진·물러서기·이동 거절)면 오차와 상관없이 서버 위치·방향으로 바로 맞춘다.
	public void OnServerPosition(PositionInfo serverPos, int tolerance, bool forced = false)
	{
		Vector3Int server = new Vector3Int(serverPos.PosX, serverPos.PosY, 0);
		Vector3Int diff = server - CellPos;
		if (forced == false && Mathf.Max(Mathf.Abs(diff.x), Mathf.Abs(diff.y)) <= tolerance)
			return;

		if (forced)
			Dir = serverPos.MoveDir;
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

		// 전투 입력(Space·1~4·Tab)은 UpdateController가 받는다(UpdateCombatInput)
	}

	static readonly KeyCode[] SkillKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };
	const int TargetInstant = 0, TargetEnemy = 1, TargetAlly = 2;
	const int ConfigInputBuffer = 20023; // 스킬 선행 입력 허용 시간(초)
	const int BufferedBasic = -1;

	// 선행 입력('캐릭터 조작 및 전투' 2-5, configs 20023): 지금 못 쓰는 키(공용 재사용 대기·평타 간격·시전 끝 직전·걷는 중)를
	// 이 시간 동안 받아 두었다가 쓸 수 있게 되는 순간 낸다. 그 안에 못 쓰면 버린다. 가장 마지막에 누른 키 하나만 기억한다.
	int _bufferedSlot;     // 0 = 없음, 1~4 = 원혼 스킬 슬롯, BufferedBasic = 평타
	float _bufferedUntil;

	// 스킬·평타는 칸 사이를 걷는 중에는 내지 않는다(칸에 도착한 뒤 낸다). 공격·스킬 동작 중에는 다음 동작을 낼 수 있다
	bool CanActNow => State != CreatureState.Moving && State != CreatureState.Dead && MyCombat.IsCasting == false;

	// 전투 입력('캐릭터 조작 및 전투' 2-2). 판정은 서버가 하고, 여기서는 쓸 수 없는 요청만 미리 거른다.
	void UpdateCombatInput()
	{
		if (TargetSelect.Active || IsFainted || HasStatus(StatusStun))
		{
			_bufferedSlot = 0;
			return;
		}

		// Tab: 가까운 적부터 현재 대상 순환
		if (Input.GetKeyDown(KeyCode.Tab))
			TargetSelect.CycleEnemyTarget();

		float window = Table.TableManager.Configs.Get(ConfigInputBuffer).value[0];
		for (int i = 0; i < SkillKeys.Length; i++)
			if (Input.GetKeyDown(SkillKeys[i]))
				Buffer(i + 1, window);
		if (Input.GetKeyDown(KeyCode.Space) && _bufferedSlot == 0)
			Buffer(BufferedBasic, window);

		if (_bufferedSlot != 0)
		{
			if (Time.time > _bufferedUntil)
				_bufferedSlot = 0;
			else if (TryFire(_bufferedSlot))
				_bufferedSlot = 0;
			return;
		}

		// Space 홀드: 평타 반복
		if (Input.GetKey(KeyCode.Space))
			TryFire(BufferedBasic);
	}

	// 개발·테스트 키(에디터·개발 빌드만). 서버 config.json debugCommands가 꺼져 있으면 서버가 무시한다.
	//   F9 = 앞 칸에 허수아비, F10 = 허수아비 모두 정리, F11 = 체력·신력 채우고 쿨타임 초기화,
	//   F12 = 테이블 다시 읽기(클라·서버) + 이 던전 한 판 새로 시작 — xlsx 수정 → 변환기 실행 → F12
	void UpdateDebugInput()
	{
		if (Application.isEditor == false && Debug.isDebugBuild == false)
			return;

		DebugCommand command = DebugCommand.DebugNone;
		if (Input.GetKeyDown(KeyCode.F9)) command = DebugCommand.DebugSpawnDummy;
		else if (Input.GetKeyDown(KeyCode.F10)) command = DebugCommand.DebugClearDummies;
		else if (Input.GetKeyDown(KeyCode.F11)) command = DebugCommand.DebugRefill;
		else if (Input.GetKeyDown(KeyCode.F12)) command = DebugCommand.DebugReload;
		if (command == DebugCommand.DebugNone)
			return;

		if (command == DebugCommand.DebugReload && ReloadClientTables() == false)
			return; // 클라가 새 JSON을 못 읽으면 서버도 바꾸지 않는다(양쪽 수치가 어긋나지 않게)

		Managers.Network.Send(new C_DebugCommand() { Command = command });
		if (command == DebugCommand.DebugRefill || command == DebugCommand.DebugReload)
			MyCombat.ClearCooldowns();
	}

	// 변환기가 Unity 밖에서 쓴 JSON을 다시 가져와(에디터) 클라 테이블을 새로 읽는다
	static bool ReloadClientTables()
	{
#if UNITY_EDITOR
		UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/Data", UnityEditor.ImportAssetOptions.ImportRecursive | UnityEditor.ImportAssetOptions.ForceUpdate);
#endif
		try
		{
			Table.TableManager.LoadAll(new ResourcesTableReader());
			Debug.Log("[Debug] 클라 테이블 다시 읽음");
			return true;
		}
		catch (System.Exception e)
		{
			Debug.LogError($"[Debug] 클라 테이블 다시 읽기 실패: {e.Message}");
			return false;
		}
	}

	void Buffer(int slot, float window)
	{
		_bufferedSlot = slot;
		_bufferedUntil = Time.time + window;
	}

	// 지금 낼 수 있으면 내고 true. 아직 못 내면 false(받아 둔 채로 다음 프레임에 다시 본다)
	bool TryFire(int slot)
	{
		if (CanActNow == false)
			return false;

		// 평타. 근접 폭·원거리 사거리는 서버가 spirits 테이블로 정한다
		if (slot == BufferedBasic)
		{
			if (MyCombat.CanSendBasic() == false)
				return false;
			Managers.Network.Send(new C_Skill() { Info = new SkillInfo() { SkillId = 0 } });
			MyCombat.OnBasicSent();
			return true;
		}

		// 원혼 스킬. 단일 대상이면 대상 선택 상태로, 즉시·방향이면 바로 요청
		Table.SpiritSkillsRow skill = MyCombat.SkillInSlot(slot);
		if (skill == null)
			return true; // 빈 슬롯: 버린다
		if (MyCombat.CanUse(skill) == false)
			return false;

		if (skill.targetType == TargetEnemy || skill.targetType == TargetAlly)
			TargetSelect.Begin(skill, SkillKeys[slot - 1]);
		else
			Managers.Network.Send(new C_Skill() { Info = new SkillInfo() { SkillId = skill.skillID } });
		return true;
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
		// 원혼 선택 화면·대상 선택 중에는 방향키를 그쪽이 쓴다
		if (SpiritSelectPanel.IsOpen || TargetSelect.Active)
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
		// 결박·버팀 중에도 돌아설 수는 있다. 기절·시전 중에는 돌아서지 않는다
		if (State == CreatureState.Idle && HasStatus(StatusStun) == false && MyCombat.IsCasting == false)
			Dir = GetDirFromVec(new Vector3Int(x, y, 0));

		// Shift+방향키는 이동 없이 방향만 바꾼다 (평타가 바라보는 방향으로만 나가므로)
		_moveKeyPressed = !_turnOnly && !MoveBlocked;
	}

	protected override void MoveToNextPos()
	{
		if (_turnOnly || SpiritSelectPanel.IsOpen || MoveBlocked)
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
