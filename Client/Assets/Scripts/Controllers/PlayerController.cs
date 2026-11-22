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

	// 주인공(무당) 스프라이트처럼 8방향 클립(IDLE_SOUTH_EAST, RUN_NORTH …)을 가진 프리팹이면 켠다.
	// 끄면 레거시 임시 스프라이트(앞·뒤·옆 3종 + 뒤집기) 규칙을 쓴다.
	[SerializeField] bool _eightDirClips = false;

	static string EightDirSuffix(MoveDir dir)
	{
		switch (dir)
		{
			case MoveDir.Up: return "NORTH";
			case MoveDir.Down: return "SOUTH";
			case MoveDir.Left: return "WEST";
			case MoveDir.Right: return "EAST";
			case MoveDir.UpLeft: return "NORTH_WEST";
			case MoveDir.UpRight: return "NORTH_EAST";
			case MoveDir.DownLeft: return "SOUTH_WEST";
			default: return "SOUTH_EAST";
		}
	}

	protected override void UpdateAnimation()
	{
		if (_animator == null || _sprite == null)
			return;

		if (_eightDirClips)
		{
			// 원혼 프리팹(Prefabs/Creature/Spirit)은 ATTACK·DIE 클립이 있다. 무당 프리팹처럼 없으면 서 있는 모습으로 둔다.
			string action;
			if (State == CreatureState.Moving) action = "RUN_";
			else if (State == CreatureState.Skill) action = "ATTACK_";
			else if (State == CreatureState.Dead) action = "DIE_";
			else action = "IDLE_";

			string clip = action + EightDirSuffix(Dir);
			if (_animator.HasState(0, Animator.StringToHash(clip)) == false)
				clip = "IDLE_" + EightDirSuffix(Dir);
			_animator.Play(clip);
			_sprite.flipX = false;
			return;
		}

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
