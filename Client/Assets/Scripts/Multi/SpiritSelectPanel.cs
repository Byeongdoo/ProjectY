using System.Collections.Generic;
using Google.Protobuf.Protocol;
using Table;
using UnityEngine;

// 나루터 원혼 선택('파티 및 매칭 시스템' 2-5). 사공에게 F → 이 화면 → 원혼을 골라 던전 입장 요청.
// 임시 화면이다(정식 UI 시안: Notion UI/spirit_select.html). ←→ 고르기, Enter 확정, Esc 닫기.
// 같은 던전의 다른 플레이어가 이미 고른 원혼은 서버가 거절(S_EnterDungeonReject)하고, 여기서 막힌 칸으로 표시한다.
public class SpiritSelectPanel : MonoBehaviour
{
	static SpiritSelectPanel s_instance;

	public static bool IsOpen => s_instance != null;

	int _dungeonId;
	readonly List<SpiritsRow> _spirits = new List<SpiritsRow>();
	readonly List<Sprite> _icons = new List<Sprite>();
	readonly HashSet<int> _taken = new HashSet<int>();
	int _index;
	int _openedFrame;
	float _requestedAt = -1f;

	const float ResendGuard = 1f; // 응답 없이 다시 보낼 수 있을 때까지(입력 처리용, 게임 수치 아님)

	public static void Open(int dungeonId)
	{
		if (s_instance != null)
			return;

		GameObject go = new GameObject("SpiritSelectPanel");
		s_instance = go.AddComponent<SpiritSelectPanel>();
		s_instance.Init(dungeonId);
	}

	public static void Close()
	{
		if (s_instance != null)
			Destroy(s_instance.gameObject);
		s_instance = null;
	}

	public static void OnRejected(int spiritId)
	{
		if (s_instance == null)
			return;
		s_instance._taken.Add(spiritId);
		s_instance._requestedAt = -1f;
	}

	void Init(int dungeonId)
	{
		_dungeonId = dungeonId;
		_openedFrame = Time.frameCount;
		foreach (SpiritsRow row in TableManager.Spirits.Rows)
		{
			_spirits.Add(row);
			GameObject prefab = Resources.Load<GameObject>(ObjectManager.SpiritPrefabPath(row));
			_icons.Add(prefab != null ? prefab.GetComponent<SpriteRenderer>().sprite : null);
		}
	}

	void OnDestroy()
	{
		if (s_instance == this)
			s_instance = null;
	}

	void Update()
	{
		// 여는 데 쓴 키 입력이 같은 프레임에 확정으로 넘어오지 않게 한다
		if (Time.frameCount == _openedFrame || _spirits.Count == 0)
			return;

		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Close();
			return;
		}

		if (Waiting)
			return;

		if (Input.GetKeyDown(KeyCode.LeftArrow)) _index = (_index + _spirits.Count - 1) % _spirits.Count;
		if (Input.GetKeyDown(KeyCode.RightArrow)) _index = (_index + 1) % _spirits.Count;

		if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
		{
			int spiritId = _spirits[_index].spiritID;
			if (_taken.Contains(spiritId))
				return;
			_requestedAt = Time.time;
			Managers.Network.Send(new C_EnterDungeon() { DungeonId = _dungeonId, SpiritId = spiritId });
		}
	}

	bool Waiting => _requestedAt >= 0 && Time.time - _requestedAt < ResendGuard;

	// 화면 배치(px)는 표시용이다. 원혼 스프라이트를 정수배로 키워 도트를 유지한다.
	const int Cell = 64, Scale = 2, Gap = 12, Pad = 16, NameH = 24;
	GUIStyle _nameStyle;

	void OnGUI()
	{
		int n = _spirits.Count;
		int slot = Cell * Scale;
		int w = n * slot + (n - 1) * Gap + Pad * 2;
		int h = slot + NameH + Pad * 2;
		Rect panel = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
		GUI.Box(panel, GUIContent.none);

		if (_nameStyle == null)
			_nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
		for (int i = 0; i < n; i++)
		{
			Rect r = new Rect(panel.x + Pad + i * (slot + Gap), panel.y + Pad, slot, slot);
			bool taken = _taken.Contains(_spirits[i].spiritID);
			bool selected = i == _index;

			Color prev = GUI.color;
			GUI.color = selected ? (Waiting ? new Color(1f, 0.85f, 0.4f) : Color.white) : new Color(1f, 1f, 1f, 0.35f);
			GUI.Box(r, GUIContent.none);
			if (taken)
				GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
			DrawSprite(r, _icons[i]);
			GUI.color = taken ? new Color(0.5f, 0.5f, 0.5f) : prev;
			GUI.Label(new Rect(r.x, r.yMax, r.width, NameH), Localization.Get(_spirits[i].name), _nameStyle);
			GUI.color = prev;
		}
	}

	// 발밑 피벗 스프라이트를 칸 아래쪽에 맞춰 그린다. 원본보다 크면(무명장수 96px 등) 정수배를 줄인다.
	static void DrawSprite(Rect r, Sprite sprite)
	{
		if (sprite == null)
			return;

		Rect tr = sprite.textureRect;
		int scale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(r.width / tr.width, r.height / tr.height)));
		float w = tr.width * scale, h = tr.height * scale;
		Rect dst = new Rect(r.x + (r.width - w) / 2, r.yMax - h, w, h);
		Texture2D tex = sprite.texture;
		Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
		GUI.DrawTextureWithTexCoords(dst, tex, uv);
	}
}
