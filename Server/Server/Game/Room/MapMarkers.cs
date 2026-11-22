using System.Collections.Generic;

namespace Server.Game
{
	// 씬 빌더(Client/Assets/Editor/MultiLevels)가 Common/MapData/<씬 이름>.markers.json 으로 내보내는 위치 정보.
	// 칸 좌표는 Unity 셀 좌표(왼쪽 아래 원점)와 같다.
	public class MapMarkers
	{
		public CellRect spawn;                          // 입장(도착) 영역. 들어오는 플레이어를 이 안의 빈 칸에 흩어 놓는다
		public List<MapSpot> spots = new List<MapSpot>(); // 시설·NPC·불씨 등 상호작용 칸

		public MapSpot FindSpot(string kind, int id)
		{
			return spots.Find(s => s.kind == kind && s.id == id);
		}
	}

	public class CellRect
	{
		public int x, y, w, h;
	}

	public class MapSpot
	{
		public string kind;  // "LobbySpot" — id = lobbySpots.spotID
		public int id;
		public int x, y;
	}
}
