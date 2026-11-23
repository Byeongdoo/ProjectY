using Google.Protobuf.Protocol;
using System.Collections.Generic;
using UnityEngine;

// 적 특수기·보스 패턴 예고(S_SkillWarning) 표시. 판정은 서버가 한다('인게임 HUD' 2-8).
//   예고 칸: 칸마다 옅은 붉은 바닥 위에 붉은 사각형이 안쪽부터 차오른다. 다 차면 발동. 보스는 바닥에 WarningGrid 리소스를 깐다.
//   산개 표식: 표식 대상 주위 configs 20045칸 원. 다른 표식과 그 거리 안이면 원이 붉어진다(겹치면 피해 ×2).
//   보스 대형 패턴(bossSkills.showName)은 예고 동안 보스 체력바 아래에 이름을 띄운다.
public class SkillWarningView : MonoBehaviour
{
	// 바닥(0)·장식(10) 위, 캐릭터(30) 아래
	const int SortingOrder = 20;
	const float BaseAlpha = 0.18f;
	const float FillAlpha = 0.5f;
	const int PatternSpread = 3;          // bossSkills.pattern 산개
	const int ConfigSpreadDistance = 20045; // 산개 판정 거리(칸)

	static readonly Dictionary<int, SkillWarningView> s_active = new Dictionary<int, SkillWarningView>();
	// 시전자 → 마지막 예고의 칸·표식 대상(발동 이펙트용)
	static readonly Dictionary<int, List<Vector3Int>> s_lastCells = new Dictionary<int, List<Vector3Int>>();
	static readonly Dictionary<int, List<int>> s_lastTargets = new Dictionary<int, List<int>>();
	static Sprite s_cellSprite;
	static Material s_lineMaterial;

	float _duration;
	float _elapsed;
	readonly List<Transform> _fills = new List<Transform>();
	readonly List<(CreatureController target, LineRenderer ring)> _spread = new List<(CreatureController, LineRenderer)>();
	float _spreadRadius;

	public static IList<Vector3Int> LastCells(int casterId) => s_lastCells.TryGetValue(casterId, out var c) ? c : null;
	public static IList<int> LastTargets(int casterId) => s_lastTargets.TryGetValue(casterId, out var t) ? t : null;

	public static void Handle(S_SkillWarning packet)
	{
		// 시전자는 예고 동안 준비 자세를 유지한다(지우면 자세도 푼다)
		GameObject caster = Managers.Object.FindById(packet.ObjectId);
		MonsterController mc = caster != null ? caster.GetComponent<MonsterController>() : null;
		if (mc != null)
		{
			if (packet.DurationMs > 0)
				mc.BeginCastPose(packet.SkillId);
			else
				mc.EndCastPose();
		}

		if (s_active.TryGetValue(packet.WarningId, out SkillWarningView old))
		{
			s_active.Remove(packet.WarningId);
			if (old != null)
				Destroy(old.gameObject);
		}

		Table.TableManager.BossSkills.TryGet(packet.SkillId, out Table.BossSkillsRow boss);
		if (packet.DurationMs <= 0)
		{
			if (boss != null)
				BossHud.ShowSkillName(packet.ObjectId, 0, 0f);
			return;
		}
		if (Managers.Map.CurrentGrid == null)
			return;

		List<Vector3Int> cells = new List<Vector3Int>();
		foreach (CellPos c in packet.Cells)
			cells.Add(new Vector3Int(c.X, c.Y, 0));
		s_lastCells[packet.ObjectId] = cells;
		s_lastTargets[packet.ObjectId] = new List<int>(packet.TargetIds);

		GameObject root = new GameObject($"SkillWarning_{packet.WarningId}");
		SkillWarningView view = root.AddComponent<SkillWarningView>();
		view._duration = packet.DurationMs / 1000f;
		int characterId = mc != null ? mc.CharacterId : 0;
		foreach (Vector3Int cell in cells)
			view.AddCell(cell, boss != null ? characterId : 0);

		if (boss != null)
		{
			if (boss.pattern == PatternSpread)
				view.AddSpreadRings(packet.TargetIds);
			if (boss.showName != 0)
				BossHud.ShowSkillName(packet.ObjectId, packet.SkillId, view._duration);
		}

		s_active[packet.WarningId] = view;
	}

	void AddCell(Vector3Int cell, int bossCharacterId)
	{
		Vector3 center = Managers.Map.CurrentGrid.CellToWorld(cell) + new Vector3(0.5f, 0.5f);

		// 바닥: 보스는 WarningGrid 리소스, 없으면 옅은 붉은 칸
		GameObject floor = bossCharacterId != 0 ? BossVfx.SpawnWarningTile(bossCharacterId, cell, transform) : null;
		if (floor == null)
			MakeQuad("Base", center, new Color(1f, 0.15f, 0.1f, BaseAlpha), 1f);

		// 차오르는 칸(크기 0 → 1)
		_fills.Add(MakeQuad("Fill", center, new Color(1f, 0.15f, 0.1f, FillAlpha), 0f).transform);
	}

	SpriteRenderer MakeQuad(string name, Vector3 center, Color color, float scale)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(transform, false);
		go.transform.position = center;
		go.transform.localScale = Vector3.one * scale;
		SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
		sr.sprite = CellSprite();
		sr.sortingOrder = SortingOrder + 1;
		sr.color = color;
		return sr;
	}

	void AddSpreadRings(IEnumerable<int> targetIds)
	{
		_spreadRadius = Table.TableManager.Configs.Get(ConfigSpreadDistance).value[0];
		foreach (int id in targetIds)
		{
			GameObject go = Managers.Object.FindById(id);
			CreatureController cc = go != null ? go.GetComponent<CreatureController>() : null;
			if (cc == null)
				continue;
			GameObject ringGo = new GameObject("SpreadRing");
			ringGo.transform.SetParent(transform, false);
			LineRenderer lr = ringGo.AddComponent<LineRenderer>();
			lr.useWorldSpace = true;
			lr.loop = true;
			lr.positionCount = 32;
			lr.widthMultiplier = 0.06f;
			lr.material = LineMaterial();
			lr.sortingOrder = SortingOrder + 2;
			_spread.Add((cc, lr));
		}
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

	static Material LineMaterial()
	{
		if (s_lineMaterial == null)
			s_lineMaterial = new Material(Shader.Find("Sprites/Default"));
		return s_lineMaterial;
	}

	void Update()
	{
		_elapsed += Time.deltaTime;
		float t = _duration > 0 ? Mathf.Clamp01(_elapsed / _duration) : 1f;
		foreach (Transform f in _fills)
			if (f != null)
				f.localScale = Vector3.one * t;

		UpdateSpreadRings();

		// 발동 순간 서버가 지우지 않아도 예고 시간이 끝나면 사라진다
		if (_elapsed >= _duration)
			Destroy(gameObject);
	}

	// 산개 원: 표식 대상을 따라가고, 다른 표식이 판정 거리(칸) 안이면 붉게
	void UpdateSpreadRings()
	{
		for (int i = 0; i < _spread.Count; i++)
		{
			(CreatureController target, LineRenderer ring) = _spread[i];
			if (target == null || ring == null)
				continue;

			bool overlap = false;
			for (int j = 0; j < _spread.Count && overlap == false; j++)
			{
				if (i == j || _spread[j].target == null)
					continue;
				Vector3Int d = _spread[j].target.CellPos - target.CellPos;
				overlap = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= _spreadRadius;
			}

			Color color = overlap ? new Color(1f, 0.2f, 0.15f, 0.95f) : new Color(1f, 1f, 1f, 0.8f);
			ring.startColor = color;
			ring.endColor = color;
			Vector3 center = target.transform.position;
			float radius = _spreadRadius + 0.5f;
			for (int k = 0; k < ring.positionCount; k++)
			{
				float a = k * Mathf.PI * 2f / ring.positionCount;
				ring.SetPosition(k, center + new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * radius);
			}
		}
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
