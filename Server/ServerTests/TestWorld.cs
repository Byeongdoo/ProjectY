using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server;
using Server.Data;
using Server.Game;
using Xunit;

// 서버 전투 판정 테스트 공용 준비물.
//   - 마스터 테이블(Client/Assets/Resources/Data)과 실제 맵(Common/MapData)을 읽는다 — 수치는 테이블 값 그대로 검증한다
//   - 소켓 없이 보낸 패킷을 모으는 FakeSession
//   - 주사위(Combat.Roll) 고정: 기본은 "치명타 없음·편차 0"
// 테이블·ObjectManager·Combat.Roll이 정적이라 테스트를 병렬로 돌리지 않는다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ServerTests
{
	public class FakeSession : ClientSession
	{
		public readonly List<IMessage> Sent = new List<IMessage>();

		public override void Send(IMessage packet)
		{
			Sent.Add(packet);
		}

		public IEnumerable<T> Of<T>() where T : IMessage => Sent.OfType<T>();
	}

	public abstract class TestWorld : IDisposable
	{
		public const int DungeonId = 87101;          // 산신당 저승길 (Common/MapData/Dungeon_87101.txt)
		public const int SpiritTank = 8501;          // 무명장수
		public const int SpiritMelee = 8502;         // 망나니
		public const int SpiritRanged = 8503;        // 착호갑사
		public const int SpiritBuffer = 8504;        // 꼭두쇠
		public const int SpiritHealer = 8505;        // 의녀
		public const int MonsterHanged = 2000102;    // 목매단 망령(1장, 근접)
		public const int BossMokgui = 3000101;       // 뒤틀린 목귀(보스)

		static bool s_loaded;
		protected static string DataPath { get; private set; }
		static readonly object s_lock = new object();

		protected readonly GameRoom Room;
		protected readonly Vector2Int Center;      // 사방 6칸이 비어 있는 칸

		protected TestWorld()
		{
			LoadOnce();
			NoCritNoVariance();
			Room = new GameRoom(RoomKind.Dungeon, RoomManager.DungeonSceneName(DungeonId), DungeonId);
			Center = FindOpenCell(Room.Map, 6);
		}

		public void Dispose()
		{
			Combat.Roll = () => new Random().NextDouble();
		}

		static void LoadOnce()
		{
			lock (s_lock)
			{
				if (s_loaded)
					return;
				string root = FindRepoRoot();
				DataPath = Path.Combine(root, "Client", "Assets", "Resources", "Data");
				Table.TableManager.LoadAll(new FileTableReader(DataPath));
				// 디버그 명령(허수아비·테이블 다시 읽기)은 서버 설정을 본다
				typeof(ConfigManager).GetProperty(nameof(ConfigManager.Config))!
					.SetValue(null, new ServerConfig() { dataPath = DataPath, debugCommands = true });
				// 던전 실패 귀환이 삼천도 룸을 쓴다
				if (RoomManager.Instance.Lobby == null)
					RoomManager.Instance.CreateLobby();
				s_loaded = true;
			}
		}

		static string FindRepoRoot()
		{
			DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) == false)
				dir = dir.Parent;
			if (dir == null)
				throw new InvalidOperationException("저장소 루트(CLAUDE.md)를 찾지 못했다");
			return dir.FullName;
		}

		// 사방 radius칸이 모두 걸을 수 있는 칸(직선·대각선 판정이 벽에 걸리지 않게)
		static Vector2Int FindOpenCell(Map map, int radius)
		{
			for (int y = map.MinY + radius; y <= map.MaxY - radius; y++)
				for (int x = map.MinX + radius; x <= map.MaxX - radius; x++)
				{
					bool open = true;
					for (int dy = -radius; dy <= radius && open; dy++)
						for (int dx = -radius; dx <= radius && open; dx++)
							open = map.CanGo(new Vector2Int(x + dx, y + dy), checkObjects: false);
					if (open)
						return new Vector2Int(x, y);
				}
			throw new InvalidOperationException("테스트 맵에 빈 공간이 없다");
		}

		// ---- 주사위 ----

		// 치명타 안 터짐(1에 가까운 값) + 편차 0(0.5)
		protected static void NoCritNoVariance() => Rolls(0.999999, 0.5);

		// 굴릴 값을 차례로 돌려준다(치명타 → 편차 순). 다 쓰면 처음부터
		protected static void Rolls(params double[] values)
		{
			int i = 0;
			Combat.Roll = () => values[i++ % values.Length];
		}

		// ---- 오브젝트 ----

		protected Player AddPlayer(int spiritId, Vector2Int cell, MoveDir dir = MoveDir.Right)
		{
			Player p = ObjectManager.Instance.Add<Player>();
			FakeSession session = new FakeSession() { MyPlayer = p };
			p.Session = session;
			p.Info.Name = $"P{p.Id}";
			p.Info.SpiritId = spiritId;
			p.Stat.Level = 1;
			Room.EnterGame(p);
			Room.Map.ApplyMove(p, cell);
			p.Dir = dir;
			return p;
		}

		protected static FakeSession SessionOf(Player p) => (FakeSession)p.Session;

		protected TrainingDummy AddDummy(Vector2Int cell)
		{
			TrainingDummy d = ObjectManager.Instance.Add<TrainingDummy>();
			d.InitDummy(cell);
			Room.EnterGame(d);
			return d;
		}

		protected Monster AddMonster(int characterId, Vector2Int cell)
		{
			Monster m = ObjectManager.Instance.Add<Monster>();
			m.InitFromTable(characterId, 1f);
			m.CellPos = cell;
			m.SpawnCell = cell;
			Room.EnterGame(m);
			return m;
		}

		protected Vector2Int At(int dx, int dy) => Center + new Vector2Int(dx, dy);

		// ---- 입력 ----

		protected void Basic(Player p) => Room.HandleSkill(p, new C_Skill() { Info = new SkillInfo() { SkillId = 0 } });

		protected void Skill(Player p, int skillId, GameObject target = null)
		{
			// 공용 재사용 대기는 매 호출 전에 풀어 둔다(그 규칙 자체를 보는 테스트만 따로 확인)
			p.GlobalCooldownTick = 0;
			Room.HandleSkill(p, new C_Skill() { Info = new SkillInfo() { SkillId = skillId, TargetId = target?.Id ?? 0 } });
		}

		// ---- 내부 상태 읽기(망령 대상은 망령 담당 코드라 테스트 전용 접근자 대신 리플렉션으로 본다) ----

		protected static Player TargetOf(Monster m)
		{
			MethodInfo refresh = typeof(Monster).GetMethod("RefreshTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
			refresh.Invoke(m, null);
			return (Player)typeof(Monster).GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(m);
		}

		protected static float ThreatOf(Monster m, Player p)
		{
			var table = (Dictionary<Player, float>)typeof(Monster).GetField("_threat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(m);
			return table.TryGetValue(p, out float v) ? v : -1f;
		}

		protected static float Cfg(int configId) => CombatConfig.Get(configId);

		protected static int Damage(GameObject before, int hpBefore) => hpBefore - before.Stat.Hp;
	}
}
