using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class PlayerController : CreatureController
{
	protected Coroutine _coSkill;
	protected bool _rangedSkill = false;

	// 저승 던전에서 빙의한 원혼(spirits.spiritID). 0 = 무당 본모습
	public int SpiritId { get; set; }

	// 혼절(S_Die faintTime). 이 시각에 마지막 불씨에서 저절로 일어난다
	public float FaintEndTime { get; private set; }
	public bool IsFainted => State == CreatureState.Dead;

	// F 홀드 진행(S_Channel)
	public ChannelKind Channel { get; private set; }
	public float ChannelStart { get; private set; }
	public float ChannelEnd { get; private set; }

	public void OnFainted(float faintTime)
	{
		CancelSkill();
		foreach (int id in new List<int>(Statuses.Keys))
			SetStatus(id, 0f, 0);
		SetChannel(ChannelKind.ChannelNone, 0f);
		FaintEndTime = Time.time + faintTime;
		Hp = 0;
		State = CreatureState.Dead;
	}

	public virtual void OnRevived(int hp, PositionInfo posInfo)
	{
		FaintEndTime = 0f;
		PosInfo = posInfo;
		State = CreatureState.Idle;
		SyncPos();
		Hp = hp;
	}

	public void SetChannel(ChannelKind kind, float duration)
	{
		Channel = duration > 0f ? kind : ChannelKind.ChannelNone;
		ChannelStart = Time.time;
		ChannelEnd = Time.time + duration;
	}

	const int ConfigBasicInterval = 20032; // 평타 간격(초) — 클립 길이를 모를 때 동작 유지 시간

	protected override void Init()
	{
		base.Init();
	}

	protected override void UpdateAnimation()
	{
		if (_animator == null || _sprite == null)
			return;

		if (_eightDirClips)
		{
			PlayEightDirClip();
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

	// 평타(0): ATTACK_ 클립. 원거리 평타면 맞은 대상까지 화살 연출.
	// 원혼 스킬: SKILL_<슬롯>_ 클립. 시전 시간이 있으면 시전이 끝날 때까지 그 동작을 유지한다.
	public override void UseSkill(int skillId, int targetId, float castTime)
	{
		_rangedSkill = false;
		if (skillId == 0)
		{
			_skillAction = "ATTACK_";
			if (targetId != 0)
			{
				_rangedSkill = true;
				CombatHud.ShowShot(transform.position, targetId);
			}
		}
		else if (Table.TableManager.SpiritSkills.TryGet(skillId, out Table.SpiritSkillsRow row))
		{
			_skillAction = $"SKILL_{row.slot}_";
		}

		if (_coSkill != null)
			StopCoroutine(_coSkill);
		_coSkill = StartCoroutine(CoSkill(castTime));
	}

	public override void CancelSkill()
	{
		if (_coSkill != null)
			StopCoroutine(_coSkill);
		_coSkill = null;
		if (State == CreatureState.Skill)
			State = CreatureState.Idle;
		CheckUpdatedFlag();
	}

	protected virtual void CheckUpdatedFlag()
	{

	}

	IEnumerator CoSkill(float castTime)
	{
		State = CreatureState.Skill;
		UpdateAnimation(); // 이미 스킬 상태여도 새 동작을 처음부터
		float fallback = Table.TableManager.Configs.Get(ConfigBasicInterval).value[0];
		yield return new WaitForSeconds(Mathf.Max(castTime, EightDirClipLength(_skillAction, fallback)));
		if (State == CreatureState.Skill)
			State = CreatureState.Idle;
		_coSkill = null;
		CheckUpdatedFlag();
	}

	public override void OnDamaged()
	{
		Debug.Log("Player HIT !");
	}
}
