using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Table;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// 멀티 프로토타입 씬(삼천도 + 산신당 저승길)과 무당 플레이어 프리팹을 만들고, 서버용 맵 데이터를 내보낸다.
//   - Assets/Scenes/Multi/Samcheondo.unity, Dungeon_87101.unity
//   - Resources/Prefabs/Creature/MudangMyPlayer.prefab, MudangPlayer.prefab
//   - ../Common/MapData/<씬>.txt(충돌) + <씬>.markers.json(입장 영역·시설 칸) — 서버 GameRoom이 읽는다
// 배치는 이 파일의 좌표가 원본이다(감천 빌더와 같은 방식). 씬을 손으로 고치면 다시 돌릴 때 덮어쓴다.
// 아트가 없어 전부 단색 플레이스홀더다(docs/levels/multi-prototype.md의 리소스 요청 목록).
public static class MultiPrototypeBuilder
{
    const string SceneDir = "Assets/Scenes/Multi";
    const string LobbyScene = "Samcheondo";          // 서버 RoomManager.LobbySceneName
    const int DungeonId = 87101;                     // 산신당 저승길
    static string DungeonScene => $"Dungeon_{DungeonId}"; // 서버 RoomManager.DungeonSceneName
    const string PlaceholderRoot = "Assets/Placeholders/Multi";
    const string PrefabDir = "Assets/Resources/Prefabs/Creature";
    const string ProtagonistRoot = "Assets/Resources/Art/Characters/Protagonist";
    const string HeroSprite = ProtagonistRoot + "/Sprites/Rotations/south.png";
    const int HeroCharacterId = 1000001;             // characterLevel 주인공 그룹

    // ================= 레벨 정의(공통) =================

    class Spot { public string Kind; public int Id; public Vector2Int Cell; public string Name; }

    class LevelDef
    {
        public string Scene; public int W, H; public bool Combat; public Define.Scene SceneType; public Color Background;
        public HashSet<Vector2Int> Walkable = new HashSet<Vector2Int>();
        public Dictionary<Vector2Int, string> GroundKind = new Dictionary<Vector2Int, string>();     // Ground 레이어 색
        public Dictionary<Vector2Int, string> StructureKind = new Dictionary<Vector2Int, string>();  // Structures 레이어(막힌 칸의 모습)
        public Dictionary<Vector2Int, string> PropKind = new Dictionary<Vector2Int, string>();       // Props 레이어(NPC·신단·불씨 표식)
        public RectInt Spawn;
        public List<Spot> Spots = new List<Spot>();
        public List<(string name, Vector2Int cell, string kind)> Markers = new List<(string, Vector2Int, string)>();
        public Action<GameObject, Transform> Decorate;    // 씬별 추가 오브젝트(사공 상호작용 등)

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H;
        public bool Free(Vector2Int c) => InBounds(c) && Walkable.Contains(c);
        public void Open(RectInt r, string ground) { foreach (var c in Cells(r)) { Walkable.Add(c); GroundKind[c] = ground; } }
        public void Close(Vector2Int c, string structure) { Walkable.Remove(c); if (structure != null) StructureKind[c] = structure; }
        public void Close(RectInt r, string structure) { foreach (var c in Cells(r)) Close(c, structure); }
    }

    class Cfg
    {
        public readonly int TilePx = I(90001), RefH = I(90003), PassageMin = I(90006), ArenaW = I(90012), ArenaH = I(90013),
            BossFireMin = I(90016), ArrivalSide = I(90020);
        public readonly float FacilityMaxSeconds = TableManager.Configs.Get(70006).value[0];
        public readonly float MoveSpeed = TableManager.CharacterLevel.Get((HeroCharacterId, 1)).moveSpeed;
        public readonly Dictionary<string, TileLayersRow> Layers = TableManager.TileLayers.Rows.ToDictionary(r => r.name);
        static int I(int id) => Mathf.RoundToInt(TableManager.Configs.Get(id).value[0]);
    }

    static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
    {
        // 삼천도: 흐린 회색빛·물안개
        { "Sand", new Color(0.55f, 0.56f, 0.55f) }, { "Path", new Color(0.47f, 0.47f, 0.45f) }, { "Water", new Color(0.30f, 0.38f, 0.44f) },
        { "Fog", new Color(0.70f, 0.72f, 0.74f) }, { "Pier", new Color(0.42f, 0.34f, 0.26f) }, { "Boat", new Color(0.30f, 0.22f, 0.16f) },
        { "House", new Color(0.36f, 0.33f, 0.30f) }, { "Board", new Color(0.50f, 0.40f, 0.28f) },
        // 저승: 낮은 채도·대비의 바닥, 붉거나 푸른 달빛
        { "UnderFloor", new Color(0.20f, 0.21f, 0.27f) }, { "UnderPath", new Color(0.25f, 0.24f, 0.30f) }, { "UnderWall", new Color(0.09f, 0.08f, 0.12f) },
        { "ArenaFloor", new Color(0.26f, 0.20f, 0.24f) },
        // 표식(Props)
        { "Npc", new Color(0.95f, 0.85f, 0.35f) }, { "Ferryman", new Color(0.95f, 0.65f, 0.25f) }, { "Altar", new Color(0.86f, 0.84f, 0.76f) },
        { "Bonfire", new Color(1f, 0.55f, 0.15f) }, { "Boss", new Color(0.55f, 0.10f, 0.12f) }, { "Clue", new Color(0.2f, 0.95f, 1f) },
        { "Collision", new Color(1f, 0.1f, 0.1f, 0.45f) },
    };

    static readonly Dictionary<string, Tile> Tiles = new Dictionary<string, Tile>();

    // ================= 진입점 =================

    [MenuItem("Tools/ProjectY/Levels/Build Multi Prototype")]
    public static void Build()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                Debug.LogError("[Multi] 저장하지 않은 씬이 열려 있어 중단합니다. 저장한 뒤 다시 실행하세요: " + SceneManager.GetSceneAt(i).path);
                return;
            }
        string[] reopen = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).path).Where(p => !string.IsNullOrEmpty(p)).ToArray();

        try
        {
            TableManager.LoadAll(new ResourcesTableReader());
            var cfg = new Cfg();
            MakeTiles(cfg.TilePx);
            BuildPlayerPrefabs(cfg);

            foreach (var def in new[] { Samcheondo(cfg), Dungeon87101(cfg) })
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                BuildScene(def, cfg);
                string path = $"{SceneDir}/{def.Scene}.unity";
                Directory.CreateDirectory(SceneDir);
                EditorSceneManager.SaveScene(scene, path);
                AddToBuildSettings(path);
                ExportMapData(def);
                Debug.Log("MULTI_LEVEL_OK " + path);
            }
        }
        catch (Exception e) { Debug.LogException(e); }
        finally
        {
            if (reopen.Length > 0)
            {
                EditorSceneManager.OpenScene(reopen[0], OpenSceneMode.Single);
                for (int i = 1; i < reopen.Length; i++) EditorSceneManager.OpenScene(reopen[i], OpenSceneMode.Additive);
            }
        }
    }

    // ================= 삼천도 =================
    // Notion '삼천도(로비) 시스템'·'레벨 디자인 가이드' 2-8 규격으로 만든 임시 배치(정식 배치 미정).
    // 전체 60×34(화면 2×2장), 북쪽이 강. 시설 5곳(lobbySpots 8801~8805) 사이는 configs 70006초 × 이동 속도 이내.

    static LevelDef Samcheondo(Cfg cfg)
    {
        var d = new LevelDef { Scene = LobbyScene, W = 60, H = 34, Combat = false, SceneType = Define.Scene.Lobby, Background = new Color(0.62f, 0.64f, 0.66f) };
        d.Open(new RectInt(0, 0, d.W, d.H), "Sand");

        // 가장자리 물안개(통행 불가)와 북쪽 강.
        for (int y = 0; y < d.H; y++) for (int x = 0; x < d.W; x++)
        {
            var c = new Vector2Int(x, y);
            if (x < 2 || x >= d.W - 2 || y < 2) d.Close(c, "Fog");
            else if (y >= 26) d.Close(c, "Water");
        }

        // 나루터: 강으로 나간 잔교(4칸 폭)와 배. 사공은 잔교 끝에 선다.
        OpenClear(d, new RectInt(28, 24, 4, 6), "Pier");
        d.Close(new RectInt(27, 30, 6, 2), "Boat");
        var ferryman = new Vector2Int(30, 28);
        AddNpcSpot(d, 8802, ferryman, "Ferryman");

        // 강가(도착): 동시에 떨어진 혼을 흩어 놓는 6×6(configs 90020). 스승이 곁에 선다.
        d.Spawn = new RectInt(6, 18, cfg.ArrivalSide, cfg.ArrivalSide);
        AddNpcSpot(d, 8801, new Vector2Int(13, 21), "Npc");

        // 명부 게시판, 주막(건물 + 앞의 주모), 저승 신단.
        d.Close(new RectInt(20, 20, 2, 1), "Board");
        d.Spots.Add(new Spot { Kind = "LobbySpot", Id = 8803, Cell = new Vector2Int(20, 20), Name = "Lobby_8803" });
        d.Close(new RectInt(44, 14, 8, 6), "House");
        AddNpcSpot(d, 8804, new Vector2Int(47, 13), "Npc");
        AddNpcSpot(d, 8805, new Vector2Int(30, 6), "Altar");

        // 큰길(보기용): 강가 → 잔교, 신단 → 잔교.
        foreach (var c in Cells(new RectInt(6, 21, 38, 2)).Concat(Cells(new RectInt(29, 7, 2, 17))))
            if (d.Walkable.Contains(c)) d.GroundKind[c] = "Path";

        d.Decorate = (root, markers) =>
        {
            var go = new GameObject("Ferry_8802"); go.transform.SetParent(markers);
            go.transform.position = new Vector3(ferryman.x + .5f, ferryman.y + .5f, 0);
            var ferry = go.AddComponent<FerryInteractable>();
            var so = new SerializedObject(ferry); so.FindProperty("_dungeonId").intValue = DungeonId; so.ApplyModifiedPropertiesWithoutUndo();
        };

        ValidateSamcheondo(d, cfg);
        return d;
    }

    static void AddNpcSpot(LevelDef d, int spotId, Vector2Int cell, string prop)
    {
        d.Close(cell, null);
        d.PropKind[cell] = prop;
        d.Spots.Add(new Spot { Kind = "LobbySpot", Id = spotId, Cell = cell, Name = "Lobby_" + spotId });
    }

    static void ValidateSamcheondo(LevelDef d, Cfg cfg)
    {
        var errors = new List<string>();
        var spotIds = TableManager.LobbySpots.Rows.Select(r => r.spotID).OrderBy(x => x).ToArray();
        if (!spotIds.SequenceEqual(d.Spots.Select(s => s.Id).OrderBy(x => x))) errors.Add("lobbySpots 시설과 배치가 다르다: " + string.Join(",", spotIds));
        var arrival = TableManager.LobbySpots.Rows.Where(r => r.isSpawn == 1).Select(r => r.spotID).ToArray();
        if (!arrival.SequenceEqual(new[] { 8801 })) errors.Add("도착 지점(isSpawn) 시설이 강가(8801)가 아니다");

        int maxDist = Mathf.FloorToInt(cfg.FacilityMaxSeconds * cfg.MoveSpeed);
        for (int i = 0; i < d.Spots.Count; i++)
            for (int j = i + 1; j < d.Spots.Count; j++)
                if (Cheb(d.Spots[i].Cell, d.Spots[j].Cell) > maxDist)
                    errors.Add($"시설 간 거리 {Cheb(d.Spots[i].Cell, d.Spots[j].Cell)} > {maxDist}칸(70006×이동 속도): {d.Spots[i].Id}-{d.Spots[j].Id}");

        foreach (var c in Cells(d.Spawn)) if (!d.Free(c)) errors.Add("도착 영역이 막혀 있다: " + c);
        var reach = Flood(d.Spawn.position, d.Free);
        foreach (var s in d.Spots)
            if (!Around(s.Cell).Any(reach.Contains)) errors.Add($"도착 영역에서 시설 {s.Id} 옆 칸에 닿지 않는다");
        if (!reach.Contains(new Vector2Int(30, 27))) errors.Add("사공 앞 칸(30,27)에 닿지 않는다");

        if (errors.Count > 0) throw new Exception("[Samcheondo] 규격 검증 실패\n- " + string.Join("\n- ", errors));
        Debug.Log($"[Samcheondo] 규격 검증 통과 — {d.W}×{d.H}칸, 시설 간 최대 {maxDist}칸 이내, 걸어서 닿는 칸 {reach.Count}");
    }

    // ================= 산신당 저승길 (dungeons 87101) =================
    // Notion '챕터별 레벨 디자인 — 1챕터 숲' 2-6의 구간 좌표(왼쪽 위 원점, y 아래로 증가)를 그대로 옮긴다.
    // 원본은 xlsx levelObjects 시트인데 저장소의 xlsx에 아직 없다 — 들어오면 이 좌표를 테이블에서 읽도록 바꾼다.
    // 문서에 좌표가 없는 산신당 앞마당(연결로 2 → 불씨 → 보스방 입구)과 입구 문은 임시로 정했다.

    static LevelDef Dungeon87101(Cfg cfg)
    {
        var d = new LevelDef { Scene = DungeonScene, W = 106, H = 58, Combat = true, SceneType = Define.Scene.Game, Background = new Color(0.05f, 0.04f, 0.08f) };
        // 기본은 전부 벽. 길(구간·연결로)만 연다('던전은 길 밖이 모두 벽이다').
        for (int y = 0; y < d.H; y++) for (int x = 0; x < d.W; x++) d.StructureKind[new Vector2Int(x, y)] = "UnderWall";
        Func<int, int, int, int, RectInt> N = (x0, x1, y0, y1) => new RectInt(x0, d.H - 1 - y1, x1 - x0 + 1, y1 - y0 + 1);
        Func<int, int, Vector2Int> P = (x, y) => new Vector2Int(x, d.H - 1 - y);

        var section1 = N(14, 102, 4, 14);   // 871011 안개골 저승판, 서→동
        var link1 = N(98, 102, 15, 27);     // 남쪽으로 꺾임(붉은 실 장막 — 구간 1 정리 시 걷힘, 전투 구현 때)
        var section2 = N(0, 103, 28, 38);   // 871012 동→서, 중간에 망자의 다리(위치 미정 — levelObjects에서)
        var link2 = N(1, 5, 39, 47);        // 붉은 실의 끝(단서 51103)
        var yard = N(1, 20, 46, 54);        // 임시: 산신당 앞마당
        var door = N(21, 21, 49, 51);       // 임시: 보스방 입구(3칸)
        var arena = N(22, 40, 44, 56);      // 871014 보스방 19×13

        foreach (var r in new[] { section1, link1, section2, link2, yard, door }) OpenClear(d, r, "UnderFloor");
        OpenClear(d, arena, "ArenaFloor");
        foreach (var c in Cells(link1).Concat(Cells(link2)).Concat(Cells(door))) d.GroundKind[c] = "UnderPath";

        d.Spawn = N(14, 17, 7, 11);         // 임시: 구간 1 서쪽 끝(구간 입구 여유 안)
        var bonfire = P(15, 50);            // 871013 구간 불씨
        var boss = P(31, 49);               // 뒤틀린 목귀 시작 위치(2×2, 기준 칸)
        var bossFire = P(23, 55);           // 보스방 불씨
        var clue = P(3, 43);                // 임시: 연결로 2 안의 단서 51103
        d.PropKind[bonfire] = "Bonfire"; d.PropKind[bossFire] = "Bonfire"; d.PropKind[boss] = "Boss"; d.PropKind[clue] = "Clue";
        d.Markers.Add(("Bonfire_871013", bonfire, "Bonfire"));
        d.Markers.Add(("BossFire_871014", bossFire, "Bonfire"));
        d.Markers.Add(("BossStart_3000101", boss, "Boss"));
        d.Markers.Add(("Clue_51103", clue, "Clue"));

        ValidateDungeon(d, cfg, new[] { link1, link2 }, arena, bonfire, boss, bossFire, door);
        return d;
    }

    static void OpenClear(LevelDef d, RectInt r, string ground)
    {
        d.Open(r, ground);
        foreach (var c in Cells(r)) d.StructureKind.Remove(c);
    }

    static void ValidateDungeon(LevelDef d, Cfg cfg, RectInt[] passages, RectInt arena, Vector2Int bonfire, Vector2Int boss, Vector2Int bossFire, RectInt door)
    {
        var errors = new List<string>();
        var dungeon = TableManager.Dungeons.Get(DungeonId);
        var sections = TableManager.DungeonSections.Rows.Where(s => s.dungeonID == DungeonId).ToArray();
        if (sections.Length != dungeon.sectionCount) errors.Add($"dungeonSections {sections.Length}행 ≠ dungeons.sectionCount {dungeon.sectionCount}");
        foreach (var p in passages) if (Mathf.Min(p.width, p.height) < cfg.PassageMin) errors.Add($"통로 폭 {Mathf.Min(p.width, p.height)} < {cfg.PassageMin}(90006): {p}");
        // 보스방 입구는 두께 1칸 벽에 낸 문이다. 가로로 지나가므로 폭은 세로 길이다.
        if (door.height < cfg.PassageMin) errors.Add($"보스방 입구 폭 {door.height} < {cfg.PassageMin}(90006): {door}");
        if (arena.width != cfg.ArenaW || arena.height != cfg.ArenaH) errors.Add($"보스 아레나 {arena.width}×{arena.height} ≠ {cfg.ArenaW}×{cfg.ArenaH}(90012·90013)");
        if (Cheb(bossFire, boss) < cfg.BossFireMin) errors.Add($"보스방 불씨-보스 {Cheb(bossFire, boss)} < {cfg.BossFireMin}(90016)");

        var reach = Flood(d.Spawn.position, d.Free);
        foreach (var c in Cells(d.Spawn)) if (!d.Free(c)) errors.Add("입장 영역이 막혀 있다: " + c);
        foreach (var (name, c) in new[] { ("구간 불씨", bonfire), ("보스방 불씨", bossFire), ("보스 시작", boss) })
            if (!reach.Contains(c)) errors.Add("입장 영역에서 닿지 않는다: " + name);

        if (errors.Count > 0) throw new Exception("[Dungeon_87101] 규격 검증 실패\n- " + string.Join("\n- ", errors));
        int doorToFire = Cheb(new Vector2Int(door.xMin, door.yMin + door.height / 2), bonfire);
        Debug.Log($"[Dungeon_87101] 규격 검증 통과 — {d.W}×{d.H}칸, 걸어서 닿는 칸 {reach.Count}, 불씨→보스방 입구 {doorToFire}칸");
    }

    // ================= 씬 구성 =================

    static void BuildScene(LevelDef d, Cfg cfg)
    {
        var root = new GameObject(d.Scene);
        var grid = new GameObject("Grid", typeof(Grid)); grid.transform.SetParent(root.transform);
        var layers = new Dictionary<string, Tilemap>();
        foreach (var row in cfg.Layers.Values.OrderBy(r => r.layerID))
        {
            var go = new GameObject(row.name, typeof(Tilemap), typeof(TilemapRenderer)); go.transform.SetParent(grid.transform);
            go.GetComponent<TilemapRenderer>().sortingOrder = row.sortOrder;
            layers[row.name] = go.GetComponent<Tilemap>();
        }

        // Ground는 맵 전체에 깐다(MapManager가 Ground 범위를 맵 크기로 쓴다). 막힌 칸은 Collision 마스크로.
        for (int y = 0; y < d.H; y++) for (int x = 0; x < d.W; x++)
        {
            var c = new Vector2Int(x, y);
            layers["Ground"].SetTile((Vector3Int)c, Tiles[d.GroundKind.TryGetValue(c, out var g) ? g : "UnderWall"]);
            if (!d.Walkable.Contains(c)) layers["Collision"].SetTile((Vector3Int)c, Tiles["Collision"]);
        }
        foreach (var kv in d.StructureKind) layers["Structures"].SetTile((Vector3Int)kv.Key, Tiles[kv.Value]);
        foreach (var kv in d.PropKind) layers["Props"].SetTile((Vector3Int)kv.Key, Tiles[kv.Value]);

        var markers = new GameObject("Markers"); markers.transform.SetParent(root.transform);
        foreach (var s in d.Spots) Mark(markers.transform, $"{s.Kind}_{s.Id}", s.Cell);
        foreach (var (name, cell, _) in d.Markers) Mark(markers.transform, name, cell);
        var spawn = Mark(markers.transform, "SpawnArea", d.Spawn.position);
        spawn.transform.position = new Vector3(d.Spawn.center.x, d.Spawn.center.y, 0);
        d.Decorate?.Invoke(root, markers.transform);

        var scene = root.AddComponent<MultiScene>();
        var so = new SerializedObject(scene);
        so.FindProperty("_sceneType").enumValueIndex = Array.IndexOf(Enum.GetValues(typeof(Define.Scene)), d.SceneType);
        so.FindProperty("_combatEnabled").boolValue = d.Combat;
        so.FindProperty("_grid").objectReferenceValue = grid.GetComponent<Grid>();
        so.FindProperty("_ground").objectReferenceValue = layers["Ground"];
        so.FindProperty("_collision").objectReferenceValue = layers["Collision"];
        so.ApplyModifiedPropertiesWithoutUndo();

        // 기준 해상도 세로(90003)를 32px 타일로 정수배 표시하는 카메라. 발밑 Y 정렬.
        var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.gameObject.AddComponent<NetCameraFollow>();
        cam.tag = "MainCamera"; cam.orthographic = true; cam.orthographicSize = cfg.RefH / 2f / cfg.TilePx;
        cam.transform.position = new Vector3(d.Spawn.center.x, d.Spawn.center.y, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = d.Background;
        cam.transparencySortMode = TransparencySortMode.CustomAxis; cam.transparencySortAxis = Vector3.up;
        cam.allowMSAA = false;
    }

    static GameObject Mark(Transform parent, string name, Vector2Int cell)
    {
        var go = new GameObject(name); go.transform.SetParent(parent);
        go.transform.position = new Vector3(cell.x + .5f, cell.y + .5f, 0);
        return go;
    }

    // ================= 무당 플레이어 프리팹 =================

    static void BuildPlayerPrefabs(Cfg cfg)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ProtagonistRoot + "/Protagonist.controller");
        if (!ctrl) throw new Exception("Protagonist.controller가 없다 — Tools > ProjectY > Levels > Gamcheon > Setup Scene을 먼저 실행한다");
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(HeroSprite);
        if (!sprite) throw new Exception("주인공 스프라이트 누락: " + HeroSprite);
        int order = cfg.Layers["Props"].sortOrder;
        Directory.CreateDirectory(PrefabDir);

        foreach (bool mine in new[] { true, false })
        {
            string name = mine ? "MudangMyPlayer" : "MudangPlayer";
            var go = new GameObject(name, typeof(SpriteRenderer), typeof(Animator));
            var sr = go.GetComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sortingOrder = order; sr.spriteSortPoint = SpriteSortPoint.Pivot;
            go.GetComponent<Animator>().runtimeAnimatorController = ctrl;
            PlayerController pc = mine ? go.AddComponent<MyPlayerController>() : go.AddComponent<PlayerController>();
            var so = new SerializedObject(pc);
            so.FindProperty("_eightDirClips").boolValue = true;
            so.FindProperty("_hpBarHeight").floatValue = sprite.bounds.max.y; // 발밑 피벗 기준 머리 위
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ================= 서버 맵 데이터 =================

    static void ExportMapData(LevelDef d)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Common/MapData"));
        Directory.CreateDirectory(dir);

        // 레거시 Map_*.txt와 같은 형식: minX, maxX, minY, maxY(포함), 그다음 위쪽 행부터 '1'=막힘
        var sb = new StringBuilder();
        sb.Append(0).Append('\n').Append(d.W - 1).Append('\n').Append(0).Append('\n').Append(d.H - 1).Append('\n');
        for (int y = d.H - 1; y >= 0; y--)
        {
            for (int x = 0; x < d.W; x++) sb.Append(d.Walkable.Contains(new Vector2Int(x, y)) ? '0' : '1');
            sb.Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, d.Scene + ".txt"), sb.ToString());

        var json = new StringBuilder();
        json.Append("{\n  \"spawn\": { \"x\": ").Append(d.Spawn.x).Append(", \"y\": ").Append(d.Spawn.y)
            .Append(", \"w\": ").Append(d.Spawn.width).Append(", \"h\": ").Append(d.Spawn.height).Append(" },\n  \"spots\": [");
        for (int i = 0; i < d.Spots.Count; i++)
        {
            var s = d.Spots[i];
            json.Append(i == 0 ? "\n" : ",\n").Append($"    {{ \"kind\": \"{s.Kind}\", \"id\": {s.Id}, \"x\": {s.Cell.x}, \"y\": {s.Cell.y} }}");
        }
        json.Append(d.Spots.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");
        File.WriteAllText(Path.Combine(dir, d.Scene + ".markers.json"), json.ToString());
    }

    // ================= 공통 =================

    static int Cheb(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    static IEnumerable<Vector2Int> Cells(RectInt r) { for (int y = r.yMin; y < r.yMax; y++) for (int x = r.xMin; x < r.xMax; x++) yield return new Vector2Int(x, y); }
    static IEnumerable<Vector2Int> Around(Vector2Int c) { for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dy != 0) yield return c + new Vector2Int(dx, dy); }

    // 4방향으로 보수적으로 판정(감천 빌더와 같은 기준)
    static HashSet<Vector2Int> Flood(Vector2Int start, Func<Vector2Int, bool> free)
    {
        var seen = new HashSet<Vector2Int> { start }; var q = new Queue<Vector2Int>(); q.Enqueue(start);
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (q.Count > 0) { var c = q.Dequeue(); foreach (var dd in dirs) { var n = c + dd; if (free(n) && seen.Add(n)) q.Enqueue(n); } }
        return seen;
    }

    static void AddToBuildSettings(string path)
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (list.Any(s => s.path == path)) return;
        list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    static void MakeTiles(int px)
    {
        Directory.CreateDirectory(PlaceholderRoot);
        Tiles.Clear();
        foreach (var kv in Colors)
        {
            string png = $"{PlaceholderRoot}/PH_{kv.Key}.png";
            var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
            var edge = new Color(kv.Value.r * .8f, kv.Value.g * .8f, kv.Value.b * .8f, kv.Value.a);
            for (int y = 0; y < px; y++) for (int x = 0; x < px; x++)
                t.SetPixel(x, y, (x == 0 || y == 0) ? edge : kv.Value);
            File.WriteAllBytes(png, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.spritePixelsPerUnit = px;
            importer.wrapMode = TextureWrapMode.Clamp;
            var s = new TextureImporterSettings(); importer.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Center; s.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(s);
            importer.SaveAndReimport();

            string asset = $"{PlaceholderRoot}/PH_{kv.Key}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(asset);
            if (!tile) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, asset); }
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png); tile.colliderType = Tile.ColliderType.None; EditorUtility.SetDirty(tile);
            Tiles[kv.Key] = tile;
        }
        AssetDatabase.SaveAssets();
    }
}
