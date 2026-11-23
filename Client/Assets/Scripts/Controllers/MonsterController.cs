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

	bool _castPose;

	// 예고 자세 중에 맞아도 히트스톱이 끝난 뒤 자세가 풀리지 않게 한다
	protected override bool HoldsPose => _castPose;

	// 망령 평타(skillId 0)와 특수기(monsterSkills ID = characterID×10 + 슬롯).
	// 동작을 한 번 처음부터 틀고 대기 모습으로 돌아간다(서버는 제자리에서 칠 때 S_Move를 다시 보내지 않는다).
	// 특수기는 슬롯에 맞는 SKILL_1/SKILL_2 클립(없으면 서 있는 모습).
	public override void UseSkill(int skillId, int targetId, float castTime)
	{
		if (State == CreatureState.Dead)
			return;

		ReleaseCastPose();
		_skillAction = skillId == 0 ? "ATTACK_" : $"SKILL_{skillId % 10}_";
		State = CreatureState.Skill;
		UpdateAnimation(); // 연속 공격이면 상태가 그대로여도 클립을 처음부터 다시 튼다

		if (_coSkill != null)
			StopCoroutine(_coSkill);
		_coSkill = StartCoroutine(CoBackToIdle(EightDirClipLength(_skillAction, 0.5f)));
	}

	IEnumerator CoBackToIdle(float seconds)
	{
		yield return new WaitForSeconds(seconds);
		if (State == CreatureState.Skill)
			State = CreatureState.Idle;
		_coSkill = null;
	}

	// 특수기 예고 중(S_SkillWarning): 특수기 클립 첫 프레임에서 멈춰 준비 자세를 유지한다('[1장] 개체' 문서 skill1·skill2)
	public void BeginCastPose(int skillId)
	{
		if (State == CreatureState.Dead || _animator == null)
			return;

		if (_coSkill != null)
		{
			StopCoroutine(_coSkill);
			_coSkill = null;
		}
		_castPose = true;
		_skillAction = $"SKILL_{skillId % 10}_";
		State = CreatureState.Skill;
		UpdateAnimation();
		_animator.speed = 0f;
	}

	// 예고 취소(durationMs = 0)
	public void EndCastPose()
	{
		if (_castPose == false)
			return;
		ReleaseCastPose();
		State = CreatureState.Idle;
		UpdateAnimation();
	}

	void ReleaseCastPose()
	{
		_castPose = false;
		if (_animator != null)
			_animator.speed = 1f;
	}

	public override void OnDead()
	{
		ReleaseCastPose();
		base.OnDead();
	}
}
