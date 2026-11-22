using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class PlayerController : CreatureController
{
	protected Coroutine _coSkill;
	protected bool _rangedSkill = false;

	protected override void Init()
	{
		base.Init();
	}

	protected override void UpdateAnimation()
	{
		if (_animator == null || _sprite == null)
			return;

		string prefix;
		if (State == CreatureState.Idle)
			prefix = "IDLE_";
		else if (State == CreatureState.Moving)
			prefix = "WALK_";
		else if (State == CreatureState.Skill)
			prefix = _rangedSkill ? "ATTACK_WEAPON_" : "ATTACK_";
		else
			return;

		GetAnimDir(out string suffix, out bool flipX);
		_animator.Play(prefix + suffix);
		_sprite.flipX = flipX;
	}

	protected override void UpdateController()
	{		
		base.UpdateController();
	}

	public override void UseSkill(int skillId)
	{
		if (skillId == 1)
		{
			_coSkill = StartCoroutine("CoStartPunch");
		}
		else if (skillId == 2)
		{
			_coSkill = StartCoroutine("CoStartShootArrow");
		}
	}

	protected virtual void CheckUpdatedFlag()
	{

	}

	IEnumerator CoStartPunch()
	{
		// 대기 시간
		_rangedSkill = false;
		State = CreatureState.Skill;
		yield return new WaitForSeconds(0.5f);
		State = CreatureState.Idle;
		_coSkill = null;
		CheckUpdatedFlag();
	}

	IEnumerator CoStartShootArrow()
	{
		// 대기 시간
		_rangedSkill = true;
		State = CreatureState.Skill;
		yield return new WaitForSeconds(0.3f);
		State = CreatureState.Idle;
		_coSkill = null;
		CheckUpdatedFlag();
	}

	public override void OnDamaged()
	{
		Debug.Log("Player HIT !");
	}
}
