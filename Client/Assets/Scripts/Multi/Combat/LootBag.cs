using Google.Protobuf.Protocol;
using System.Collections.Generic;
using UnityEngine;

// 적 처치 보상(S_Loot)을 받아 담는 임시 가방과 획득 알림.
// 자동 획득: 서버가 굴려 보낸 것을 그대로 담는다. 지금은 게임을 켜 둔 동안만 유지한다.
// TODO 로컬 세이브(저장소 인터페이스·세이브 버전)가 생기면 그쪽으로 옮긴다. 인벤토리 화면은 '아웃게임 메뉴 UI'에서.
public class LootBag : MonoBehaviour
{
	const int SoulItemId = 9500; // items.itemID 혼백(재화)
	const float FeedSeconds = 4f;
	const int FeedMax = 6;

	// itemID → 개수
	static readonly Dictionary<int, int> s_items = new Dictionary<int, int>();
	public static IReadOnlyDictionary<int, int> Items => s_items;
	public static int Soul => s_items.TryGetValue(SoulItemId, out int n) ? n : 0;

	struct FeedLine
	{
		public string Text;
		public Color Color;
		public float Until;
	}
	static readonly List<FeedLine> s_feed = new List<FeedLine>();
	static LootBag s_view;
	static GUIStyle s_style;

	public static void Receive(S_Loot loot)
	{
		foreach (ItemCount item in loot.Items)
		{
			s_items.TryGetValue(item.ItemId, out int have);
			s_items[item.ItemId] = have + item.Count;
			AddFeed(item);
		}

		if (s_view == null)
			s_view = new GameObject("LootBag").AddComponent<LootBag>();
	}

	static void AddFeed(ItemCount item)
	{
		string name = $"[{item.ItemId}]";
		int grade = 0;
		if (Table.TableManager.Items.TryGet(item.ItemId, out Table.ItemsRow row))
		{
			name = Localization.Get(row.name);
			grade = row.grade;
		}

		s_feed.Add(new FeedLine()
		{
			Text = item.Count > 1 ? $"{name} ×{item.Count}" : name,
			Color = GradeColor(grade),
			Until = Time.time + FeedSeconds,
		});
		if (s_feed.Count > FeedMax)
			s_feed.RemoveAt(0);
	}

	// 등급 색(임시): 재화 흰색, 범 회색, 상 초록, 진 파랑, 영 보라, 신 금색
	static Color GradeColor(int grade)
	{
		switch (grade)
		{
			case 1: return new Color(0.8f, 0.8f, 0.8f);
			case 2: return new Color(0.45f, 0.9f, 0.45f);
			case 3: return new Color(0.45f, 0.7f, 1f);
			case 4: return new Color(0.8f, 0.5f, 1f);
			case 5: return new Color(1f, 0.82f, 0.3f);
			default: return Color.white;
		}
	}

	// 임시 표시(IMGUI): 화면 오른쪽 아래에 최근 획득을 몇 초 띄운다
	void OnGUI()
	{
		s_feed.RemoveAll(f => f.Until <= Time.time);
		if (s_feed.Count == 0)
			return;

		if (s_style == null)
			s_style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 14, fontStyle = FontStyle.Bold };

		float lineH = 20f;
		float y = Screen.height - 80f;
		for (int i = s_feed.Count - 1; i >= 0; i--)
		{
			s_style.normal.textColor = s_feed[i].Color;
			GUI.Label(new Rect(Screen.width - 320f, y, 300f, lineH), s_feed[i].Text, s_style);
			y -= lineH;
		}
	}
}
