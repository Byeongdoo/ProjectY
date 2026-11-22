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

	public virtual void OnDamaged()
	{

	}

	public virtual void OnDead()
	{
		State = CreatureState.Dead;

		GameObject effect = Managers.Resource.Instantiate("Effect/DieEffect");
		effect.transform.position = transform.position;
		effect.GetComponent<Animator>().Play("START");
		GameObject.Destroy(effect, 0.5f);
	}

	public virtual void UseSkill(int skillId)
	{

	}
}
