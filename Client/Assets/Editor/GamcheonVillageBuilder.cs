using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Table;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// 이승 1챕터 '감천 마을'(regions 61101) 씬을 레벨 디자인 가이드 규격으로 생성·검증한다.
// 배치는 이 파일의 좌표가 원본이다. 씬을 손으로 고쳤다면 다시 돌리기 전에 이 파일에 반영한다.
// 건물·우물·돌담·장독대·무덤·신단은 아트가 없어 단색 플레이스홀더다(교체 목록은 docs/levels/gamcheon.md).
public static class GamcheonVillageBuilder
{
    const int RegionId = 61101;
    const int PassageToForest = 62101;
    const string ScenePath = "Assets/Scenes/Single/Village_Gamcheon.unity";
    const string TerrainRoot = "Assets/Resources/Art/Tiles/Iseung/Gamcheon";
    const string PlaceholderRoot = "Assets/Placeholders/Gamcheon";
    const string HeroSprite = "Assets/Resources/Art/Characters/Protagonist/Sprites/Rotations/south.png";

    // 지역 크기(칸). 최대 변 길이는 configs 90018로 검증한다.
    const int W = 64, H = 44;

    // ---- 레이아웃 (칸 좌표, 왼쪽 아래 원점, RectInt = x, y, 폭, 높이) ----
    static readonly RectInt SouthRoad = new RectInt(31, 3, 3, 15);    // 마을 어귀 → 우물 광장 (주 동선 3칸)
    static readonly RectInt Plaza = new RectInt(26, 18, 13, 9);       // 우물 광장
    static readonly RectInt NorthRoad = new RectInt(31, 27, 3, 17);   // 광장 → 숲길 입구 (주 동선 3칸)
    static readonly RectInt WestRoad = new RectInt(5, 21, 21, 2);     // 광장 → 이장 집·아이 어머니 집 (주 동선 2칸)
    static readonly RectInt EastRoad = new RectInt(39, 21, 9, 2);     // 광장 → 노파 집 (주 동선 2칸)
    static readonly RectInt GraveTrail = new RectInt(23, 35, 8, 2);   // 북쪽 길 → 무덤 언덕 샛길
    static readonly RectInt AltarSpur = new RectInt(34, 33, 2, 2);    // 북쪽 길 → 신단
    static readonly RectInt SouthWestSpur = new RectInt(28, 6, 3, 2); // 남쪽 길 → 서쪽 초가
    static readonly RectInt SouthEastSpur = new RectInt(34, 6, 3, 2); // 남쪽 길 → 동쪽 초가

    static readonly RectInt[] MainPaths = { SouthRoad, Plaza, NorthRoad, WestRoad, EastRoad };

    class House
    {
        public string Name; public RectInt Body; public RectInt Yard; public bool TiledRoof;
        public House(string name, RectInt body, RectInt yard, bool tiledRoof = false) { Name = name; Body = body; Yard = yard; TiledRoof = tiledRoof; }
    }

    static readonly House[] Houses =
    {
        new House("House_VillageHead", new RectInt(5, 27, 7, 5), new RectInt(5, 23, 7, 4), tiledRoof: true), // 이장 집(기와)
        new House("House_ChildMother", new RectInt(15, 27, 5, 4), new RectInt(15, 23, 5, 4)),              // 아이 어머니 집(초가)
        new House("House_Crone", new RectInt(50, 27, 7, 5), new RectInt(48, 21, 11, 6)),                   // 굿을 청한 노파의 집(초가)
        new House("House_SouthWest", new RectInt(23, 8, 5, 4), new RectInt(23, 6, 5, 2)),                  // 일반 초가
        new House("House_SouthEast", new RectInt(37, 8, 5, 4), new RectInt(37, 6, 5, 2)),                  // 일반 초가
    };

    // 노파 집 돌담. 서쪽 담에 동쪽 길이 들어오는 틈(y 21~22)을 둔다.
    static readonly RectInt CroneWall = new RectInt(47, 20, 13, 13);
    static readonly RectInt CroneWallGate = new RectInt(47, 21, 1, 2);
    static readonly RectInt Well = new RectInt(31, 21, 3, 3);
    static readonly Vector2Int[] Jars = { new Vector2Int(57, 27), new Vector2Int(58, 27), new Vector2Int(57, 28), new Vector2Int(58, 28) };

    static readonly Vector2Int Spawn = new Vector2Int(32, 4);
    static readonly Vector2Int Altar = new Vector2Int(37, 34);             // 이승 신단(altarCount 1)
    static readonly Vector2Int EmptyGrave = new Vector2Int(21, 36);        // 단서 51102 '아이의 빈 무덤'(Observe)
    static readonly Vector2Int EmptyGraveFront = new Vector2Int(22, 36);   // F 상호작용 칸
    static readonly Vector2Int[] OtherGraves = { new Vector2Int(18, 38), new Vector2Int(21, 39), new Vector2Int(17, 35) };
    static readonly RectInt ForestGate = new RectInt(31, 41, 3, 3);        // passages 62101 → 안개골 숲(61102)
    static readonly RectInt ForestGateApron = new RectInt(31, 38, 3, 3);   // 관문 앞 3×3 여유

    static readonly Dictionary<int, Vector2Int> NpcSpots = new Dictionary<int, Vector2Int>
    {
        { 81101, new Vector2Int(52, 24) }, // 굿을 청한 노파 — 자기 집 마당
        { 81102, new Vector2Int(17, 24) }, // 아이의 어머니 — 자기 집 마당
        { 81103, new Vector2Int(36, 23) }, // 마을 이장 — 우물 곁
    };

    // 마을 안 나무·바위(가장자리 숲은 따로 채운다). 위치는 나무 뿌리 칸.
    static readonly Vector2Int[] VillageTrees =
    {
        new Vector2Int(25, 29), new Vector2Int(40, 29), new Vector2Int(24, 15), new Vector2Int(43, 16),
        new Vector2Int(13, 34), new Vector2Int(45, 36), new Vector2Int(9, 11), new Vector2Int(53, 12),
        new Vector2Int(27, 39), new Vector2Int(40, 40), new Vector2Int(14, 18), new Vector2Int(46, 26),
    };
    static readonly Vector2Int[] Rocks =
    {
        new Vector2Int(28, 14), new Vector2Int(37, 15), new Vector2Int(20, 31), new Vector2Int(42, 33),
        new Vector2Int(10, 16), new Vector2Int(55, 16), new Vector2Int(26, 33),
    };

    const int ForestDepth = 3; // 가장자리 숲(통행 불가) 두께

    // ---- 런타임 상태 ----
    static readonly Dictionary<int, Tile> Terrain = new Dictionary<int, Tile>();
    static readonly Dictionary<string, Tile> Placeholder = new Dictionary<string, Tile>();
    static readonly Dictionary<string, Sprite> MarkerSprite = new Dictionary<string, Sprite>();

    [MenuItem("Tools/ProjectY/Levels/Build Village_Gamcheon")]
    public static void Build()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                Debug.LogError("[Gamcheon] 저장하지 않은 씬이 열려 있어 중단합니다. 저장한 뒤 다시 실행하세요: " + SceneManager.GetSceneAt(i).path);
                return;
            }
        string[] reopen = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).path).Where(p => !string.IsNullOrEmpty(p)).ToArray();

        try
        {
            TableManager.LoadAll(new ResourcesTableReader());
            var cfg = new Cfg();
            Validate(cfg);
            LoadTerrain();
            MakePlaceholders(cfg.TilePx);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildScene(cfg);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            RenderOverview(cfg);
            Debug.Log("GAMCHEON_LEVEL_OK " + ScenePath);
        }
        catch (Exception e) { Debug.LogException(e); }
        finally
        {
            if (reopen.Length > 0 && reopen[0] != ScenePath)
            {
                EditorSceneManager.OpenScene(reopen[0], OpenSceneMode.Single);
                for (int i = 1; i < reopen.Length; i++) EditorSceneManager.OpenScene(reopen[i], OpenSceneMode.Additive);
            }
        }
    }

    // configs의 레벨 디자인 상수(90001~). 코드에 수치를 두지 않는다.
    class Cfg
    {
        public readonly int TilePx = I(90001), RefW = I(90002), RefH = I(90003), MainPathWidth = I(90007),
            MaxSide = I(90018), ClueSpacing = I(90019);
        public readonly Dictionary<string, TileLayersRow> Layers = TableManager.TileLayers.Rows.ToDictionary(r => r.name);
        static int I(int id) => Mathf.RoundToInt(TableManager.Configs.Get(id).value[0]);
    }

    // ================= 검증 =================

    static void Validate(Cfg cfg)
    {
        var errors = new List<string>();
        var region = TableManager.Regions.Get(RegionId);
        if (region.scene != Path.GetFileNameWithoutExtension(ScenePath)) errors.Add($"regions.scene({region.scene})과 씬 이름이 다르다");
        if (Mathf.Max(W, H) > cfg.MaxSide) errors.Add($"지역 최대 변 {Mathf.Max(W, H)} > {cfg.MaxSide}(90018)");
        foreach (var r in new[] { WestRoad, EastRoad, SouthRoad, NorthRoad })
            if (Mathf.Min(r.width, r.height) < cfg.MainPathWidth) errors.Add($"주 동선 폭 {Mathf.Min(r.width, r.height)} < {cfg.MainPathWidth}(90007): {r}");

        var passage = TableManager.Passages.Get(PassageToForest);
        if (passage.region != RegionId || passage.livingPass != 1) errors.Add("passages 62101이 감천에서 이승 통행 가능한 길이 아니다");
        if (region.altarCount != 1) errors.Add($"altarCount {region.altarCount} — 신단 배치 수를 다시 정해야 한다");

        var npcs = TableManager.Npcs.Rows.Where(n => n.region == RegionId).Select(n => n.npcID).OrderBy(x => x).ToArray();
        if (!npcs.SequenceEqual(NpcSpots.Keys.OrderBy(x => x))) errors.Add("npcs 테이블의 감천 NPC와 배치가 다르다: " + string.Join(",", npcs));
        var observeClues = TableManager.Clues.Rows.Where(c => c.region == RegionId && c.world == 1 && c.acquireType == 2).Select(c => c.clueID).ToArray();
        if (!observeClues.SequenceEqual(new[] { 51102 })) errors.Add("감천 조사(Observe) 단서가 배치와 다르다: " + string.Join(",", observeClues));

        var blocked = BlockedCells();
        Func<Vector2Int, bool> free = c => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H && !blocked.Contains(c);

        // 조사 대상: 앞 칸 비움, 서로 거리(90019) — 현재 1개라 거리 판정은 대상이 늘면 의미가 생긴다.
        var clueCells = new[] { EmptyGrave };
        for (int i = 0; i < clueCells.Length; i++)
            for (int j = i + 1; j < clueCells.Length; j++)
                if (Cheb(clueCells[i], clueCells[j]) < cfg.ClueSpacing) errors.Add("조사 대상 간격 부족");
        if (!free(EmptyGraveFront)) errors.Add("빈 무덤 상호작용 칸이 막혀 있다");

        // 신단 주변 3×3 비충돌(신단 칸 제외), 관문 앞 3×3 비움.
        foreach (var c in Around(Altar)) if (!free(c)) errors.Add("신단 주변 칸이 막혀 있다: " + c);
        foreach (var c in Cells(ForestGateApron)) if (!free(c)) errors.Add("숲길 입구 앞 3×3이 막혀 있다: " + c);

        // 스폰에서 모든 목적지에 걸어서 닿는가(대각선 모서리 통과 규칙이 미확정이라 4방향으로 보수적으로 판정).
        var reach = Flood(Spawn, free);
        var targets = new Dictionary<string, Vector2Int> { { "빈 무덤 앞", EmptyGraveFront }, { "숲길 입구", new Vector2Int(32, H - 1) } };
        foreach (var kv in NpcSpots) targets.Add("NPC " + kv.Key, kv.Value);
        foreach (var c in Around(Altar)) targets["신단 주변 " + c] = c;
        foreach (var kv in targets) if (!reach.Contains(kv.Value)) errors.Add("스폰에서 닿지 않는다: " + kv.Key);
        foreach (var kv in NpcSpots) if (blocked.Contains(kv.Value)) errors.Add("NPC 자리가 막혀 있다: " + kv.Key);

        if (errors.Count > 0) throw new Exception("[Gamcheon] 레벨 규격 검증 실패\n- " + string.Join("\n- ", errors));
        Debug.Log($"[Gamcheon] 규격 검증 통과 — {W}×{H}칸, 걸어서 닿는 칸 {reach.Count}, 막힌 칸 {blocked.Count}");
    }

    static int Cheb(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    static IEnumerable<Vector2Int> Cells(RectInt r) { for (int y = r.yMin; y < r.yMax; y++) for (int x = r.xMin; x < r.xMax; x++) yield return new Vector2Int(x, y); }
    static IEnumerable<Vector2Int> Around(Vector2Int c) { for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dy != 0) yield return c + new Vector2Int(dx, dy); }

    static HashSet<Vector2Int> Flood(Vector2Int start, Func<Vector2Int, bool> free)
    {
        var seen = new HashSet<Vector2Int> { start }; var q = new Queue<Vector2Int>(); q.Enqueue(start);
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (q.Count > 0) { var c = q.Dequeue(); foreach (var d in dirs) { var n = c + d; if (free(n) && seen.Add(n)) q.Enqueue(n); } }
        return seen;
    }

    // ================= 칸 분류 =================

    static HashSet<Vector2Int> DirtCells()
    {
        var set = new HashSet<Vector2Int>();
        foreach (var r in MainPaths.Concat(new[] { GraveTrail, AltarSpur, SouthWestSpur, SouthEastSpur, ForestGate, ForestGateApron }))
            set.UnionWith(Cells(r));
        foreach (var h in Houses) set.UnionWith(Cells(h.Yard));
        set.UnionWith(Cells(new RectInt(Altar.x - 1, Altar.y - 1, 3, 3)));
        set.UnionWith(Cells(new RectInt(EmptyGrave.x - 1, EmptyGrave.y - 1, 3, 2)));
        return set;
    }

    static bool IsForest(Vector2Int c)
    {
        bool edge = c.x < ForestDepth || c.x >= W - ForestDepth || c.y < ForestDepth || c.y >= H - ForestDepth;
        bool gate = c.x >= ForestGate.xMin && c.x < ForestGate.xMax && c.y >= ForestGate.yMin;
        return edge && !gate;
    }

    static IEnumerable<Vector2Int> CroneWallCells()
    {
        foreach (var c in Cells(CroneWall))
        {
            bool border = c.x == CroneWall.xMin || c.x == CroneWall.xMax - 1 || c.y == CroneWall.yMin || c.y == CroneWall.yMax - 1;
            if (border && !CroneWallGate.Contains(c)) yield return c;
        }
    }

    static HashSet<Vector2Int> BlockedCells()
    {
        var set = new HashSet<Vector2Int>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { var c = new Vector2Int(x, y); if (IsForest(c)) set.Add(c); }
        foreach (var h in Houses) set.UnionWith(Cells(h.Body));
        set.UnionWith(Cells(Well));
        set.UnionWith(CroneWallCells());
        set.UnionWith(Jars);
        set.Add(EmptyGrave); set.UnionWith(OtherGraves);
        set.Add(Altar);
        set.UnionWith(VillageTrees); // 나무는 뿌리(줄기) 칸만 막는다. 수관은 막지 않는다.
        set.UnionWith(Rocks);
        return set;
    }

    // ================= 씬 구성 =================

    static void BuildScene(Cfg cfg)
    {
        var root = new GameObject("Village_Gamcheon");
        var grid = new GameObject("Grid", typeof(Grid)); grid.transform.SetParent(root.transform);
        var layers = new Dictionary<string, Tilemap>();
        foreach (var row in cfg.Layers.Values.OrderBy(r => r.layerID))
        {
            var go = new GameObject(row.name, typeof(Tilemap), typeof(TilemapRenderer)); go.transform.SetParent(grid.transform);
            var tr = go.GetComponent<TilemapRenderer>(); tr.sortingOrder = row.sortOrder;
            if (row.collision == 1) tr.enabled = false; // 충돌 마스크는 보이지 않는다
            layers[row.name] = go.GetComponent<Tilemap>();
        }

        // Ground: 흙(lower)/잔디(upper) Wang 코너 타일. 흙 칸에 닿는 꼭짓점은 흙으로 둔다.
        var dirt = DirtCells();
        Func<int, int, bool> grassVertex = (vx, vy) =>
            !(dirt.Contains(new Vector2Int(vx - 1, vy - 1)) || dirt.Contains(new Vector2Int(vx, vy - 1)) || dirt.Contains(new Vector2Int(vx - 1, vy)) || dirt.Contains(new Vector2Int(vx, vy)));
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            layers["Ground"].SetTile(new Vector3Int(x, y, 0), Terrain[Mask(grassVertex(x, y + 1), grassVertex(x + 1, y + 1), grassVertex(x, y), grassVertex(x + 1, y))]);

        var blocked = BlockedCells();
        foreach (var c in blocked) layers["Collision"].SetTile((Vector3Int)c, Placeholder["Collision"]);

        // Structures: 집 몸체·우물·돌담. Foreground: 지붕 — 몸체 위쪽 절반과 그 윗칸(뒤로 들어간 캐릭터를 가린다).
        foreach (var h in Houses)
        {
            foreach (var c in Cells(h.Body)) layers["Structures"].SetTile((Vector3Int)c, Placeholder["HouseWall"]);
            var roof = new RectInt(h.Body.xMin, h.Body.yMin + h.Body.height / 2, h.Body.width, h.Body.height - h.Body.height / 2 + 1);
            foreach (var c in Cells(roof)) layers["Foreground"].SetTile((Vector3Int)c, Placeholder[h.TiledRoof ? "RoofTile" : "RoofThatch"]);
        }
        foreach (var c in Cells(Well)) layers["Structures"].SetTile((Vector3Int)c, Placeholder["Well"]);
        foreach (var c in CroneWallCells()) layers["Structures"].SetTile((Vector3Int)c, Placeholder["StoneWall"]);

        // Props: 장독대·무덤·신단.
        foreach (var c in Jars) layers["Props"].SetTile((Vector3Int)c, Placeholder["Jar"]);
        foreach (var c in OtherGraves) layers["Props"].SetTile((Vector3Int)c, Placeholder["Grave"]);
        layers["Props"].SetTile((Vector3Int)EmptyGrave, Placeholder["GraveEmpty"]);
        layers["Props"].SetTile((Vector3Int)Altar, Placeholder["Altar"]);

        // 나무·바위는 발밑 Y 정렬 오브젝트(아트 파이프라인 '가림과 충돌'). 정렬 순서는 Props 레이어 값을 따른다.
        int propOrder = cfg.Layers["Props"].sortOrder;
        var objects = new GameObject("YSortedObjects"); objects.transform.SetParent(root.transform);
        var tree = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainRoot + "/Props/Gamcheon_PineTree.prefab");
        var rock = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainRoot + "/Props/Gamcheon_Rock.prefab");
        int n = 0;
        foreach (var c in ForestTreeRoots()) Place(tree, objects.transform, c, "PineTree_" + n++, propOrder);
        foreach (var c in VillageTrees) Place(tree, objects.transform, c, "PineTree_" + n++, propOrder);
        n = 0;
        foreach (var c in Rocks) Place(rock, objects.transform, c, "Rock_" + n++, propOrder);

        // 게임 로직이 찾을 위치 표식(EditorOnly 시각화 + 이름 규칙). 실제 상호작용·NPC 생성은 이승 시스템 구현 때 붙인다.
        var markers = new GameObject("Markers"); markers.transform.SetParent(root.transform);
        Marker(markers.transform, "PlayerSpawn", Spawn, "Spawn");
        foreach (var kv in NpcSpots) Marker(markers.transform, "NpcSpot_" + kv.Key, kv.Value, "Npc");
        Marker(markers.transform, "Clue_51102_EmptyGrave", EmptyGrave, "Clue");
        Marker(markers.transform, "Altar_" + RegionId, Altar, "Altar");
        var gate = Marker(markers.transform, "Passage_" + PassageToForest, new Vector2Int(32, H - 1), "Passage");
        gate.transform.position = new Vector3(ForestGate.center.x, ForestGate.center.y, 0);
        var gateBox = gate.AddComponent<BoxCollider2D>(); gateBox.isTrigger = true; gateBox.size = new Vector2(ForestGate.width, ForestGate.height);

        // 기준 해상도(90002×90003)를 32px 타일로 정수배 표시하는 카메라. 플레이어 추적은 이동 구현 때 붙인다.
        var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        cam.tag = "MainCamera"; cam.orthographic = true; cam.orthographicSize = cfg.RefH / 2f / cfg.TilePx;
        cam.transform.position = new Vector3(Spawn.x + .5f, Spawn.y + .5f + cam.orthographicSize - 3, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.10f, 0.08f);
        cam.transparencySortMode = TransparencySortMode.CustomAxis; cam.transparencySortAxis = Vector3.up;
        cam.allowMSAA = false;

        // 크기 확인용 주인공(플레이어 프리팹이 생기면 지운다).
        var hero = new GameObject("Preview_Protagonist", typeof(SpriteRenderer)); hero.tag = "EditorOnly";
        hero.transform.SetParent(root.transform); hero.transform.position = new Vector3(Spawn.x + .5f, Spawn.y, 0);
        var hsr = hero.GetComponent<SpriteRenderer>(); hsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(HeroSprite);
        hsr.sortingOrder = propOrder; hsr.spriteSortPoint = SpriteSortPoint.Pivot;
    }

    // 가장자리 숲: 2칸 간격 격자, 행마다 1칸 엇갈려 빽빽하게.
    static IEnumerable<Vector2Int> ForestTreeRoots()
    {
        for (int y = 0; y < H; y += 2)
            for (int x = (y / 2) % 2; x < W; x += 2)
            {
                var c = new Vector2Int(x, y);
                if (IsForest(c)) yield return c;
            }
    }

    static void Place(GameObject prefab, Transform parent, Vector2Int cell, string name, int order)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name; go.transform.position = new Vector3(cell.x + .5f, cell.y + .5f, 0);
        var group = go.GetComponent<SortingGroup>();
        if (group) group.sortingOrder = order;
        else foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>()) sr.sortingOrder = order;
    }

    static GameObject Marker(Transform parent, string name, Vector2Int cell, string kind)
    {
        var go = new GameObject(name); go.transform.SetParent(parent);
        go.transform.position = new Vector3(cell.x + .5f, cell.y + .5f, 0);
        var vis = new GameObject("EditorVisual", typeof(SpriteRenderer)); vis.tag = "EditorOnly";
        vis.transform.SetParent(go.transform, false);
        var sr = vis.GetComponent<SpriteRenderer>(); sr.sprite = MarkerSprite[kind]; sr.sortingOrder = 200;
        return go;
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (list.Any(s => s.path == ScenePath)) return;
        list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ================= 타일·플레이스홀더 =================

    static int Mask(bool nw, bool ne, bool sw, bool se) => (nw ? 8 : 0) | (ne ? 4 : 0) | (sw ? 2 : 0) | (se ? 1 : 0);

    static void LoadTerrain()
    {
        Terrain.Clear();
        for (int m = 0; m < 16; m++)
        {
            var t = AssetDatabase.LoadAssetAtPath<Tile>($"{TerrainRoot}/TileAssets/Gamcheon_Wang_{m:D2}.asset");
            if (!t || !t.sprite) throw new Exception("감천 지면 타일 누락: " + m);
            Terrain[m] = t;
        }
    }

    static void MakePlaceholders(int px)
    {
        var tiles = new Dictionary<string, Color>
        {
            { "Collision", new Color(1f, 0.1f, 0.1f, 0.45f) },
            { "HouseWall", new Color(0.62f, 0.50f, 0.36f) },
            { "RoofThatch", new Color(0.78f, 0.66f, 0.38f) },
            { "RoofTile", new Color(0.24f, 0.26f, 0.30f) },
            { "StoneWall", new Color(0.55f, 0.55f, 0.52f) },
            { "Well", new Color(0.25f, 0.42f, 0.60f) },
            { "Jar", new Color(0.30f, 0.20f, 0.14f) },
            { "Grave", new Color(0.42f, 0.48f, 0.28f) },
            { "GraveEmpty", new Color(0.36f, 0.27f, 0.18f) },
            { "Altar", new Color(0.86f, 0.84f, 0.76f) },
        };
        var markers = new Dictionary<string, Color>
        {
            { "Spawn", new Color(0.2f, 1f, 0.3f) }, { "Npc", new Color(1f, 0.85f, 0.1f) }, { "Clue", new Color(0.2f, 0.95f, 1f) },
            { "Altar", new Color(1f, 0.5f, 0.1f) }, { "Passage", new Color(1f, 0.2f, 0.9f) },
        };
        Directory.CreateDirectory(PlaceholderRoot);
        Placeholder.Clear(); MarkerSprite.Clear();
        foreach (var kv in tiles)
        {
            var sprite = SquareSprite("PH_" + kv.Key, kv.Value, px, outlineOnly: false);
            string asset = $"{PlaceholderRoot}/PH_{kv.Key}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(asset);
            if (!tile) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, asset); }
            tile.sprite = sprite; tile.colliderType = Tile.ColliderType.None; EditorUtility.SetDirty(tile);
            Placeholder[kv.Key] = tile;
        }
        foreach (var kv in markers) MarkerSprite[kv.Key] = SquareSprite("PH_Marker_" + kv.Key, kv.Value, px, outlineOnly: true);
        AssetDatabase.SaveAssets();
    }

    static Sprite SquareSprite(string name, Color color, int px, bool outlineOnly)
    {
        string path = $"{PlaceholderRoot}/{name}.png";
        var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
        var edge = new Color(color.r * .6f, color.g * .6f, color.b * .6f, Mathf.Max(color.a, .9f));
        for (int y = 0; y < px; y++) for (int x = 0; x < px; x++)
        {
            int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(px - 1 - x, px - 1 - y));
            Color c = outlineOnly ? (d < 3 ? color : Color.clear) : (d < 1 ? edge : color);
            t.SetPixel(x, y, c);
        }
        File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.spritePixelsPerUnit = px;
        importer.wrapMode = TextureWrapMode.Clamp;
        var s = new TextureImporterSettings(); importer.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Center; s.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(s);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ================= 미리보기 =================

    static void RenderOverview(Cfg cfg)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/levels"));
        Directory.CreateDirectory(dir);
        var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
        var oldPos = cam.transform.position; float oldSize = cam.orthographicSize;
        int pw = W * cfg.TilePx, ph = H * cfg.TilePx;
        cam.transform.position = new Vector3(W / 2f, H / 2f, -10); cam.orthographicSize = H / 2f;
        var rt = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, antiAliasing = 1 };
        // 수동 Render 직전에는 SortingGroup 정렬이 아직 계산되지 않아 나무 줄기가 지면 뒤로 빠진다.
        SortingGroup.UpdateAllSortingGroups();
        cam.targetTexture = rt; cam.aspect = (float)pw / ph; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null; cam.ResetAspect(); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        File.WriteAllBytes(Path.Combine(dir, "Village_Gamcheon_Overview.png"), tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        cam.transform.position = oldPos; cam.orthographicSize = oldSize;
    }
}
