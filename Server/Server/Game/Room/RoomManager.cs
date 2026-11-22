using System;
using System.Collections.Generic;
using System.Threading;

namespace Server.Game
{
	public class RoomManager
	{
		public static RoomManager Instance { get; } = new RoomManager();

		// 씬 이름 = Common/MapData 맵 이름. 클라 씬 빌더(Client/Assets/Editor/MultiLevels)와 같은 규칙을 쓴다.
		public const string LobbySceneName = "Samcheondo";
		public static string DungeonSceneName(int dungeonId) => $"Dungeon_{dungeonId}";

		const int TickMs = 50;

		object _lock = new object();
		Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
		Dictionary<int, GameRoom> _dungeonRooms = new Dictionary<int, GameRoom>(); // dungeonId → 테스트용 공용 인스턴스
		int _roomId = 1;

		// 삼천도 채널. 지금은 채널 1개(채널 분할·60명 제한은 '삼천도(로비) 시스템' 2-5, 이후 단계)
		public GameRoom Lobby { get; private set; }

		public void CreateLobby()
		{
			Lobby = Add(RoomKind.Lobby, LobbySceneName, 0);
		}

		// 지금은 던전마다 룸 하나를 모두가 같이 쓴다(매칭·파티 인스턴스는 이후 단계). 맵 파일이 없으면 null
		public GameRoom GetOrCreateDungeon(int dungeonId)
		{
			lock (_lock)
			{
				if (_dungeonRooms.TryGetValue(dungeonId, out GameRoom room))
					return room;

				try
				{
					room = Add(RoomKind.Dungeon, DungeonSceneName(dungeonId), dungeonId);
				}
				catch (Exception e)
				{
					Console.WriteLine($"던전 룸 생성 실패 ({dungeonId}): {e.Message}");
					return null;
				}
				_dungeonRooms.Add(dungeonId, room);
				return room;
			}
		}

		GameRoom Add(RoomKind kind, string sceneName, int dungeonId)
		{
			GameRoom gameRoom = new GameRoom(kind, sceneName, dungeonId);

			lock (_lock)
			{
				gameRoom.RoomId = _roomId;
				_rooms.Add(_roomId, gameRoom);
				_roomId++;
			}

			StartTick(gameRoom);
			return gameRoom;
		}

		public bool Remove(int roomId)
		{
			lock (_lock)
			{
				return _rooms.Remove(roomId);
			}
		}

		public GameRoom Find(int roomId)
		{
			lock (_lock)
			{
				GameRoom room = null;
				if (_rooms.TryGetValue(roomId, out room))
					return room;

				return null;
			}
		}

		// 룸 틱은 룸마다 전용 스레드 하나에서 순차 실행한다.
		// (System.Timers.Timer는 이전 콜백이 끝나기 전에 다음 콜백이 겹쳐 실행될 수 있어서 Update/Flush가 동시에 돌 위험이 있다)
		static void StartTick(GameRoom room)
		{
			Thread thread = new Thread(() =>
			{
				while (true)
				{
					long start = Environment.TickCount64;

					try
					{
						room.Update();
					}
					catch (Exception e)
					{
						Console.WriteLine($"Room Update Failed {e}");
					}

					int elapsed = (int)(Environment.TickCount64 - start);
					if (elapsed < TickMs)
						Thread.Sleep(TickMs - elapsed);
				}
			});
			thread.IsBackground = true;
			thread.Name = $"Room_{room.RoomId}_{room.SceneName}";
			thread.Start();
		}
	}
}
