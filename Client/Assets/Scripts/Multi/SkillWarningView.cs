using Google.Protobuf.Protocol;
using System.Collections.Generic;
using UnityEngine;

// 적 특수기 예고 칸(S_SkillWarning) 임시 표시: 위험 칸마다 반투명 붉은 사각형을 깔고 발동 직전까지 점점 진하게 한다.
// Warning 이펙트 리소스(Notion '이펙트' Warning 파트)가 오면 이 표시를 교체한다. 판정은 서버가 한다.
public class SkillWarningView : MonoBehaviour
{
	// 바닥(0)·장식(10) 위, 캐릭터(30) 아래
	const int SortingOrder = 20;
	const float AlphaStart = 0.15f;
	const float AlphaEnd = 0.5f;

	static readonly Dictionary<int, SkillWarningView> s_active = new Dictionary<int, SkillWarningView>();
	static Sprite s_cellSprite;

	float _duration;
	float _elapsed;
	readonly List<SpriteRenderer> _cells = new List<SpriteRenderer>();

	public static void Handle(S_SkillWarning packet)
	{
		if (s_active.TryGetValue(packet.WarningId, out SkillWarningView old))
		{
			s_active.Remove(packet.WarningId);
			if (old != null)
				Destroy(old.gameObject);
		}

		if (packet.DurationMs <= 0 || Managers.Map.CurrentGrid == null)
			return;

		GameObject root = new GameObject($"SkillWarning_{packet.WarningId}");
		SkillWarningView view = root.AddComponent<SkillWarningView>();
		view._duration = packet.DurationMs / 1000f;
		foreach (CellPos cell in packet.Cells)
			view.AddCell(new Vector3Int(cell.X, cell.Y, 0));
		s_active[packet.WarningId] = view;
	}

	void AddCell(Vector3Int cell)
	{
		GameObject go = new GameObject("Cell");
		go.transform.SetParent(transform, false);
		go.transform.position = Managers.Map.CurrentGrid.CellToWorld(cell) + new Vector3(0.5f, 0.5f);

		SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
		sr.sprite = CellSprite();
		sr.sortingOrder = SortingOrder;
		sr.color = new Color(1f, 0.15f, 0.1f, AlphaStart);
		_cells.Add(sr);
	}

	// 1×1 흰 텍스처를 1칸(1유닛) 크기 스프라이트로 쓴다
	static Sprite CellSprite()
	{
		if (s_cellSprite == null)
		{
			Texture2D tex = new Texture2D(1, 1);
			tex.SetPixel(0, 0, Color.white);
			tex.filterMode = FilterMode.Point;
			tex.Apply();
			s_cellSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
		}
		return s_cellSprite;
	}

	void Update()
	{
		_elapsed += Time.deltaTime;
		float t = _duration > 0 ? Mathf.Clamp01(_elapsed / _duration) : 1f;
		float alpha = Mathf.Lerp(AlphaStart, AlphaEnd, t);
		foreach (SpriteRenderer sr in _cells)
		{
			Color c = sr.color;
			c.a = alpha;
			sr.color = c;
		}

		// 발동 순간 서버가 지우지 않아도 예고 시간이 끝나면 사라진다
		if (_elapsed >= _duration)
			Destroy(gameObject);
	}

	void OnDestroy()
	{
		foreach (KeyValuePair<int, SkillWarningView> kv in s_active)
		{
			if (kv.Value == this)
			{
				s_active.Remove(kv.Key);
				break;
			}
		}
	}
}
