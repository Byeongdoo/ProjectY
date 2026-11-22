using Google.Protobuf.Protocol;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using UnityEngine;
using static Define;

public class CreatureController : BaseController
{
	HpBar _hpBar;

	// 체력 막대 높이(발밑 피벗 기준, 칸). 주인공 스프라이트는 머리 위로 올린다. 빌더가 스프라이트 크기에서 정한다.
	[SerializeField] float _hpBarHeight = 0.5f;

	public override StatInfo Stat
	{
		get { return base.Stat; }
		set { base.Stat = value; UpdateHpBar(); }
	}

	public override int Hp
	{
		get { return Stat.Hp; }
		set { base.Hp = value; UpdateHpBar(); }
	}

	protected void AddHpBar()
	{
		GameObject go = Managers.Resource.Instantiate("UI/HpBar", transform);
		go.transform.localPosition = new Vector3(0, _hpBarHeight, 0);
		go.name = "HpBar";
		_hpBar = go.GetComponent<HpBar>();
		UpdateHpBar();
	}

	void UpdateHpBar()
	{
		if (_hpBar == null)
			return;

		float ratio = 0.0f;
		if (Stat.MaxHp > 0)
			ratio = ((float)Hp) / Stat.MaxHp;

		_hpBar.SetHpBar(ratio);
	}

	protected override void Init()
	{
		base.Init();
		// 삼천도처럼 전투가 없는 곳에서는 체력 막대를 띄우지 않는다
		if (MultiScene.CombatEnabled)
			AddHpBar();
	}

	// 8방향 클립(IDLE_SOUTH_EAST, RUN_NORTH …)을 가진 프리팹이면 켠다(무당·원혼·망령). 프리팹 생성 도구가 정한다.
	// 끄면 레거시 임시 스프라이트(앞·뒤·옆 3종 + 뒤집기) 규칙을 쓴다.
	[SerializeField] protected bool _eightDirClips = false;

	protected static string EightDirSuffix(MoveDir dir)
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

	// 원혼·망령 프리팹은 ATTACK·DIE 클립이 있다. 무당 프리팹처럼 없는 동작이면 서 있는 모습으로 둔다.
	protected void PlayEightDirClip()
	{
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
	}

	public virtual void OnDamaged()
	{

	}

	public virtual void OnDead()
	{
		State = CreatureState.Dead;

		// 쓰러짐 이펙트 리소스(Prefabs/Effect/DieEffect)는 새 리소스가 나올 때까지 비어 있다. 없으면 건너뛴다.
		if (Resources.Load<GameObject>("Prefabs/Effect/DieEffect") == null)
			return;
		GameObject effect = Managers.Resource.Instantiate("Effect/DieEffect");
		effect.transform.position = transform.position;
		effect.GetComponent<Animator>().Play("START");
		GameObject.Destroy(effect, 0.5f);
	}

	public virtual void UseSkill(int skillId)
	{

	}
}
