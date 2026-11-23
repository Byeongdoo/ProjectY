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

	// characterInfos.characterID(보스 판별·보스 이펙트 묶음)
	public int CharacterId { get; set; }

	public bool IsBoss => Table.TableManager.CharacterInfos.TryGet(CharacterId, out Table.CharacterInfosRow row) && row.type == 3;

	int _bossPhase = 1;
	GameObject _exhaustAura, _phaseAura;

	// 보스 상태(S_BossState). 페이즈가 오르면 전환 동작(PHASE_TRANSITION)을 한 번 틀고 2페이즈 오라를 켠다
	public void SetBossPhase(int phase)
	{
		bool up = phase > _bossPhase;
		_bossPhase = phase;
		_phaseAura = BossVfx.SetAura(this, "Phase2Aura", _phaseAura, phase >= 2);
		if (up && State != CreatureState.Dead)
		{
			ReleaseCastPose();
			_skillAction = "PHASE_TRANSITION_";
			State = CreatureState.Skill;
			UpdateAnimation();
			if (_coSkill != null)
				StopCoroutine(_coSkill);
			_coSkill = StartCoroutine(CoBackToIdle(EightDirClipLength(_skillAction, 0.5f)));
		}
	}

	// 탈진(8013)이 걸리면 보스 오라를 켜고, 풀리면 끈다
	protected override void OnStatusChanged(int statusId)
	{
		if (statusId == StatusExhaust)
			_exhaustAura = BossVfx.SetAura(this, "ExhaustedAura", _exhaustAura, HasStatus(StatusExhaust));
	}

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
		_skillAction = skillId == 0 ? "ATTACK_" : SpecialAction(skillId);
		State = CreatureState.Skill;
		UpdateAnimation(); // 연속 공격이면 상태가 그대로여도 클립을 처음부터 다시 튼다

		// 보스 패턴 발동 이펙트: 예고 때 받아 둔 칸·표식 대상으로 보여 준다
		if (skillId != 0 && Table.TableManager.BossSkills.TryGet(skillId, out Table.BossSkillsRow boss))
			BossVfx.OnFire(this, boss, SkillWarningView.LastCells(Id), SkillWarningView.LastTargets(Id));

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
		_skillAction = SpecialAction(skillId);
		State = CreatureState.Skill;
		UpdateAnimation();
		_animator.speed = 0f;
	}

	// 특수기 동작 클립 접두어.
	//   보스(bossSkills): 패턴으로 고른다 — 탱커 강타 TANK_SLAM, 탈진이 붙는 대형 패턴 MOUNTAIN_WAIL,
	//   산개·디버프 THREAD_DEBUFF_CAST, 그 밖의 장판 ROOT_AREA_CAST(뒤틀린 목귀 리소스 기준. 클립이 없으면 서 있는 모습)
	//   일반 적(monsterSkills = characterID×10 + 슬롯): SKILL_1/SKILL_2
	static string SpecialAction(int skillId)
	{
		if (Table.TableManager.BossSkills.TryGet(skillId, out Table.BossSkillsRow boss))
		{
			if (boss.pattern == 1) return "TANK_SLAM_";
			if (boss.exhaustTime > 0) return "MOUNTAIN_WAIL_";
			if (boss.pattern == 3 || boss.pattern == 7) return "THREAD_DEBUFF_CAST_";
			return "ROOT_AREA_CAST_";
		}
		return $"SKILL_{skillId % 10}_";
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
		_exhaustAura = BossVfx.SetAura(this, "ExhaustedAura", _exhaustAura, false);
		_phaseAura = BossVfx.SetAura(this, "Phase2Aura", _phaseAura, false);
		BossHud.Remove(Id);
		base.OnDead();
	}
}
