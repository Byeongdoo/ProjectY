using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class MyPlayerController : PlayerController
{
	bool _moveKeyPressed = false;

	protected override void Init()
	{
		base.Init();
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
				break;
		}

		base.UpdateController();
	}

	protected override void UpdateIdle()
	{
		// 이동 상태로 갈지 확인
		if (_moveKeyPressed)
		{
			State = CreatureState.Moving;
			return;
		}

		// Shift+방향키로 제자리에서 방향만 바꾼 경우 서버에 알린다
		CheckUpdatedFlag();

		if (_coSkillCooltime == null && Input.GetKey(KeyCode.Space))
		{
			Debug.Log("Skill !");

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

	void LateUpdate()
	{
		Camera.main.transform.position = new Vector3(transform.position.x, transform.position.y, -10);
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

		if (x == 0 && y == 0)
		{
			_moveKeyPressed = false;
			return;
		}

		Dir = GetDirFromVec(new Vector3Int(x, y, 0));

		// Shift+방향키는 이동 없이 방향만 바꾼다 (평타가 바라보는 방향으로만 나가므로)
		bool turnOnly = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		_moveKeyPressed = !turnOnly;
	}

	protected override void MoveToNextPos()
	{
		if (_moveKeyPressed == false)
		{
			State = CreatureState.Idle;
			CheckUpdatedFlag();
			return;
		}

		Vector3Int destPos = CellPos + DirToVec(Dir);

		if (Managers.Map.CanMove(CellPos, destPos))
		{
			if (Managers.Object.FindCreature(destPos) == null)
			{
				CellPos = destPos;
			}
		}

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
