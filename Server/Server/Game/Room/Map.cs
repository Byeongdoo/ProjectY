using Google.Protobuf.Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Server.Game
{
	public struct Pos
	{
		public Pos(int y, int x) { Y = y; X = x; }
		public int Y;
		public int X;
	}

	public struct PQNode : IComparable<PQNode>
	{
		public int F;
		public int G;
		public int Y;
		public int X;

		public int CompareTo(PQNode other)
		{
			if (F == other.F)
				return 0;
			return F < other.F ? 1 : -1;
		}
	}

	public struct Vector2Int
	{
		public int x;
		public int y;

		public Vector2Int(int x, int y) { this.x = x; this.y = y; }

		public static Vector2Int up { get { return new Vector2Int(0, 1); } }
		public static Vector2Int down { get { return new Vector2Int(0, -1); } }
		public static Vector2Int left { get { return new Vector2Int(-1, 0); } }
		public static Vector2Int right { get { return new Vector2Int(1, 0); } }

		public static Vector2Int operator+(Vector2Int a, Vector2Int b)
		{
			return new Vector2Int(a.x + b.x, a.y + b.y);
		}

		public static Vector2Int operator -(Vector2Int a, Vector2Int b)
		{
			return new Vector2Int(a.x - b.x, a.y - b.y);
		}

		public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
		public int sqrMagnitude { get { return (x * x + y * y); } }
		// 대각선도 1칸으로 세므로(기획: 캐릭터 조작 및 전투 2-3) 맨해튼이 아니라 체비셰프 거리다
		public int cellDist { get { return Math.Max(Math.Abs(x), Math.Abs(y)); } }
	}

	public class Map
	{
		public int MinX { get; set; }
		public int MaxX { get; set; }
		public int MinY { get; set; }
		public int MaxY { get; set; }

		public int SizeX { get { return MaxX - MinX + 1; } }
		public int SizeY { get { return MaxY - MinY + 1; } }

		bool[,] _collision;
		GameObject[,] _objects;     // 칸을 막는 오브젝트(몬스터, 겹침을 허용하지 않을 때의 플레이어)

		// 아군끼리 같은 칸 겹침 허용(configs 90005, '레벨 디자인 가이드' 2-3).
		// 허용하면 플레이어는 _objects에 올리지 않고 칸별 인원만 센다 — 플레이어끼리는 막지 않고, 몬스터는 플레이어를 막는다.
		const int ConfigAlliesOverlap = 90005;
		bool _alliesOverlap;
		int[,] _playerCount;
		Dictionary<int, Vector2Int> _playerCells = new Dictionary<int, Vector2Int>();

		// 맵 파일과 함께 빌더가 내보낸 위치 정보(도착 영역, 시설·NPC 칸 등). 레거시 맵은 null
		public MapMarkers Markers { get; private set; }

		// blockPlayers: 몬스터처럼 플레이어에게 막히는 쪽이 물을 때 true
		public bool CanGo(Vector2Int cellPos, bool checkObjects = true, bool blockPlayers = false)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return false;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return false;

			int x = cellPos.x - MinX;
			int y = MaxY - cellPos.y;
			if (_collision[y, x])
				return false;
			if (checkObjects == false)
				return true;
			return _objects[y, x] == null && (blockPlayers == false || _playerCount[y, x] == 0);
		}

		bool OccupiesGrid(GameObject gameObject)
		{
			return gameObject.ObjectType != GameObjectType.Player || _alliesOverlap == false;
		}

		bool IsWall(Vector2Int cellPos)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return true;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return true;

			return _collision[MaxY - cellPos.y, cellPos.x - MinX];
		}

		// 대각선 이동은 직선 두 칸이 모두 벽일 때만 막는다 (모서리 통과 규칙, 사용자 결정)
		public bool IsCornerBlocked(Vector2Int from, Vector2Int dest)
		{
			if (from.x == dest.x || from.y == dest.y)
				return false;

			return IsWall(new Vector2Int(dest.x, from.y)) && IsWall(new Vector2Int(from.x, dest.y));
		}

		// 한 칸 이동(8방향)이 유효한지 판정한다
		public bool CanMove(Vector2Int from, Vector2Int dest, bool checkObjects = true)
		{
			if ((dest - from).cellDist != 1)
				return false;
			if (CanGo(dest, checkObjects) == false)
				return false;

			return IsCornerBlocked(from, dest) == false;
		}

		public GameObject Find(Vector2Int cellPos)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return null;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return null;

			int x = cellPos.x - MinX;
			int y = MaxY - cellPos.y;
			return _objects[y, x];
		}

		public bool ApplyLeave(GameObject gameObject)
		{
			if (gameObject.Room == null)
				return false;
			if (gameObject.Room.Map != this)
				return false;

			if (OccupiesGrid(gameObject) == false)
			{
				// 실제로 올려 둔 칸에서만 뺀다(처음 들어올 때 ApplyMove가 먼저 부르는 ApplyLeave가 남의 칸을 깎지 않게)
				if (_playerCells.Remove(gameObject.Id, out Vector2Int placed) == false)
					return false;
				_playerCount[MaxY - placed.y, placed.x - MinX]--;
				return true;
			}

			PositionInfo posInfo = gameObject.PosInfo;
			if (posInfo.PosX < MinX || posInfo.PosX > MaxX)
				return false;
			if (posInfo.PosY < MinY || posInfo.PosY > MaxY)
				return false;

			{
				int x = posInfo.PosX - MinX;
				int y = MaxY - posInfo.PosY;
				if (_objects[y, x] == gameObject)
					_objects[y, x] = null;
			}

			return true;
		}

		public bool ApplyMove(GameObject gameObject, Vector2Int dest)
		{
			ApplyLeave(gameObject);

			if (gameObject.Room == null)
				return false;
			if (gameObject.Room.Map != this)
				return false;

			PositionInfo posInfo = gameObject.PosInfo;
			bool isPlayer = gameObject.ObjectType == GameObjectType.Player;
			if (CanGo(dest, true, blockPlayers: isPlayer == false) == false)
				return false;

			{
				int x = dest.x - MinX;
				int y = MaxY - dest.y;
				if (OccupiesGrid(gameObject))
				{
					_objects[y, x] = gameObject;
				}
				else
				{
					_playerCount[y, x]++;
					_playerCells[gameObject.Id] = dest;
				}
			}

			// 실제 좌표 이동
			posInfo.PosX = dest.x;
			posInfo.PosY = dest.y;
			return true;
		}

		public void LoadMap(int mapId, string pathPrefix = "../../../../../Common/MapData")
		{
			LoadMap("Map_" + mapId.ToString("000"), pathPrefix);
		}

		// 기본 상대 경로에 없으면 실행 위치에서 위로 올라가며 Common/MapData 를 찾는다. 없으면 null
		public static string FindMapFile(string fileName, string pathPrefix = "../../../../../Common/MapData")
		{
			if (File.Exists($"{pathPrefix}/{fileName}"))
				return $"{pathPrefix}/{fileName}";

			DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				string candidate = Path.Combine(dir.FullName, "Common", "MapData", fileName);
				if (File.Exists(candidate))
					return candidate;
				dir = dir.Parent;
			}
			return null;
		}

		// 씬 빌더가 내보낸 맵: <이름>.txt(충돌) + <이름>.markers.json(위치 정보, 선택)
		public void LoadMap(string mapName, string pathPrefix = "../../../../../Common/MapData")
		{
			string path = FindMapFile($"{mapName}.txt", pathPrefix);
			if (path == null)
				throw new FileNotFoundException($"맵 충돌 파일이 없다: Common/MapData/{mapName}.txt");

			string markersPath = FindMapFile($"{mapName}.markers.json", pathPrefix);
			Markers = markersPath == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<MapMarkers>(File.ReadAllText(markersPath));

			_alliesOverlap = Table.TableManager.Configs.Get(ConfigAlliesOverlap).value[0] >= 1;
			_playerCells.Clear();

			// Collision 관련 파일
			string text = File.ReadAllText(path);
			StringReader reader = new StringReader(text);

			MinX = int.Parse(reader.ReadLine());
			MaxX = int.Parse(reader.ReadLine());
			MinY = int.Parse(reader.ReadLine());
			MaxY = int.Parse(reader.ReadLine());

			int xCount = MaxX - MinX + 1;
			int yCount = MaxY - MinY + 1;
			_collision = new bool[yCount, xCount];
			_objects = new GameObject[yCount, xCount];
			_playerCount = new int[yCount, xCount];

			for (int y = 0; y < yCount; y++)
			{
				string line = reader.ReadLine();
				for (int x = 0; x < xCount; x++)
				{
					_collision[y, x] = (line[x] == '1' ? true : false);
				}
			}
		}

		#region A* PathFinding

		// 상 하 좌 우 + 대각선 4방향 (배열 좌표라 y는 아래로 증가)
		// 대각선도 거리상 1칸이지만 이동 시간은 configs 20030 배율만큼 더 걸린다.
		// 비용을 같게 두면 직선 대신 지그재그 대각선 경로가 같은 비용으로 뽑히므로, 비용도 시간 기준으로 둔다.
		const int ConfigDiagonalTimeScale = 20030;
		const int StraightCost = 10;
		int[] _deltaY = new int[] { 1, -1, 0, 0, 1, 1, -1, -1 };
		int[] _deltaX = new int[] { 0, 0, -1, 1, -1, 1, -1, 1 };

		static int DiagonalCost()
		{
			return (int)Math.Round(StraightCost * Table.TableManager.Configs.Get(ConfigDiagonalTimeScale).value[0]);
		}

		// 옥타일 거리 (직선 비용 10, 대각선 비용 = 10 × 대각선 배율)
		static int Heuristic(Pos a, Pos b, int diagonalCost)
		{
			int dy = Math.Abs(a.Y - b.Y);
			int dx = Math.Abs(a.X - b.X);
			int diag = Math.Min(dy, dx);
			int straight = Math.Max(dy, dx) - diag;
			return StraightCost * straight + diagonalCost * diag;
		}

		public List<Vector2Int> FindPath(Vector2Int startCellPos, Vector2Int destCellPos, bool checkObjects = true)
		{
			List<Pos> path = new List<Pos>();

			// 점수 매기기
			// F = G + H
			// F = 최종 점수 (작을 수록 좋음, 경로에 따라 달라짐)
			// G = 시작점에서 해당 좌표까지 이동하는데 드는 비용 (작을 수록 좋음, 경로에 따라 달라짐)
			// H = 목적지에서 얼마나 가까운지 (작을 수록 좋음, 고정)

			// (y, x) 이미 방문했는지 여부 (방문 = closed 상태)
			bool[,] closed = new bool[SizeY, SizeX]; // CloseList

			// (y, x) 가는 길을 한 번이라도 발견했는지
			// 발견X => MaxValue
			// 발견O => F = G + H
			int[,] open = new int[SizeY, SizeX]; // OpenList
			for (int y = 0; y < SizeY; y++)
				for (int x = 0; x < SizeX; x++)
					open[y, x] = Int32.MaxValue;

			Pos[,] parent = new Pos[SizeY, SizeX];

			// 오픈리스트에 있는 정보들 중에서, 가장 좋은 후보를 빠르게 뽑아오기 위한 도구
			PriorityQueue<PQNode> pq = new PriorityQueue<PQNode>();

			// CellPos -> ArrayPos
			Pos pos = Cell2Pos(startCellPos);
			Pos dest = Cell2Pos(destCellPos);
			int diagonalCost = DiagonalCost();

			// 시작점 발견 (예약 진행)
			open[pos.Y, pos.X] = Heuristic(pos, dest, diagonalCost);
			pq.Push(new PQNode() { F = Heuristic(pos, dest, diagonalCost), G = 0, Y = pos.Y, X = pos.X });
			parent[pos.Y, pos.X] = new Pos(pos.Y, pos.X);

			bool reached = false;
			while (pq.Count > 0)
			{
				// 제일 좋은 후보를 찾는다
				PQNode node = pq.Pop();
				// 동일한 좌표를 여러 경로로 찾아서, 더 빠른 경로로 인해서 이미 방문(closed)된 경우 스킵
				if (closed[node.Y, node.X])
					continue;

				// 방문한다
				closed[node.Y, node.X] = true;
				// 목적지 도착했으면 바로 종료
				if (node.Y == dest.Y && node.X == dest.X)
				{
					reached = true;
					break;
				}

				// 상하좌우 등 이동할 수 있는 좌표인지 확인해서 예약(open)한다
				for (int i = 0; i < _deltaY.Length; i++)
				{
					Pos next = new Pos(node.Y + _deltaY[i], node.X + _deltaX[i]);

					// 맵 배열 범위를 벗어났으면 스킵 (목적지 예외 처리 때문에 CanGo 이전에 먼저 확인)
					if (next.Y < 0 || next.Y >= SizeY || next.X < 0 || next.X >= SizeX)
						continue;

					// 벽으로 막혀서 갈 수 없으면 스킵 (목적지 자체는 오브젝트가 있어도 허용)
					if (next.Y != dest.Y || next.X != dest.X)
					{
						if (CanGo(Pos2Cell(next), checkObjects) == false) // CellPos
							continue;
					}

					// 플레이어와 같은 모서리 통과 규칙을 적용한다
					if (IsCornerBlocked(Pos2Cell(new Pos(node.Y, node.X)), Pos2Cell(next)))
						continue;

					// 이미 방문한 곳이면 스킵
					if (closed[next.Y, next.X])
						continue;

					// 비용 계산 (G: 시작점부터의 실제 비용, H: 옥타일 거리 휴리스틱)
					bool diagonal = _deltaY[i] != 0 && _deltaX[i] != 0;
					int g = node.G + (diagonal ? diagonalCost : StraightCost);
					int h = Heuristic(next, dest, diagonalCost);
					// 다른 경로에서 더 빠른 길 이미 찾았으면 스킵
					if (open[next.Y, next.X] <= g + h)
						continue;

					// 예약 진행
					open[next.Y, next.X] = g + h;
					pq.Push(new PQNode() { F = g + h, G = g, Y = next.Y, X = next.X });
					parent[next.Y, next.X] = new Pos(node.Y, node.X);
				}
			}

			// 목적지에 도달하지 못했으면 시작 위치만 담아 반환 (호출 쪽에서 Count < 2 로 길 없음을 판단)
			if (reached == false)
				return new List<Vector2Int>() { startCellPos };

			return CalcCellPathFromParent(parent, dest);
		}

		List<Vector2Int> CalcCellPathFromParent(Pos[,] parent, Pos dest)
		{
			List<Vector2Int> cells = new List<Vector2Int>();

			int y = dest.Y;
			int x = dest.X;
			while (parent[y, x].Y != y || parent[y, x].X != x)
			{
				cells.Add(Pos2Cell(new Pos(y, x)));
				Pos pos = parent[y, x];
				y = pos.Y;
				x = pos.X;
			}
			cells.Add(Pos2Cell(new Pos(y, x)));
			cells.Reverse();

			return cells;
		}

		Pos Cell2Pos(Vector2Int cell)
		{
			// CellPos -> ArrayPos
			return new Pos(MaxY - cell.y, cell.x - MinX);
		}

		Vector2Int Pos2Cell(Pos pos)
		{
			// ArrayPos -> CellPos
			return new Vector2Int(pos.X + MinX, MaxY - pos.Y);
		}

		#endregion
	}

}
