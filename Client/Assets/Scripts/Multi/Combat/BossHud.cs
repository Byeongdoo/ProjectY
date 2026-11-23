using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Protocol;
using UnityEngine;

// 보스 체력바(임시 IMGUI, '인게임 HUD' 2-3·2-8). 상단 중앙.
//   교전 중인 보스(S_BossState.engaged)만 띄운다: 이름 · 체력 · 페이즈 눈금(bossPhases.hpStart)
//   그 아래: 탈진 남은 초와 받는 피해 증가(statusEffects 8013), 대형 패턴 이름(bossSkills.showName, 예고 동안)
// 표시 문자열은 strings 시트(Localization)에서 읽는다. 최종 UI 리소스가 오면 교체한다.
public class BossHud : MonoBehaviour
{
	const int StatusExhaust = 8013;
	const float BarWidth = 420f;
	const float BarHeight = 14f;
	const float Top = 12f;

	class BossEntry
	{
		public bool Engaged;
		public int Phase = 1;
		public int SkillNameId;   // 예고 중인 대형 패턴(0 = 없음)
		public float SkillNameUntil;
	}

	static readonly Dictionary<int, BossEntry> s_bosses = new Dictionary<int, BossEntry>();
	static BossHud s_instance;
	GUIStyle _title, _small;
	Texture2D _white;

	static BossEntry Entry(int objectId)
	{
		if (s_bosses.TryGetValue(objectId, out BossEntry e) == false)
		{
			e = new BossEntry();
			s_bosses[objectId] = e;
		}
		if (s_instance == null)
			s_instance = new GameObject("BossHud").AddComponent<BossHud>();
		return e;
	}

	public static void OnState(S_BossState state)
	{
		BossEntry e = Entry(state.ObjectId);
		e.Engaged = state.Engaged;
		e.Phase = state.Phase;

		GameObject go = Managers.Object.FindById(state.ObjectId);
		MonsterController mc = go != null ? go.GetComponent<MonsterController>() : null;
		if (mc != null)
			mc.SetBossPhase(state.Phase);
	}

	// 대형 패턴 이름(예고 동안). skillId 0이면 지운다
	public static void ShowSkillName(int bossId, int skillId, float seconds)
	{
		BossEntry e = Entry(bossId);
		e.SkillNameId = skillId;
		e.SkillNameUntil = Time.time + seconds;
	}

	public static void Remove(int bossId)
	{
		s_bosses.Remove(bossId);
	}

	void OnGUI()
	{
		if (_title == null)
		{
			_title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
			_small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
			_white = Texture2D.whiteTexture;
		}

		// 교전 중인 보스 하나(던전에 보스는 하나)
		foreach (KeyValuePair<int, BossEntry> kv in s_bosses.ToList())
		{
			if (kv.Value.Engaged == false)
				continue;
			GameObject go = Managers.Object.FindById(kv.Key);
			MonsterController boss = go != null ? go.GetComponent<MonsterController>() : null;
			if (boss == null)
			{
				s_bosses.Remove(kv.Key);
				continue;
			}
			Draw(boss, kv.Value);
			break;
		}
	}

	void Draw(MonsterController boss, BossEntry e)
	{
		float x = (Screen.width - BarWidth) / 2f;
		float y = Top;

		string name = Table.TableManager.CharacterInfos.TryGet(boss.CharacterId, out Table.CharacterInfosRow info) ? Localization.Get(info.name) : "";
		GUI.Label(new Rect(x, y, BarWidth, 20), name, _title);
		y += 20;

		// 체력
		float ratio = boss.Stat.MaxHp > 0 ? Mathf.Clamp01((float)boss.Hp / boss.Stat.MaxHp) : 0f;
		Rect bar = new Rect(x, y, BarWidth, BarHeight);
		Fill(bar, new Color(0, 0, 0, 0.7f));
		bool exhausted = boss.HasStatus(StatusExhaust);
		Fill(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), exhausted ? new Color(1f, 0.6f, 0.2f) : new Color(0.75f, 0.12f, 0.12f));
		GUI.Label(bar, $"{boss.Hp} / {boss.Stat.MaxHp}", _small);

		// 페이즈 눈금: 다음 페이즈가 시작되는 체력 비율 자리에 세로 선
		foreach (Table.BossPhasesRow p in Table.TableManager.BossPhases.Rows)
		{
			if (p.boss != boss.CharacterId || p.hpStart >= 1f)
				continue;
			float tx = bar.x + bar.width * p.hpStart;
			Fill(new Rect(tx - 1, bar.y - 3, 2, bar.height + 6), p.phase <= e.Phase ? new Color(1f, 1f, 1f, 0.4f) : Color.white);
		}
		y += BarHeight + 4;

		// 탈진: 남은 초와 받는 피해 증가
		if (exhausted && Table.TableManager.StatusEffects.TryGet(StatusExhaust, out Table.StatusEffectsRow ex))
		{
			float left = boss.Statuses.TryGetValue(StatusExhaust, out var s) ? Mathf.Max(0f, s.endTime - Time.time) : 0f;
			GUI.Label(new Rect(x, y, BarWidth, 18), $"{Localization.Get(ex.name)} {left:0.0}s  ·  +{ex.value * 100f:0}%", _small);
			y += 18;
		}

		// 대형 패턴 이름(예고 동안)
		if (e.SkillNameId != 0 && Time.time < e.SkillNameUntil)
			GUI.Label(new Rect(x, y, BarWidth, 20), Localization.Get($"BossSkill_{e.SkillNameId}"), _title);
	}

	void Fill(Rect r, Color c)
	{
		Color old = GUI.color;
		GUI.color = c;
		GUI.DrawTexture(r, _white);
		GUI.color = old;
	}
}
