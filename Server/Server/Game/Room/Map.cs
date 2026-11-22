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
		GameObject[,] _objects;

		public bool CanGo(Vector2Int cellPos, bool checkObjects = true)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return false;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return false;

			int x = cellPos.x - MinX;
			int y = MaxY - cellPos.y;
			return !_collision[y, x] && (!checkObjects || _objects[y, x] == null);
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
			if (CanGo(dest, true) == false)
				return false;

			{
				int x = dest.x - MinX;
				int y = MaxY - dest.y;
				_objects[y, x] = gameObject;
			}

			// 실제 좌표 이동
			posInfo.PosX = dest.x;
			posInfo.PosY = dest.y;
			return true;
		}

		public void LoadMap(int mapId, string pathPrefix = "../../../../../Common/MapData")
		{
			string mapName = "Map_" + mapId.ToString("000");

			// 기본 상대 경로에 파일이 없으면 실행 위치에서 위로 올라가며 Common/MapData 를 찾는다
			if (File.Exists($"{pathPrefix}/{mapName}.txt") == false)
			{
				DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
				while (dir != null)
				{
					string candidate = Path.Combine(dir.FullName, "Common", "MapData");
					if (File.Exists(Path.Combine(candidate, $"{mapName}.txt")))
					{
						pathPrefix = candidate;
						break;
					}
					dir = dir.Parent;
				}
			}

			// Collision 관련 파일
			string text = File.ReadAllText($"{pathPrefix}/{mapName}.txt");
			StringReader reader = new StringReader(text);

			MinX = int.Parse(reader.ReadLine());
			MaxX = int.Parse(reader.ReadLine());
			MinY = int.Parse(reader.ReadLine());
			MaxY = int.Parse(reader.ReadLine());

			int xCount = MaxX - MinX + 1;
			int yCount = MaxY - MinY + 1;
			_collision = new bool[yCount, xCount];
			_objects = new GameObject[yCount, xCount];

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
		// 대각선도 1칸이라 비용이 직선과 같다
		int[] _deltaY = new int[] { 1, -1, 0, 0, 1, 1, -1, -1 };
		int[] _deltaX = new int[] { 0, 0, -1, 1, -1, 1, -1, 1 };
		int[] _cost = new int[] { 10, 10, 10, 10, 10, 10, 10, 10 };

		static int Heuristic(Pos a, Pos b)
		{
			return 10 * Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.X - b.X));
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

			// 시작점 발견 (예약 진행)
			open[pos.Y, pos.X] = Heuristic(pos, dest);
			pq.Push(new PQNode() { F = Heuristic(pos, dest), G = 0, Y = pos.Y, X = pos.X });
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

					// 비용 계산 (G: 시작점부터의 실제 비용, H: 체비셰프 거리 휴리스틱)
					int g = node.G + _cost[i];
					int h = Heuristic(next, dest);
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
