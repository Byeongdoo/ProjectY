using UnityEngine;

// 이승(싱글) 전용 8방향. 서버 패킷의 MoveDir에 의존하지 않도록 따로 둔다.
// 이름은 주인공 애니메이션 클립 접미사(IDLE_SOUTH_EAST 등)와 같다.
public enum IseungDir
{
	SOUTH, SOUTH_EAST, EAST, NORTH_EAST, NORTH, NORTH_WEST, WEST, SOUTH_WEST
}

public static class IseungDirUtil
{
	public static Vector3Int ToVec(IseungDir dir)
	{
		switch (dir)
		{
			case IseungDir.SOUTH: return new Vector3Int(0, -1, 0);
			case IseungDir.SOUTH_EAST: return new Vector3Int(1, -1, 0);
			case IseungDir.EAST: return new Vector3Int(1, 0, 0);
			case IseungDir.NORTH_EAST: return new Vector3Int(1, 1, 0);
			case IseungDir.NORTH: return new Vector3Int(0, 1, 0);
			case IseungDir.NORTH_WEST: return new Vector3Int(-1, 1, 0);
			case IseungDir.WEST: return new Vector3Int(-1, 0, 0);
			default: return new Vector3Int(-1, -1, 0);
		}
	}

	// (0,0)은 호출하지 않는다.
	public static IseungDir FromVec(int x, int y)
	{
		if (y > 0) return x < 0 ? IseungDir.NORTH_WEST : (x > 0 ? IseungDir.NORTH_EAST : IseungDir.NORTH);
		if (y < 0) return x < 0 ? IseungDir.SOUTH_WEST : (x > 0 ? IseungDir.SOUTH_EAST : IseungDir.SOUTH);
		return x < 0 ? IseungDir.WEST : IseungDir.EAST;
	}
}
