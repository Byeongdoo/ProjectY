using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.Game
{
	public partial class GameRoom : JobSerializer
	{
		public int RoomId { get; set; }

		Dictionary<int, Player> _players = new Dictionary<int, Player>();
		Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
		Dictionary<int, Field> _fields = new Dictionary<int, Field>();

		public Map Map { get; private set; } = new Map();

		public RoomKind Kind { get; private set; }
		public string SceneName { get; private set; }  // 클라가 불러올 씬 = Common/MapData 맵 이름
		public int DungeonId { get; private set; }     // 던전 룸만. dungeons.dungeonID

		static readonly Random _random = new Random();

		// 맵은 생성 시점에 바로 읽는다(파일이 없으면 룸을 만들지 않도록 예외를 호출 쪽으로 올린다)
		public GameRoom(RoomKind kind, string sceneName, int dungeonId)
		{
			Kind = kind;
			SceneName = sceneName;
			DungeonId = dungeonId;
			Map.LoadMap(sceneName);
			if (kind == RoomKind.Dungeon)
				ResetDungeonRun();
		}

		// 던전 룸을 만들 때 한 번 망령 팩을 배치한다(DungeonSpawner). 지금은 던전별 공용 룸이라 쓰러진 망령은 서버를 다시 켜야 돌아온다.
		public void SpawnDungeonMonsters()
		{
			if (Kind != RoomKind.Dungeon)
				return;
			if (Table.TableManager.Dungeons.TryGet(DungeonId, out Table.DungeonsRow dungeon) == false)
				return;

			int count = 0;
			foreach (DungeonSpawner.PackSpawn pack in DungeonSpawner.Plan(DungeonId, _random))
			{
				List<Vector2Int> cells = DungeonSpawner.PickCells(Map, pack.Point, pack.CharacterIds.Count, _random);
				MonsterPack monsterPack = new MonsterPack();
				for (int i = 0; i < cells.Count; i++)
				{
					Monster monster = ObjectManager.Instance.Add<Monster>();
					monster.InitFromTable(pack.CharacterIds[i], dungeon.mobHpRate);
					monster.CellPos = cells[i];
					monster.SpawnCell = cells[i];
					monster.Pack = monsterPack;
					monster.PackIndex = i;
					monsterPack.Members.Add(monster);
					EnterGame(monster);
					count++;
				}
			}
			Console.WriteLine($"[{SceneName}] 망령 {count}체 배치");
		}

		// 들어오는 플레이어를 입장 영역(markers.spawn) 안의 빈 칸에 흩어 놓는다('레벨 디자인 가이드' 2-8 삼천도 도착 영역)
		Vector2Int PickSpawnCell()
		{
			CellRect area = Map.Markers?.spawn;
			if (area == null)
				return new Vector2Int(Map.MinX, Map.MinY);

			List<Vector2Int> free = new List<Vector2Int>();
			for (int y = area.y; y < area.y + area.h; y++)
				for (int x = area.x; x < area.x + area.w; x++)
					if (Map.CanGo(new Vector2Int(x, y)))
						free.Add(new Vector2Int(x, y));

			if (free.Count == 0)
				return new Vector2Int(area.x, area.y);
			return free[_random.Next(free.Count)];
		}

		long _lastUpdateTick;

		// 누군가 주기적으로 호출해줘야 한다
		public void Update()
		{
			long now = Environment.TickCount64;
			float deltaSec = _lastUpdateTick == 0 ? 0f : (now - _lastUpdateTick) / 1000f;
			_lastUpdateTick = now;

			// 상태이상 만료·지속 피해, 신력 자연 회복(전투가 있는 던전만).
			// 지속 피해로 쓰러지면 룸을 나갔다 들어오므로(목록이 바뀜) 복사본을 돈다
			foreach (Player player in _players.Values.ToList())
			{
				if (player.Room != this)
					continue;
				player.Status.Update();
				if (Kind == RoomKind.Dungeon)
					player.RegenSpirit(deltaSec);
			}

			// 혼절 시간 초과 부활, 구간 불씨, 비전투 회복
			UpdateDungeon(deltaSec);

			foreach (Monster monster in _monsters.Values.ToList())
			{
				if (monster.Room == this)
					monster.Update();
			}

			foreach (Field field in _fields.Values.ToList())
			{
				if (field.Room == this)
					field.Update();
			}

			Flush();
		}

		public void EnterGame(GameObject gameObject)
		{
			if (gameObject == null)
				return;

			GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

			if (type == GameObjectType.Player)
			{
				Player player = gameObject as Player;
				if (_players.ContainsKey(player.Id))
					return;
				_players.Add(gameObject.Id, player);
				player.Room = this;
				if (player.PendingRoom == this)
					player.PendingRoom = null;

				// 삼천도에서는 무당 본모습이다. 원혼은 던전 입장 때 나루터에서 고른다.
				if (Kind == RoomKind.Lobby)
					player.Info.SpiritId = 0;
				player.ApplyStats();

				player.CellPos = PickSpawnCell();
				player.State = CreatureState.Idle;
				player.Dir = MoveDir.Down;
				Map.ApplyMove(player, player.CellPos);

				// 본인한테 정보 전송
				{
					S_EnterGame enterPacket = new S_EnterGame();
					enterPacket.Player = player.Info;
					player.Session.Send(enterPacket);

					S_Spawn spawnPacket = new S_Spawn();
					foreach (Player p in _players.Values)
					{
						if (player != p)
							spawnPacket.Objects.Add(p.Info);
					}

					foreach (Monster m in _monsters.Values)
						spawnPacket.Objects.Add(m.Info);

					foreach (Field f in _fields.Values)
						spawnPacket.Objects.Add(f.Info);

					player.Session.Send(spawnPacket);

					if (Kind == RoomKind.Dungeon)
						player.Session.Send(DungeonState());
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = gameObject as Monster;
				_monsters.Add(gameObject.Id, monster);
				monster.Room = this;

				Map.ApplyMove(monster, new Vector2Int(monster.CellPos.x, monster.CellPos.y));
			}
			else if (type == GameObjectType.Field)
			{
				// 설치물은 칸을 막지 않는다
				Field field = gameObject as Field;
				_fields.Add(gameObject.Id, field);
				field.Room = this;
			}

			// 타인한테 정보 전송
			{
				S_Spawn spawnPacket = new S_Spawn();
				spawnPacket.Objects.Add(gameObject.Info);
				foreach (Player p in _players.Values)
				{
					if (p.Id != gameObject.Id)
						p.Session.Send(spawnPacket);
				}
			}
		}

		public void LeaveGame(int objectId)
		{
			GameObjectType type = ObjectManager.GetObjectTypeById(objectId);

			if (type == GameObjectType.Player)
			{
				// 씬을 불러오다 끊긴 플레이어도 여기로 온다(ClientSession: Room ?? PendingRoom). 원혼 예약부터 푼다.
				ReleaseSpirit(objectId);

				Player player = null;
				if (_players.TryGetValue(objectId, out player))
					CancelChannel(player);
				if (_players.Remove(objectId, out player) == false)
					return;

				Map.ApplyLeave(player);
				player.Room = null;
				player.Status.Clear();
				player.ResetCombatState();
				_atBonfire.Remove(objectId);

				// 던전: 모두 나가면 한 판을 초기화하고, 남은 사람이 전부 혼절해 있으면 전멸
				if (Kind == RoomKind.Dungeon)
				{
					if (_players.Count == 0)
						ResetDungeonRun();
					else
						CheckWipe();
				}

				// 본인한테 정보 전송
				{
					S_LeaveGame leavePacket = new S_LeaveGame();
					player.Session.Send(leavePacket);
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = null;
				if (_monsters.Remove(objectId, out monster) == false)
					return;

				Map.ApplyLeave(monster);
				monster.Room = null;
			}
			else if (type == GameObjectType.Field)
			{
				if (_fields.Remove(objectId, out Field field) == false)
					return;

				field.Room = null;
			}

			// 타인한테 정보 전송
			{
				S_Despawn despawnPacket = new S_Despawn();
				despawnPacket.ObjectIds.Add(objectId);
				foreach (Player p in _players.Values)
				{
					if (p.Id != objectId)
						p.Session.Send(despawnPacket);
				}
			}
		}

		const int ConfigMoveTolerance = 20049; // 멀티 이동 위치 허용 오차(칸)

		public void HandleMove(Player player, C_Move movePacket)
		{
			if (player == null)
				return;

			PositionInfo movePosInfo = movePacket.PosInfo;
			if (movePosInfo == null)
				return;

			ObjectInfo info = player.Info;

			// 클라이언트가 보낸 상태는 신뢰하지 않는다 (사망/스킬 상태 위조 방지)
			if (movePosInfo.State != CreatureState.Idle && movePosInfo.State != CreatureState.Moving)
				return;

			// 클라이언트는 스킬 종료 시 Idle 상태의 C_Move 로 서버 상태를 되돌리므로 Skill 상태에서의 요청은 허용한다
			if (info.PosInfo.State == CreatureState.Dead)
				return;

			// 다른 좌표로 이동할 경우의 검증. 클라는 응답을 기다리지 않고 예측 이동(이동 중 대각선 전환·되돌아가기 포함)하므로
			// 서버 칸과 정확히 한 칸 차이가 아니어도 허용 오차(configs 20049) 안이고 목표 칸이 비어 있으면 받아들인다.
			// 받아들일 수 없으면 서버 위치를 그 플레이어에게 보내 교정한다.
			Vector2Int dest = new Vector2Int(movePosInfo.PosX, movePosInfo.PosY);
			bool positionChange = dest.x != info.PosInfo.PosX || dest.y != info.PosInfo.PosY;

			// 기절·결박·버팀·시전 중에는 칸을 옮길 수 없다. 기절이면 방향도 못 바꾼다
			if (positionChange ? player.CanMove == false : player.CanAct == false)
			{
				player.Session.Send(new S_Move() { ObjectId = info.ObjectId, PosInfo = player.PosInfo, Forced = true });
				return;
			}

			// 움직이면 F 홀드(혼 부르기·보스방 불씨)가 끊긴다
			if (positionChange)
				CancelChannel(player);

			if (positionChange)
			{
				int dist = (dest - player.CellPos).cellDist;
				int tolerance = (int)Math.Round(Table.TableManager.Configs.Get(ConfigMoveTolerance).value[0]);
				bool ok = dist == 1 ? Map.CanMove(player.CellPos, dest) : dist <= tolerance && Map.CanGo(dest);
				if (ok == false)
				{
					S_Move correction = new S_Move();
					correction.ObjectId = info.ObjectId;
					correction.PosInfo = player.PosInfo;
					player.Session.Send(correction);
					return;
				}
			}

			info.PosInfo.State = movePosInfo.State;
			info.PosInfo.MoveDir = movePosInfo.MoveDir;
			Map.ApplyMove(player, new Vector2Int(movePosInfo.PosX, movePosInfo.PosY));

			// 다른 플레이어한테도 알려준다 (검증된 서버 쪽 좌표를 보낸다)
			S_Move resMovePacket = new S_Move();
			resMovePacket.ObjectId = player.Info.ObjectId;
			resMovePacket.PosInfo = player.PosInfo;

			Broadcast(resMovePacket);
		}

		// 삼천도 나루터 사공에게 원혼을 골라 던전에 들어간다('파티 및 매칭 시스템' 2-5 나루터 원혼 선택).
		// 지금은 큐·매칭·수락 없이 던전별 테스트 룸 하나에 넣는다. 원혼 중복 금지는 그 룸 안에서 지킨다('원혼(직업) 시스템' 2-3).
		// 흐름: 삼천도 잡(위치 검증) → 던전 잡(원혼 예약) → 삼천도 잡(퇴장·씬 전환). 룸 상태는 각자 잡에서만 건드린다.
		public void HandleEnterDungeon(Player player, C_EnterDungeon packet)
		{
			if (player == null || player.Room != this)
				return;
			if (Kind != RoomKind.Lobby)
				return;
			if (Table.TableManager.Dungeons.TryGet(packet.DungeonId, out _) == false)
				return;
			if (Table.TableManager.Spirits.TryGet(packet.SpiritId, out _) == false)
				return;

			// 사공(나루터 NPC) 옆 칸에서만 받는다. 클라가 보낸 요청이라도 위치는 서버가 본다.
			MapSpot ferry = Map.Markers?.FindSpot(MapSpotKind.LobbySpot, (int)LobbySpotId.Ferry);
			if (ferry == null || (player.CellPos - new Vector2Int(ferry.x, ferry.y)).cellDist > 1)
				return;

			// TODO 해금 검증: 이승 진행도는 로컬 세이브라 서버가 원본을 갖지 않는다(docs/architecture.md 미확정 항목).
			GameRoom dungeon = RoomManager.Instance.GetOrCreateDungeon(packet.DungeonId);
			if (dungeon == null)
				return;

			dungeon.Push(dungeon.ReserveSpirit, player, packet.SpiritId, this);
		}

		// 던전 룸: 원혼 → 그 원혼을 맡은 플레이어. 씬을 불러오는 중인 플레이어도 포함한다(예약).
		Dictionary<int, int> _spiritOwners = new Dictionary<int, int>();

		void ReserveSpirit(Player player, int spiritId, GameRoom lobby)
		{
			if (_spiritOwners.TryGetValue(spiritId, out int owner) && owner != player.Id)
			{
				player.Session.Send(new S_EnterDungeonReject() { SpiritId = spiritId });
				return;
			}

			ReleaseSpirit(player.Id); // 다시 고른 경우 앞선 예약을 푼다
			_spiritOwners[spiritId] = player.Id;
			lobby.Push(lobby.CompleteEnterDungeon, player, spiritId, this);
		}

		void ReleaseSpirit(int playerId)
		{
			int found = 0;
			foreach (KeyValuePair<int, int> kv in _spiritOwners)
				if (kv.Value == playerId) { found = kv.Key; break; }
			if (found != 0)
				_spiritOwners.Remove(found);
		}

		void CompleteEnterDungeon(Player player, int spiritId, GameRoom dungeon)
		{
			// 예약 사이에 접속이 끊기거나 자리를 옮겼으면 예약을 돌려준다
			if (player.Room != this || player.Session.MyPlayer != player)
			{
				dungeon.Push(dungeon.ReleaseSpirit, player.Id);
				return;
			}

			LeaveGame(player.Id);
			player.Info.SpiritId = spiritId;
			player.PendingRoom = dungeon;
			player.Session.Send(new S_ChangeScene() { SceneName = dungeon.SceneName });
		}

		// 적 처치 보상: 룸의 플레이어 전원(혼절 중 포함)이 각자 굴리고, 받은 것은 본인에게만 알린다
		public void GrantKillRewards(Monster monster)
		{
			foreach (Player p in _players.Values)
			{
				S_Loot loot = Loot.Roll(monster);
				if (loot.Items.Count > 0)
					p.Session.Send(loot);
			}
		}

		// 그 칸에 서 있는 살아 있는 플레이어들(아군끼리는 한 칸에 겹칠 수 있다). 적 특수기 판정용
		public List<Player> TargetablePlayersAt(Vector2Int cell)
		{
			List<Player> found = new List<Player>();
			foreach (Player p in _players.Values)
				if (p.IsAlive && p.CellPos.x == cell.x && p.CellPos.y == cell.y)
					found.Add(p);
			return found;
		}

		public Player FindPlayer(Func<GameObject, bool> condition)
		{
			foreach (Player player in _players.Values)
			{
				if (condition.Invoke(player))
					return player;
			}

			return null;
		}

		public void Broadcast(IMessage packet)
		{
			foreach (Player p in _players.Values)
			{
				p.Session.Send(packet);
			}
		}
	}
}
