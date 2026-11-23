using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class GameRoom : JobSerializer
	{
		public int RoomId { get; set; }

		Dictionary<int, Player> _players = new Dictionary<int, Player>();
		Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
		Dictionary<int, Projectile> _projectiles = new Dictionary<int, Projectile>();

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

		// 누군가 주기적으로 호출해줘야 한다
		public void Update()
		{
			// 상태이상 만료. 만료 처리가 컬렉션을 바꾸지 않으므로 순회 중에 불러도 된다.
			foreach (Player player in _players.Values)
				player.Statuses.Update();

			foreach (Monster monster in _monsters.Values)
			{
				monster.Update();
			}

			foreach (Projectile projectile in _projectiles.Values)
			{
				projectile.Update();
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
				player.Statuses.Clear(notify: false);

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
						{
							p.Statuses.FillInfo(p.Info);
							spawnPacket.Objects.Add(p.Info);
						}
					}

					foreach (Monster m in _monsters.Values)
					{
						m.Statuses.FillInfo(m.Info);
						spawnPacket.Objects.Add(m.Info);
					}

					foreach (Projectile p in _projectiles.Values)
						spawnPacket.Objects.Add(p.Info);

					player.Session.Send(spawnPacket);
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = gameObject as Monster;
				_monsters.Add(gameObject.Id, monster);
				monster.Room = this;

				Map.ApplyMove(monster, new Vector2Int(monster.CellPos.x, monster.CellPos.y));
			}
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = gameObject as Projectile;
				_projectiles.Add(gameObject.Id, projectile);
				projectile.Room = this;
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
				if (_players.Remove(objectId, out player) == false)
					return;

				Map.ApplyLeave(player);
				player.Statuses.Clear(notify: false);
				player.Room = null;

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
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = null;
				if (_projectiles.Remove(objectId, out projectile) == false)
					return;

				projectile.Room = null;
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

			// 상태이상: 기절은 방향 전환도 못 하고, 결박·버팀은 제자리 방향 전환만 된다
			Vector2Int requested = new Vector2Int(movePosInfo.PosX, movePosInfo.PosY);
			bool wantsMove = requested.x != info.PosInfo.PosX || requested.y != info.PosInfo.PosY;
			if (player.Statuses.CanTurn == false || (wantsMove && player.Statuses.CanMove == false))
			{
				S_Move correction = new S_Move();
				correction.ObjectId = info.ObjectId;
				correction.PosInfo = player.PosInfo;
				player.Session.Send(correction);
				return;
			}

			// 다른 좌표로 이동할 경우의 검증. 클라는 응답을 기다리지 않고 예측 이동(이동 중 대각선 전환·되돌아가기 포함)하므로
			// 서버 칸과 정확히 한 칸 차이가 아니어도 허용 오차(configs 20049) 안이고 목표 칸이 비어 있으면 받아들인다.
			// 받아들일 수 없으면 서버 위치를 그 플레이어에게 보내 교정한다.
			Vector2Int dest = new Vector2Int(movePosInfo.PosX, movePosInfo.PosY);
			if (dest.x != info.PosInfo.PosX || dest.y != info.PosInfo.PosY)
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

		public void HandleSkill(Player player, C_Skill skillPacket)
		{
			if (player == null)
				return;

			// 삼천도는 전투가 없다('삼천도(로비) 시스템' 2-4)
			if (Kind == RoomKind.Lobby)
				return;

			ObjectInfo info = player.Info;
			if (info.PosInfo.State != CreatureState.Idle)
				return;

			// 기절 중에는 공격·스킬 불가(결박은 공격 가능)
			if (player.Statuses.CanAct == false)
				return;

			if (skillPacket.Info == null)
				return;

			// 원혼에 빙의했으면 평타 형태(근접/원거리)는 클라 요청이 아니라 spirits 테이블로 서버가 정한다
			int skillId = skillPacket.Info.SkillId;
			Table.SpiritsRow spirit = null;
			if (info.SpiritId != 0 && Table.TableManager.Spirits.TryGet(info.SpiritId, out spirit))
				skillId = spirit.basicRange <= 1 ? BasicMeleeSkillId : BasicRangedSkillId;

			// 존재하는 스킬인지 먼저 확인한다 (없는 스킬로 상태만 바뀌는 것을 방지)
			Data.Skill skillData = null;
			if (DataManager.SkillDict.TryGetValue(skillId, out skillData) == false)
				return;

			// 쿨타임 체크. 지금 스킬은 Space 평타 자리의 레거시 스킬이라 공격 속도(원혼 배율 포함)로 간격을 나눈다.
			int cooldownMs = (int)(1000 * skillData.cooldown / player.CurrentAttackSpeed);
			long now = Environment.TickCount64;
			if (player.NextSkillTick > now)
				return;
			player.NextSkillTick = now + cooldownMs;

			info.PosInfo.State = CreatureState.Skill;
			S_Skill skill = new S_Skill() { Info = new SkillInfo() };
			skill.ObjectId = info.ObjectId;
			skill.Info.SkillId = skillId;
			Broadcast(skill);

			// 클라이언트의 Idle 복귀 패킷이 오지 않더라도 쿨타임 후에는 Skill 상태에 갇히지 않게 한다
			PushAfter(cooldownMs, ResetSkillState, player);

			switch (skillData.skillType)
			{
				case SkillType.SkillAuto:
					{
						// 근접 평타: 바라보는 앞 칸을 가운데로 좌우 45도씩 넓힌다.
						// 폭 1 = 앞 칸만(무명장수), 폭 3 = 왼쪽 앞·앞·오른쪽 앞(망나니). 폭은 spirits.basicWidth
						int width = spirit != null ? Math.Max(1, spirit.basicWidth) : 1;
						int half = (width - 1) / 2;
						for (int step = -half; step <= half; step++)
						{
							MoveDir dir = GameObject.RotateDir(info.PosInfo.MoveDir, step);
							GameObject target = Map.Find(player.GetFrontCellPos(dir));
							// 삼천도·저승 모두 PvP가 없으므로 망령(몬스터)만 맞는다
							// 피해 = 공격력 × 평타 계수(spirits.basicCoef)에 피해 공식 적용('캐릭터 조작 및 전투' 2-7)
							if (target != null && target.ObjectType == GameObjectType.Monster)
								Combat.Hit(player, target, player.BasicCoef);
						}
					}
					break;
				case SkillType.SkillProjectile:
					{
						Arrow arrow = ObjectManager.Instance.Add<Arrow>();
						if (arrow == null)
							return;

						arrow.Owner = player;
						arrow.Data = skillData;
						arrow.PosInfo.State = CreatureState.Moving;
						arrow.PosInfo.MoveDir = player.PosInfo.MoveDir;
						arrow.PosInfo.PosX = player.PosInfo.PosX;
						arrow.PosInfo.PosY = player.PosInfo.PosY;
						arrow.Speed = skillData.projectile.speed;
						Push(EnterGame, arrow);
					}
					break;
			}
		}

		// 레거시 SkillData.json의 평타 스킬(1 = 근접, 2 = 화살). 원혼 평타 데이터가 spiritSkills로 옮겨지면 교체한다
		public const int BasicMeleeSkillId = 1;
		public const int BasicRangedSkillId = 2;

		void ResetSkillState(Player player)
		{
			if (player.Room != this)
				return;

			if (player.State == CreatureState.Skill)
				player.State = CreatureState.Idle;
		}

		const int ConfigFaintDuration = 20039; // 혼절 유지 시간(초)
		const int ConfigReviveHpRate = 20041;  // 부활 시 체력 비율

		// 혼절 30초 뒤 부활 예약. 혼 부르기·불씨·전멸은 다음 단계라, 지금은 던전 입장 영역에서 체력 50%로 일어난다.
		public void ScheduleFaintRevive(Player player, int faintSerial)
		{
			int ms = (int)(1000 * Table.TableManager.Configs.Get(ConfigFaintDuration).value[0]);
			PushAfter(ms, ReviveFainted, player, faintSerial);
		}

		void ReviveFainted(Player player, int faintSerial)
		{
			if (player.Room != this || player.State != CreatureState.Dead || player.FaintSerial != faintSerial)
				return;

			float rate = Table.TableManager.Configs.Get(ConfigReviveHpRate).value[0];
			player.Hp = Math.Max(1, (int)Math.Round(player.Stat.MaxHp * rate));
			player.State = CreatureState.Idle;
			player.Dir = MoveDir.Down;
			Map.ApplyMove(player, PickSpawnCell());

			Broadcast(new S_ChangeHp() { ObjectId = player.Id, Hp = player.Hp });
			Broadcast(new S_Move() { ObjectId = player.Id, PosInfo = player.PosInfo });
		}

		// 망령이 노릴 수 있는 플레이어인지(같은 룸, 혼절 아님)
		public bool IsTargetable(Player player)
		{
			return player != null && player.Room == this && player.State != CreatureState.Dead;
		}

		// 그 칸에 서 있는 노릴 수 있는 플레이어들(아군끼리는 한 칸에 겹칠 수 있다)
		public List<Player> TargetablePlayersAt(Vector2Int cell)
		{
			List<Player> found = new List<Player>();
			foreach (Player p in _players.Values)
				if (IsTargetable(p) && p.CellPos.x == cell.x && p.CellPos.y == cell.y)
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
