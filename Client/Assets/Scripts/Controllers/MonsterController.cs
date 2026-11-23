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

	const int BasicSkillId = 1; // 서버 평타 S_Skill ID

	bool _castPose;

	// 서버가 평타(1)·특수기(monsterSkills ID)를 쓸 때마다 S_Skill을 보낸다.
	// 동작을 한 번 처음부터 보여 주고 대기 모습으로 돌아간다. 특수기는 슬롯에 맞는 SKILL_1/SKILL_2 클립(없으면 ATTACK).
	public override void UseSkill(int skillId)
	{
		if (State == CreatureState.Dead)
			return;

		if (_coSkill != null)
			StopCoroutine(_coSkill);
		ReleaseCastPose();
		State = CreatureState.Skill;
		if (_animator != null)
		{
			string clip = skillId == BasicSkillId ? null : SpecialClip(skillId);
			if (clip != null)
				_animator.Play(clip, 0, 0f);
			else
				_animator.Play(_animator.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, 0f);
		}
		_coSkill = StartCoroutine(CoSkill());
	}

	// 특수기 예고 중(S_SkillWarning): 특수기 클립 첫 프레임에서 멈춰 준비 자세를 유지한다('[1장] 개체' 문서 skill1·skill2)
	public void BeginCastPose(int skillId)
	{
		if (State == CreatureState.Dead || _animator == null)
			return;
		string clip = SpecialClip(skillId);
		if (clip == null)
			return;

		if (_coSkill != null)
		{
			StopCoroutine(_coSkill);
			_coSkill = null;
		}
		_castPose = true;
		_animator.Play(clip, 0, 0f);
		_animator.speed = 0f;
	}

	// 예고 취소(durationMs = 0)
	public void EndCastPose()
	{
		if (_castPose == false)
			return;
		ReleaseCastPose();
		UpdateAnimation();
	}

	void ReleaseCastPose()
	{
		_castPose = false;
		if (_animator != null)
			_animator.speed = 1f;
	}

	// monsterSkills ID의 슬롯(ID 끝자리 = characterID×10 + 슬롯)으로 SKILL_n_방향 클립을 고른다
	string SpecialClip(int skillId)
	{
		if (_animator == null || _eightDirClips == false)
			return null;
		string clip = $"SKILL_{skillId % 10}_{EightDirSuffix(Dir)}";
		return _animator.HasState(0, Animator.StringToHash(clip)) ? clip : null;
	}

	public override void OnDead()
	{
		ReleaseCastPose();
		base.OnDead();
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
