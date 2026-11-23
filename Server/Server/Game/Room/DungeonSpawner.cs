using System;
using System.Collections.Generic;
using System.Linq;
using Table;

namespace Server.Game
{
	// 저승 던전의 망령 팩 배치('망령(적) 시스템' 2-6, '저승 던전 구조').
	//   - 어디에: levelObjects 중 그 던전 맵(levelMaps kind 2, refID = dungeonID)의 팩 지점(objType 10). size = 팩 크기(체), refID = 구간
	//   - 무엇을: 그 구간(dungeonSections)의 spawnGroups.composition 중에서 고른다
	// levelObjects 좌표는 왼쪽 위 원점(y 아래로 증가)이라 서버 맵(왼쪽 아래 원점)으로 뒤집는다(클라 MultiPrototypeBuilder와 같은 변환).
	public static class DungeonSpawner
	{
		const int MapKindDungeon = 2;   // levelMaps.kind
		const int ObjTypePackPoint = 10; // levelObjects.objType — 팩 지점

		public class PackSpawn
		{
			public int SectionId;
			public Vector2Int Point;
			public List<int> CharacterIds = new List<int>();
		}

		public static List<PackSpawn> Plan(int dungeonId, Random random)
		{
			var packs = new List<PackSpawn>();
			LevelMapsRow map = TableManager.LevelMaps.Rows.FirstOrDefault(m => m.kind == MapKindDungeon && m.refID == dungeonId);
			if (map == null)
				return packs;

			Dictionary<int, DungeonSectionsRow> sections = TableManager.DungeonSections.Rows
				.Where(s => s.dungeonID == dungeonId && s.spawnGroup != 0)
				.ToDictionary(s => s.sectionID);

			foreach (LevelObjectsRow obj in TableManager.LevelObjects.Rows)
			{
				if (obj.mapID != map.mapID || obj.objType != ObjTypePackPoint)
					continue;
				if (sections.TryGetValue(obj.refID, out DungeonSectionsRow section) == false)
					continue;
				if (TableManager.SpawnGroups.TryGet(section.spawnGroup, out SpawnGroupsRow group) == false || group.composition.Count == 0)
					continue;

				var pack = new PackSpawn { SectionId = section.sectionID, Point = new Vector2Int(obj.x, map.height - 1 - obj.y) };
				for (int i = 0; i < obj.size; i++)
					pack.CharacterIds.Add(group.composition[random.Next(group.composition.Count)]);
				packs.Add(pack);
			}
			return packs;
		}

		public const int ObjTypeBossArena = 13;     // 보스 아레나(영역)
		public const int ObjTypeBossStart = 14;     // 보스 시작 위치(refID = 보스 characterID)
		public const int ObjTypeBonfire = 12;       // 구간 불씨
		public const int ObjTypeArenaBonfire = 15;  // 보스방 불씨

		// 그 던전 맵의 levelObjects 중 objType인 지점들(서버 좌표)
		public static List<Vector2Int> Points(int dungeonId, int objType)
		{
			var points = new List<Vector2Int>();
			LevelMapsRow map = TableManager.LevelMaps.Rows.FirstOrDefault(m => m.kind == MapKindDungeon && m.refID == dungeonId);
			if (map == null)
				return points;
			foreach (LevelObjectsRow obj in TableManager.LevelObjects.Rows)
				if (obj.mapID == map.mapID && obj.objType == objType)
					points.Add(new Vector2Int(obj.x, map.height - 1 - obj.y));
			return points;
		}

		// 그 던전 맵의 levelObjects 중 objType인 영역들(서버 좌표, 양 끝 포함). levelObjects는 위쪽이 y 0이라 뒤집는다
		public static List<(Vector2Int min, Vector2Int max, int refId)> Rects(int dungeonId, int objType)
		{
			var rects = new List<(Vector2Int, Vector2Int, int)>();
			LevelMapsRow map = TableManager.LevelMaps.Rows.FirstOrDefault(m => m.kind == MapKindDungeon && m.refID == dungeonId);
			if (map == null)
				return rects;
			foreach (LevelObjectsRow obj in TableManager.LevelObjects.Rows)
				if (obj.mapID == map.mapID && obj.objType == objType)
				{
					int w = Math.Max(1, obj.w), h = Math.Max(1, obj.h);
					rects.Add((new Vector2Int(obj.x, map.height - 1 - (obj.y + h - 1)), new Vector2Int(obj.x + w - 1, map.height - 1 - obj.y), obj.refID));
				}
			return rects;
		}

		// 팩 지점에서 가까운 빈 칸부터 채운다(체비셰프 거리 순, 같은 거리면 무작위).
		public static List<Vector2Int> PickCells(Map map, Vector2Int point, int count, Random random)
		{
			var result = new List<Vector2Int>();
			int maxRing = Math.Max(map.MaxX - map.MinX, map.MaxY - map.MinY);
			for (int ring = 0; ring <= maxRing && result.Count < count; ring++)
			{
				var ringCells = new List<Vector2Int>();
				for (int dy = -ring; dy <= ring; dy++)
					for (int dx = -ring; dx <= ring; dx++)
						if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring)
							ringCells.Add(new Vector2Int(point.x + dx, point.y + dy));

				foreach (Vector2Int c in ringCells.OrderBy(_ => random.Next()))
				{
					if (result.Count >= count)
						break;
					if (map.CanGo(c) && result.Contains(c) == false)
						result.Add(c);
				}
			}
			return result;
		}
	}
}
