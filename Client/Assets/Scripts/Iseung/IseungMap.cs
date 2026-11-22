using UnityEngine;
using UnityEngine.Tilemaps;

// 이승 지역 씬의 칸 판정. Collision 타일맵(이승판)에 타일이 있으면 막힌 칸이다.
// 서버·MapManager에 의존하지 않는다(이승은 클라 단독).
public class IseungMap : MonoBehaviour
{
	[SerializeField] Grid _grid;
	[SerializeField] Tilemap _ground;
	[SerializeField] Tilemap _collision;

	public Grid Grid => _grid;

	// Collision은 에디터에서 팔레트로 칠하며 보이도록 렌더러를 켜 두고, 플레이 중에는 숨긴다.
	void Awake()
	{
		if (_collision != null && _collision.TryGetComponent(out TilemapRenderer r))
			r.enabled = false;
	}

	// 지면이 깔린 범위(월드 좌표). 카메라가 맵 밖을 비추지 않게 할 때 쓴다.
	public Bounds WorldBounds
	{
		get
		{
			BoundsInt b = _ground.cellBounds;
			Vector3 min = _grid.CellToWorld(b.min);
			Vector3 max = _grid.CellToWorld(b.max);
			return new Bounds((min + max) / 2, max - min);
		}
	}

	// 정렬은 Y축으로 하므로 z는 0에 고정한다(GetCellCenterWorld는 셀 깊이의 절반을 z에 더한다).
	public Vector3 CellCenter(Vector3Int cell)
	{
		Vector3 p = _grid.GetCellCenterWorld(cell);
		p.z = 0;
		return p;
	}

	public bool IsBlocked(Vector3Int cell)
	{
		if (_ground.HasTile(cell) == false)
			return true;
		return _collision.HasTile(cell);
	}

	// 서버 Map.CanMove와 같은 규칙: 8방향 한 칸, 대각선은 직선 두 칸이 모두 막혔을 때만 막힌다.
	public bool CanMove(Vector3Int from, Vector3Int dest)
	{
		int dx = dest.x - from.x;
		int dy = dest.y - from.y;
		if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != 1)
			return false;
		if (IsBlocked(dest))
			return false;
		if (dx != 0 && dy != 0)
			return !(IsBlocked(new Vector3Int(dest.x, from.y, 0)) && IsBlocked(new Vector3Int(from.x, dest.y, 0)));
		return true;
	}
}
