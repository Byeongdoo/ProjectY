using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class MonsterController : CreatureController
{
	Coroutine _coSkill;

	protected override void Init()
	{
		base.Init();
	}

	// 망령 프리팹(Prefabs/Creature/Monster)은 8방향 클립을 쓴다. 레거시 Monster 프리팹은 기존 규칙.
	protected override void UpdateAnimation()
	{
		if (_animator == null || _sprite == null)
			return;

		if (_eightDirClips)
		{
			PlayEightDirClip();
			return;
		}

		base.UpdateAnimation();
	}

	protected override void UpdateIdle()
	{
		base.UpdateIdle();
	}

	public override void OnDamaged()
	{
		//Managers.Object.Remove(Id);
		//Managers.Resource.Destroy(gameObject);
	}

	// 서버가 평타를 칠 때마다 S_Skill(1)을 보낸다. 공격 동작을 한 번 처음부터 보여 주고 대기 모습으로 돌아간다.
	public override void UseSkill(int skillId)
	{
		if (skillId != 1 || State == CreatureState.Dead)
			return;

		if (_coSkill != null)
			StopCoroutine(_coSkill);
		State = CreatureState.Skill;
		if (_animator != null)
			_animator.Play(_animator.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, 0f);
		_coSkill = StartCoroutine(CoSkill());
	}

	IEnumerator CoSkill()
	{
		yield return null; // Play는 다음 프레임에 반영된다
		float length = _animator != null ? _animator.GetCurrentAnimatorStateInfo(0).length : 0f;
		yield return new WaitForSeconds(length);
		if (State == CreatureState.Skill)
			State = CreatureState.Idle;
		_coSkill = null;
	}
}
