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
		// 외형 전용 프리팹(원혼)은 컨트롤러를 생성 시 붙이므로 표시 설정을 CreatureVisual에서 가져온다
		CreatureVisual visual = GetComponent<CreatureVisual>();
		if (visual != null)
		{
			_hpBarHeight = visual.hpBarHeight;
			_eightDirClips = visual.eightDirClips;
		}

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

	// 스킬 상태에서 틀 동작(ATTACK_ = 평타, SKILL_1_ ~ SKILL_4_ = 원혼 스킬 슬롯)
	protected string _skillAction = "ATTACK_";

	// 원혼·망령 프리팹은 ATTACK·DIE 클립이 있다. 무당 프리팹처럼 없는 동작이면 서 있는 모습으로 둔다.
	protected void PlayEightDirClip()
	{
		string action;
		if (State == CreatureState.Dead) action = "DIE_";
		else if (HasStatus(StatusStun)) action = "STUN_";
		else if (State == CreatureState.Moving) action = "RUN_";
		else if (State == CreatureState.Skill) action = _skillAction;
		else action = "IDLE_";

		string clip = action + EightDirSuffix(Dir);
		if (_animator.HasState(0, Animator.StringToHash(clip)) == false)
			clip = "IDLE_" + EightDirSuffix(Dir);
		// 공격·스킬은 같은 클립이 이어져도 처음부터 다시 튼다(연속 평타)
		if (State == CreatureState.Skill)
			_animator.Play(clip, 0, 0f);
		else
			_animator.Play(clip);
		_sprite.flipX = false;
	}

	// 지금 방향의 동작 클립 길이(초). 클립이 없으면 fallback
	protected float EightDirClipLength(string action, float fallback)
	{
		if (_animator == null || _animator.runtimeAnimatorController == null)
			return fallback;
		string name = action + EightDirSuffix(Dir);
		foreach (AnimationClip c in _animator.runtimeAnimatorController.animationClips)
			if (c.name == name)
				return c.length;
		return fallback;
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

	// 서버가 받아들인 평타(skillId 0)·원혼 스킬. castTime > 0이면 시전 시작
	public virtual void UseSkill(int skillId, int targetId, float castTime)
	{

	}

	// 시전 취소(S_CancelSkill)
	public virtual void CancelSkill()
	{

	}

	// ---- 상태이상 (S_ChangeStatus). 표시와 내 입력 제한에만 쓴다. 효과는 서버가 판정한다 ----

	public const int StatusRoot = 8001;   // 결박: 이동 불가
	public const int StatusStun = 8005;   // 기절: 이동·공격·스킬 불가
	public const int StatusFervor = 8008; // 신명: 신력 소모 감소
	public const int StatusBrace = 8009;  // 버팀: 이동 불가
	public const int StatusFrenzy = 8011; // 광기: 공격 속도 증가
	public const int StatusSlow = 8004;   // 둔화: 이동·공격 속도 감소

	readonly Dictionary<int, (float endTime, int stack)> _statuses = new Dictionary<int, (float, int)>();

	public IReadOnlyDictionary<int, (float endTime, int stack)> Statuses => _statuses;

	public bool HasStatus(int statusId)
	{
		return _statuses.TryGetValue(statusId, out var s) && s.endTime > Time.time;
	}

	// 효과 값 × 중첩(statusEffects.value). 없으면 0
	public float StatusValue(int statusId)
	{
		if (HasStatus(statusId) == false)
			return 0f;
		return Table.TableManager.StatusEffects.Get(statusId).value * _statuses[statusId].stack;
	}

	public void SetStatus(int statusId, float duration, int stack)
	{
		bool stunBefore = HasStatus(StatusStun);
		if (duration <= 0f)
			_statuses.Remove(statusId);
		else
			_statuses[statusId] = (Time.time + duration, stack);
		if (stunBefore != HasStatus(StatusStun))
			UpdateAnimation();
	}

	// 이동 불가 상태(기절·결박·버팀)
	public bool IsMoveLocked => HasStatus(StatusStun) || HasStatus(StatusRoot) || HasStatus(StatusBrace);
}
