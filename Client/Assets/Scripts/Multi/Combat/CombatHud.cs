using System.Collections.Generic;
using Table;
using UnityEngine;

// 저승 던전 전투 HUD 플레이스홀더(IMGUI). 정식 HUD는 '인게임 HUD'(UI/ingame_hud.html) 작업에서 교체한다.
//   하단: 체력·신력 막대, 스킬 슬롯 1~4(쿨타임·신력 부족·공용 재사용 대기), 시전 막대
//   좌상단: 내 상태이상
//   월드: 현재 대상·대상 선택 커서(사거리 밖이면 회색), 피해·회복 숫자, 원거리 평타 궤적, 덫 칸
// 아래 크기·시간 상수는 화면 연출용이며 게임 수치가 아니다.
public class CombatHud : MonoBehaviour
{
	static CombatHud s_instance;

	const int SlotSize = 64;
	const int SlotGap = 6;
	const int BarWidth = 4 * SlotSize + 3 * SlotGap;
	const int BarHeight = 12;
	const float NumberLifetime = 0.9f;  // 피해 숫자가 떠 있는 시간
	const float NumberRise = 0.8f;      // 그동안 올라가는 높이(월드 단위)
	const float ShotLifetime = 0.15f;   // 원거리 평타 궤적

	struct FloatingNumber
	{
		public Vector3 WorldPos;
		public string Text;
		public Color Color;
		public float Start;
		public bool Big;
	}

	struct Shot
	{
		public Vector3 From;
		public Vector3 To;
		public float Start;
	}

	readonly List<FloatingNumber> _numbers = new List<FloatingNumber>();
	readonly List<Shot> _shots = new List<Shot>();
	GUIStyle _center, _number, _numberBig, _small;
	Texture2D _white;

	public static void Ensure()
	{
		if (s_instance != null)
			return;
		s_instance = new GameObject("CombatHud").AddComponent<CombatHud>();
	}

	void OnDestroy()
	{
		if (s_instance == this)
			s_instance = null;
	}

	public static void ShowNumber(CreatureController target, int amount, bool critical, bool heal)
	{
		if (s_instance == null || target == null)
			return;
		bool mine = Managers.Object.MyPlayer != null && target.Id == Managers.Object.MyPlayer.Id;
		Color color = heal ? new Color(0.4f, 1f, 0.4f) : critical ? new Color(1f, 0.85f, 0.2f) : mine ? new Color(1f, 0.45f, 0.45f) : Color.white;
		s_instance._numbers.Add(new FloatingNumber()
		{
			WorldPos = target.transform.position + Vector3.up * HeadHeight(target),
			Text = heal ? $"+{amount}" : amount.ToString(),
			Color = color,
			Start = Time.time,
			Big = critical,
		});
	}

	public static void ShowShot(Vector3 from, int targetId)
	{
		if (s_instance == null)
			return;
		GameObject target = Managers.Object.FindById(targetId);
		if (target == null)
			return;
		s_instance._shots.Add(new Shot() { From = from + Vector3.up * 0.5f, To = target.transform.position + Vector3.up * 0.5f, Start = Time.time });
	}

	static float HeadHeight(CreatureController cc)
	{
		CreatureVisual visual = cc.GetComponent<CreatureVisual>();
		return visual != null ? visual.hpBarHeight : 1f;
	}

	void OnGUI()
	{
		if (_center == null)
		{
			_center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 11 };
			_small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
			_number = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };
			_numberBig = new GUIStyle(_number) { fontSize = 22 };
			_white = Texture2D.whiteTexture;
		}

		Camera cam = Camera.main;
		if (cam != null)
		{
			DrawFields(cam);
			DrawShots(cam);
			DrawTargetMarker(cam);
			DrawNumbers(cam);
		}

		MyPlayerController me = Managers.Object.MyPlayer;
		if (me == null)
			return;
		DrawBars(me);
		DrawSlots();
		DrawCastBar();
		DrawStatuses(me);
	}

	// ---- 화면 하단 ----

	Rect BottomArea()
	{
		float x = (Screen.width - BarWidth) / 2f;
		float y = Screen.height - SlotSize - 2 * BarHeight - 24;
		return new Rect(x, y, BarWidth, SlotSize + 2 * BarHeight + 16);
	}

	void DrawBars(MyPlayerController me)
	{
		Rect area = BottomArea();
		float hpRatio = me.Stat.MaxHp > 0 ? (float)me.Hp / me.Stat.MaxHp : 0f;
		float spRatio = MyCombat.MaxSpirit > 0 ? (float)MyCombat.Spirit / MyCombat.MaxSpirit : 0f;
		DrawBar(new Rect(area.x, area.y, BarWidth, BarHeight), hpRatio, new Color(0.8f, 0.2f, 0.2f), $"{me.Hp} / {me.Stat.MaxHp}");
		DrawBar(new Rect(area.x, area.y + BarHeight + 2, BarWidth, BarHeight), spRatio, new Color(0.3f, 0.5f, 1f), $"{MyCombat.Spirit} / {MyCombat.MaxSpirit}");
	}

	void DrawBar(Rect r, float ratio, Color color, string label)
	{
		Fill(r, new Color(0f, 0f, 0f, 0.6f));
		Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(ratio), r.height), color);
		GUI.Label(r, label, _center);
	}

	void DrawSlots()
	{
		Rect area = BottomArea();
		float y = area.y + 2 * BarHeight + 8;
		float gcd = MyCombat.GlobalCooldownLeft;
		for (int slot = 1; slot <= 4; slot++)
		{
			Rect r = new Rect(area.x + (slot - 1) * (SlotSize + SlotGap), y, SlotSize, SlotSize);
			SpiritSkillsRow skill = MyCombat.SkillInSlot(slot);
			Fill(r, new Color(0f, 0f, 0f, 0.6f));
			GUI.Label(new Rect(r.x + 3, r.y, 16, 16), slot.ToString(), _small);
			if (skill == null)
				continue;

			bool usable = MyCombat.CanUse(skill);
			bool selecting = TargetSelect.Active && TargetSelect.Skill.skillID == skill.skillID;
			Color prev = GUI.color;
			GUI.color = selecting ? new Color(1f, 0.9f, 0.4f) : usable ? Color.white : new Color(1f, 1f, 1f, 0.45f);
			GUI.Label(new Rect(r.x, r.y + 10, r.width, r.height - 28), Localization.Get(skill.name), _center);

			float cd = MyCombat.CooldownLeft(skill.skillID);
			string info = cd > 0f ? $"{cd:0.0}s" : skill.gate == 1 ? Mathf.RoundToInt(MyCombat.SpiritCost(skill)).ToString() : "";
			GUI.Label(new Rect(r.x, r.yMax - 18, r.width, 16), info, _center);
			GUI.color = prev;

			// 공용 재사용 대기
			if (gcd > 0f)
				Fill(new Rect(r.x, r.yMax - 3, r.width * Mathf.Clamp01(gcd / Table.TableManager.Configs.Get(20044).value[0]), 3), new Color(1f, 1f, 1f, 0.6f));
		}
	}

	void DrawCastBar()
	{
		if (MyCombat.IsCasting == false)
			return;
		Rect area = BottomArea();
		float ratio = (Time.time - MyCombat.CastStart) / Mathf.Max(0.01f, MyCombat.CastEnd - MyCombat.CastStart);
		string name = TableManager.SpiritSkills.TryGet(MyCombat.CastSkillId, out SpiritSkillsRow row) ? Localization.Get(row.name) : "";
		DrawBar(new Rect(area.x + BarWidth / 4f, area.y - BarHeight - 10, BarWidth / 2f, BarHeight), ratio, new Color(1f, 0.8f, 0.3f), name);
	}

	void DrawStatuses(MyPlayerController me)
	{
		float y = 10;
		foreach (KeyValuePair<int, (float endTime, int stack)> kv in me.Statuses)
		{
			float left = kv.Value.endTime - Time.time;
			if (left <= 0f)
				continue;
			string stack = kv.Value.stack > 1 ? $" x{kv.Value.stack}" : "";
			string name = TableManager.StatusEffects.TryGet(kv.Key, out StatusEffectsRow row) ? Localization.Get(row.name) : kv.Key.ToString();
			GUI.Label(new Rect(10, y, 200, 18), $"{name}{stack}  {left:0.0}s", _small);
			y += 18;
		}
	}

	// ---- 월드 ----

	void DrawTargetMarker(Camera cam)
	{
		int id = TargetSelect.Active ? TargetSelect.CursorId : MyCombat.CurrentTargetId;
		if (id == 0)
			return;
		GameObject go = Managers.Object.FindById(id);
		CreatureController cc = go != null ? go.GetComponent<CreatureController>() : null;
		if (cc == null)
		{
			if (TargetSelect.Active == false)
				MyCombat.CurrentTargetId = 0;
			return;
		}

		Color color = new Color(1f, 0.4f, 0.3f);
		if (TargetSelect.Active)
			color = TargetSelect.InRange(TargetSelect.Skill, cc) ? new Color(1f, 0.9f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);

		Vector2 head = ToGui(cam, cc.transform.position + Vector3.up * (HeadHeight(cc) + 0.35f));
		Color prev = GUI.color;
		GUI.color = color;
		GUI.Label(new Rect(head.x - 20, head.y - 12, 40, 24), "▼", _numberBig);
		GUI.color = prev;
	}

	void DrawNumbers(Camera cam)
	{
		float now = Time.time;
		_numbers.RemoveAll(n => now - n.Start > NumberLifetime);
		foreach (FloatingNumber n in _numbers)
		{
			float t = (now - n.Start) / NumberLifetime;
			Vector2 p = ToGui(cam, n.WorldPos + Vector3.up * (NumberRise * t));
			Color prev = GUI.color;
			GUI.color = new Color(n.Color.r, n.Color.g, n.Color.b, 1f - t * t);
			GUI.Label(new Rect(p.x - 40, p.y - 14, 80, 28), n.Text, n.Big ? _numberBig : _number);
			GUI.color = prev;
		}
	}

	void DrawShots(Camera cam)
	{
		float now = Time.time;
		_shots.RemoveAll(s => now - s.Start > ShotLifetime);
		foreach (Shot s in _shots)
		{
			float t = (now - s.Start) / ShotLifetime;
			Vector2 p = ToGui(cam, Vector3.Lerp(s.From, s.To, t));
			Fill(new Rect(p.x - 3, p.y - 3, 6, 6), new Color(1f, 0.95f, 0.7f));
		}
	}

	void DrawFields(Camera cam)
	{
		foreach (FieldController f in Managers.Object.Fields())
		{
			if (f == null)
				continue;
			Vector2 a = ToGui(cam, f.transform.position + new Vector3(-0.35f, 0.35f));
			Vector2 b = ToGui(cam, f.transform.position + new Vector3(0.35f, -0.35f));
			Rect r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
			Color c = new Color(0.9f, 0.6f, 0.2f, 0.9f);
			Fill(new Rect(r.x, r.y, r.width, 2), c);
			Fill(new Rect(r.x, r.yMax - 2, r.width, 2), c);
			Fill(new Rect(r.x, r.y, 2, r.height), c);
			Fill(new Rect(r.xMax - 2, r.y, 2, r.height), c);
		}
	}

	static Vector2 ToGui(Camera cam, Vector3 world)
	{
		Vector3 s = cam.WorldToScreenPoint(world);
		return new Vector2(s.x, Screen.height - s.y);
	}

	void Fill(Rect r, Color color)
	{
		Color prev = GUI.color;
		GUI.color = color;
		GUI.DrawTexture(r, _white);
		GUI.color = prev;
	}
}
