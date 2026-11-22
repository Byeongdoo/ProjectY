using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class ArrowController : BaseController
{
	protected override void Init()
	{
		// 스프라이트가 위(Up)를 향해 그려져 있으므로 진행 방향에서 90도를 뺀다
		Vector3Int v = DirToVec(Dir);
		float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f;
		transform.rotation = Quaternion.Euler(0, 0, angle);

		State = CreatureState.Moving;

		base.Init();
	}

	protected override void UpdateAnimation()
	{

	}
}
