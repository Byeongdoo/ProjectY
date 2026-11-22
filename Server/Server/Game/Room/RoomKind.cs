namespace Server.Game
{
	public enum RoomKind
	{
		Lobby,    // 삼천도 채널
		Dungeon,  // 저승 던전 인스턴스
	}

	// markers.json의 spot.kind
	public static class MapSpotKind
	{
		public const string LobbySpot = "LobbySpot";
	}

	// lobbySpots.spotID (MasterTable). 시설 5곳
	public enum LobbySpotId
	{
		Arrival = 8801,  // 강가(도착)
		Ferry = 8802,    // 나루터(사공)
		Board = 8803,    // 명부 게시판
		Tavern = 8804,   // 주막
		Altar = 8805,    // 저승 신단
	}
}
