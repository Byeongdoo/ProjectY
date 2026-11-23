using System.Collections.Generic;
using UnityEngine;

// 보스 패턴 이펙트(PixelLab 리소스, Prefabs/Effect/<묶음>/<묶음>_<이름>). 판정은 서버가 하고 여기서는 보여 주기만 한다.
// 어떤 패턴에 어떤 이펙트를 쓰는지는 '보스 이펙트 — 뒤틀린 목귀' 요청서(아트 제안)를 따랐다:
//   예고 칸 WarningGrid(칸마다), 탱커 강타 TankSlamImpact, 장판 RootEruption, 결박 디버프 ThreadBinding(결박이 풀릴 때까지),
//   대형 패턴(탈진이 붙는 것) MountainWailRing, 탈진 ExhaustedAura, 2페이즈 Phase2Aura.
// 리소스가 없는 보스는 아무것도 띄우지 않는다(임시 붉은 칸만).
public static class BossVfx
{
	// 보스 characterID → 이펙트 묶음 이름(리소스 폴더). 새 보스 리소스가 오면 여기에 더한다
	static readonly Dictionary<int, string> s_scope = new Dictionary<int, string>()
	{
		{ 3000101, "Mokgui" }, // 뒤틀린 목귀
	};

	const int PatternTankbuster = 1; // bossSkills.pattern
	const int PatternSpread = 3;
	const int PatternDebuff = 7;
	const int ShapeMark = 7;         // bossSkills.shape 표식형
	const int StatusRoot = 8001;
	const float OnceLifetime = 1.2f;  // 한 번 재생 이펙트(8프레임·10fps)를 지우는 시간

	public static bool HasScope(int characterId) => s_scope.ContainsKey(characterId);

	static GameObject Spawn(int characterId, string effect, Vector3 position, Transform parent = null)
	{
		if (s_scope.TryGetValue(characterId, out string scope) == false)
			return null;
		string path = $"Effect/{scope}/{scope}_{effect}";
		if (Resources.Load<GameObject>($"Prefabs/{path}") == null)
			return null;
		GameObject go = Managers.Resource.Instantiate(path, parent);
		if (go == null)
			return null;
		go.transform.position = position;
		return go;
	}

	static Vector3 CellCenter(Vector3Int cell)
	{
		return Managers.Map.CurrentGrid.CellToWorld(cell) + new Vector3(0.5f, 0.5f);
	}

	// 예고 칸 바닥 격자(칸마다 하나). 없으면 null
	public static GameObject SpawnWarningTile(int characterId, Vector3Int cell, Transform parent)
	{
		return Spawn(characterId, "WarningGrid", CellCenter(cell), parent);
	}

	// 발동: 패턴에 맞는 충격 이펙트
	public static void OnFire(MonsterController boss, Table.BossSkillsRow skill, IList<Vector3Int> cells, IList<int> targetIds)
	{
		int id = boss.CharacterId;
		if (HasScope(id) == false)
			return;

		if (skill.exhaustTime > 0)
		{
			// 대형 패턴: 보스 중심 충격파. 128px(4칸) 원본을 범위 지름에 맞춘다
			GameObject ring = Spawn(id, "MountainWailRing", boss.transform.position);
			if (ring != null)
			{
				float diameter = skill.radius * 2 + 1;
				ring.transform.localScale = Vector3.one * (diameter / 4f);
				Object.Destroy(ring, OnceLifetime);
			}
			return;
		}

		if (skill.pattern == PatternTankbuster)
		{
			foreach (CreatureController t in Targets(targetIds))
				Destroy(Spawn(id, "TankSlamImpact", t.transform.position));
			return;
		}

		if (skill.shape == ShapeMark)
		{
			// 결박을 거는 표식(디버프·산개): 대상에 붉은 실이 감겨 결박이 풀릴 때까지 남는다
			if (skill.status == StatusRoot && (skill.pattern == PatternDebuff || skill.pattern == PatternSpread))
				foreach (CreatureController t in Targets(targetIds))
					AttachWhileStatus(Spawn(id, "ThreadBinding", t.transform.position, t.transform), t, StatusRoot);
			return;
		}

		// 장판: 칸마다 뿌리가 솟는다(64px = 2칸이라 한 칸 건너 하나씩이면 충분하지만, 범위 모양이 읽히도록 칸마다 둔다)
		foreach (Vector3Int c in cells)
			Destroy(Spawn(id, "RootEruption", CellCenter(c)));
	}

	static void Destroy(GameObject go)
	{
		if (go != null)
			Object.Destroy(go, OnceLifetime);
	}

	static IEnumerable<CreatureController> Targets(IList<int> targetIds)
	{
		if (targetIds == null)
			yield break;
		foreach (int tid in targetIds)
		{
			GameObject go = Managers.Object.FindById(tid);
			CreatureController cc = go != null ? go.GetComponent<CreatureController>() : null;
			if (cc != null)
				yield return cc;
		}
	}

	// 상태이상이 걸려 있는 동안만 남는 이펙트
	static void AttachWhileStatus(GameObject fx, CreatureController owner, int statusId)
	{
		if (fx == null)
			return;
		fx.transform.localPosition = Vector3.zero;
		StatusFollower f = fx.AddComponent<StatusFollower>();
		f.Owner = owner;
		f.StatusId = statusId;
	}

	// 보스에 붙는 지속 이펙트(탈진·2페이즈). 켜면 하나만, 끄면 지운다
	public static GameObject SetAura(MonsterController boss, string effect, GameObject current, bool on)
	{
		if (on == false)
		{
			if (current != null)
				Object.Destroy(current);
			return null;
		}
		if (current != null)
			return current;
		GameObject go = Spawn(boss.CharacterId, effect, boss.transform.position, boss.transform);
		if (go != null)
			go.transform.localPosition = Vector3.zero;
		return go;
	}

	class StatusFollower : MonoBehaviour
	{
		public CreatureController Owner;
		public int StatusId;
		float _grace = 0.3f; // 상태이상 패킷이 발동보다 조금 늦게 올 수 있다

		void Update()
		{
			_grace -= Time.deltaTime;
			if (Owner == null || (_grace <= 0f && Owner.HasStatus(StatusId) == false))
				Object.Destroy(gameObject);
		}
	}
}
