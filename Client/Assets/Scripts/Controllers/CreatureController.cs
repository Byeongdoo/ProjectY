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

	#region 상태이상

	// statusEffects.statusID (서버 StatusId와 같은 값, 데이터 키)
	public const int StatusBind = 8001;    // 결박 — 이동 불가
	public const int StatusSlow = 8004;    // 둔화 — 이동·공격 속도 감소
	public const int StatusStun = 8005;    // 기절 — 이동·공격·방향 전환 불가
	public const int StatusHaste = 8007;   // 신속 — 이동 속도 증가
	public const int StatusBrace = 8009;   // 버팀 — 이동 불가

	// statusID → 끝나는 시각(Time.time). 서버가 해제를 보내지만, 패킷이 늦어도 표시가 남지 않게 시간으로도 지운다.
	readonly Dictionary<int, float> _statusEnd = new Dictionary<int, float>();

	public void SetStatuses(IEnumerable<StatusInfo> statuses)
	{
		_statusEnd.Clear();
		foreach (StatusInfo s in statuses)
			SetStatus(s);
	}

	public void SetStatus(StatusInfo status)
	{
		if (status.RemainMs <= 0)
			_statusEnd.Remove(status.StatusId);
		else
			_statusEnd[status.StatusId] = Time.time + status.RemainMs / 1000f;
	}

	public bool HasStatus(int statusId)
	{
		return _statusEnd.TryGetValue(statusId, out float end) && end > Time.time;
	}

	public bool StatusCanMove { get { return !HasStatus(StatusBind) && !HasStatus(StatusStun) && !HasStatus(StatusBrace); } }
	public bool StatusCanAct { get { return !HasStatus(StatusStun); } }
	public bool StatusCanTurn { get { return !HasStatus(StatusStun); } }

	// 서버 StatusSet과 같은 규칙: 증가는 × (1 + 값), 감소는 × (1 − 값), 여러 효과는 곱한다
	public override float MoveSpeed
	{
		get { return Speed * StatusRate(StatusSlow, -1) * StatusRate(StatusHaste, +1); }
	}

	float StatusRate(int statusId, int sign)
	{
		if (HasStatus(statusId) == false || Table.TableManager.StatusEffects.TryGet(statusId, out Table.StatusEffectsRow row) == false)
			return 1f;
		return Mathf.Max(0f, 1f + sign * row.value);
	}

	// 임시 표시: 체력 막대 위에 상태이상 이름(해로움 = 붉은색, 이로움 = 금색). 문양·HUD 아이콘 리소스가 오면 교체한다.
	static GUIStyle s_statusStyle;

	void OnGUI()
	{
		if (_statusEnd.Count == 0 || Camera.main == null)
			return;

		Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + new Vector3(0, _hpBarHeight + 0.35f, 0));
		if (screen.z < 0)
			return;

		if (s_statusStyle == null)
			s_statusStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };

		float y = Screen.height - screen.y;
		foreach (KeyValuePair<int, float> kv in _statusEnd)
		{
			if (kv.Value <= Time.time || Table.TableManager.StatusEffects.TryGet(kv.Key, out Table.StatusEffectsRow row) == false)
				continue;
			s_statusStyle.normal.textColor = row.harmful != 0 ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.3f);
			GUI.Label(new Rect(screen.x - 60, y - 10, 120, 20), Localization.Get(row.name), s_statusStyle);
			y -= 14;
		}
	}

	#endregion

	public virtual void OnDamaged()
	{

	}

	public virtual void OnDead()
	{
		State = CreatureState.Dead;
		_statusEnd.Clear();

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
