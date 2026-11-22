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

// 이승 1챕터 '감천 마을'(regions 61101) 씬 도구.
// 맵(지면·건물·소품·충돌)은 사람이 Tile Palette로 칠하고 프리팹을 놓는다. 이 도구는 칠한 내용을 건드리지 않는다.
//  - Setup: 씬 뼈대(레이어 Tilemap, 마커, IseungMap, 주인공, 카메라)를 없는 것만 만들고 참조를 다시 잇는다.
//  - Validate: 칠한 Ground/Collision과 마커 위치로 레벨 규격을 검사하고 미리보기 PNG를 만든다.
// 위치의 원본은 씬이다. 마커(Markers/*)를 옮기면 그 칸이 스폰·NPC·단서·신단·출구 위치가 된다.
public static class GamcheonVillageBuilder
{
    const int RegionId = 61101;
    const int PassageToForest = 62101;
    const string ScenePath = "Assets/Scenes/Single/Village_Gamcheon.unity";
    const string PlaceholderRoot = "Assets/Placeholders/Gamcheon";
    const string CollisionTilePath = PlaceholderRoot + "/PH_Collision.asset";
    const string CollisionPalettePath = PlaceholderRoot + "/Palette_Collision.prefab";
    const string ProtagonistRoot = "Assets/Resources/Art/Characters/Protagonist";
    const string HeroSprite = ProtagonistRoot + "/Sprites/Rotations/south.png";

    const string RootName = "Village_Gamcheon";
    const string ObjectsName = "YSortedObjects"; // 나무·건물·소품 프리팹을 두는 곳(발밑 Y 정렬, Props 순서)

    // 마커가 없을 때만 쓰는 처음 위치(칸). 이후에는 씬의 마커 위치가 원본이다.
    static readonly Vector2Int DefaultSpawn = new Vector2Int(32, 4);
    static readonly Vector2Int DefaultAltar = new Vector2Int(37, 34);
    static readonly Vector2Int DefaultPassage = new Vector2Int(32, 42);
    static readonly Vector2Int DefaultPassageSize = new Vector2Int(3, 3);
    static readonly Dictionary<int, Vector2Int> DefaultNpc = new Dictionary<int, Vector2Int>
    {
        { 81101, new Vector2Int(52, 24) }, { 81102, new Vector2Int(17, 24) }, { 81103, new Vector2Int(36, 23) },
    };
    static readonly Dictionary<int, Vector2Int> DefaultClue = new Dictionary<int, Vector2Int> { { 51102, new Vector2Int(21, 36) } };

    // ================= Setup =================

    [MenuItem("Tools/ProjectY/Levels/Gamcheon/Setup Scene")]
    public static void Setup()
    {
        try
        {
            TableManager.LoadAll(new ResourcesTableReader());
            var cfg = new Cfg();
            var scene = OpenOrCreateScene(); if (!scene.IsValid()) return;
            var collisionTile = EnsureCollisionTile(cfg.TilePx);
            EnsureCollisionPalette(collisionTile);

            var root = FindOrCreate(null, RootName);
            var grid = Need<Grid>(FindOrCreate(root.transform, "Grid"));
            var layers = new Dictionary<string, Tilemap>();
            foreach (var row in cfg.Layers.Values.OrderBy(r => r.layerID))
            {
                var go = FindOrCreate(grid.transform, row.name);
                var tm = Need<Tilemap>(go);
                var tr = Need<TilemapRenderer>(go);
                tr.sortingOrder = row.sortOrder;
                if (row.collision == 1)
                {
                    tr.enabled = true;               // 칠하면서 보이게. 플레이 중에는 IseungMap이 숨긴다.
                    tr.sortingOrder = 1000;
                    tm.color = new Color(1, 1, 1, .5f);
                }
                go.transform.SetSiblingIndex(row.layerID % 100 - 1);
                layers[row.name] = tm;
            }
            FindOrCreate(root.transform, ObjectsName);

            // 마커: 없는 것만 처음 위치에 만든다. 있는 마커는 옮기지 않는다.
            var markers = FindOrCreate(root.transform, "Markers").transform;
            var spawn = EnsureMarker(markers, "PlayerSpawn", "Spawn", DefaultSpawn, cfg.TilePx);
            foreach (var id in RegionNpcs()) EnsureMarker(markers, "NpcSpot_" + id, "Npc", DefaultNpc.TryGetValue(id, out var c) ? c : DefaultSpawn + Vector2Int.up * 2, cfg.TilePx);
            foreach (var id in RegionObserveClues()) EnsureMarker(markers, "Clue_" + id, "Clue", DefaultClue.TryGetValue(id, out var c) ? c : DefaultSpawn + Vector2Int.up * 2, cfg.TilePx);
            var altar = EnsureMarker(markers, "Altar_" + RegionId, "Altar", DefaultAltar, cfg.TilePx);
            var altarComp = Need<IseungAltar>(altar);
            SetRefs(altarComp, ("_grid", grid));
            var altarSo = new SerializedObject(altarComp); altarSo.FindProperty("_regionId").intValue = RegionId; altarSo.ApplyModifiedPropertiesWithoutUndo();
            bool newGate = !markers.Find("Passage_" + PassageToForest);
            var gate = EnsureMarker(markers, "Passage_" + PassageToForest, "Passage", DefaultPassage, cfg.TilePx);
            var gateBox = Need<BoxCollider2D>(gate);
            gateBox.isTrigger = true;
            if (newGate) gateBox.size = DefaultPassageSize;

            // 칸 판정: Ground 범위 + Collision 마스크.
            var map = Need<IseungMap>(root);
            SetRefs(map, ("_grid", grid), ("_ground", layers["Ground"]), ("_collision", layers["Collision"]));

            // 주인공: 스폰 마커 칸에서 시작한다.
            int propOrder = cfg.Layers["Props"].sortOrder;
            var hero = FindOrCreate(root.transform, "Player");
            var hsr = Need<SpriteRenderer>(hero);
            if (!hsr.sprite) hsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(HeroSprite);
            hsr.sortingOrder = propOrder; hsr.spriteSortPoint = SpriteSortPoint.Pivot;
            var anim = Need<Animator>(hero);
            if (!anim.runtimeAnimatorController) anim.runtimeAnimatorController = ProtagonistController();
            var player = Need<IseungPlayerController>(hero);
            SetRefs(player, ("_map", map));
            var spawnCell = grid.WorldToCell(spawn.transform.position);
            var pso = new SerializedObject(player); pso.FindProperty("_cellPos").vector3IntValue = spawnCell; pso.ApplyModifiedPropertiesWithoutUndo();
            var hp = grid.GetCellCenterWorld(spawnCell); hero.transform.position = new Vector3(hp.x, hp.y, 0);

            // 기준 해상도(90002×90003)를 32px 타일로 정수배 표시하는 카메라.
            var camGo = GameObject.Find("Main Camera"); if (!camGo) camGo = new GameObject("Main Camera");
            var cam = Need<Camera>(camGo);
            if (!camGo.GetComponent<AudioListener>()) camGo.AddComponent<AudioListener>();
            var follow = Need<IseungCameraFollow>(camGo);
            SetRefs(follow, ("_target", hero.transform), ("_map", map));
            cam.tag = "MainCamera"; cam.orthographic = true; cam.orthographicSize = cfg.RefH / 2f / cfg.TilePx;
            cam.transform.position = new Vector3(hero.transform.position.x, hero.transform.position.y, -10);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.10f, 0.08f);
            cam.transparencySortMode = TransparencySortMode.CustomAxis; cam.transparencySortAxis = Vector3.up;
            cam.allowMSAA = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings();
            Debug.Log("[Gamcheon] 씬 뼈대 준비 완료. 칠한 타일·놓은 프리팹은 건드리지 않았다: " + ScenePath);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ================= Validate =================

    [MenuItem("Tools/ProjectY/Levels/Gamcheon/Validate + Overview")]
    public static void ValidateMenu()
    {
        try
        {
            TableManager.LoadAll(new ResourcesTableReader());
            var cfg = new Cfg();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var root = GameObject.Find(RootName);
            if (!root) throw new Exception("[Gamcheon] 씬 뼈대가 없다. Setup Scene을 먼저 실행한다.");
            var errors = Validate(cfg, root, out string summary);
            RenderOverview(cfg, root);
            if (errors.Count > 0) Debug.LogError("[Gamcheon] 레벨 규격 검증 실패\n- " + string.Join("\n- ", errors));
            else Debug.Log("[Gamcheon] 규격 검증 통과 — " + summary);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    // configs의 레벨 디자인 상수(90001~). 코드에 수치를 두지 않는다.
    class Cfg
    {
        public readonly int TilePx = I(90001), RefW = I(90002), RefH = I(90003), MaxSide = I(90018), ClueSpacing = I(90019);
        public readonly Dictionary<string, TileLayersRow> Layers = TableManager.TileLayers.Rows.ToDictionary(r => r.name);
        static int I(int id) => Mathf.RoundToInt(TableManager.Configs.Get(id).value[0]);
    }

    static int[] RegionNpcs() => TableManager.Npcs.Rows.Where(n => n.region == RegionId).Select(n => n.npcID).OrderBy(x => x).ToArray();
    static int[] RegionObserveClues() => TableManager.Clues.Rows.Where(c => c.region == RegionId && c.world == 1 && c.acquireType == 2).Select(c => c.clueID).OrderBy(x => x).ToArray();

    static List<string> Validate(Cfg cfg, GameObject root, out string summary)
    {
        var errors = new List<string>();
        var grid = root.GetComponentInChildren<Grid>();
        var ground = grid.transform.Find("Ground").GetComponent<Tilemap>();
        var collision = grid.transform.Find("Collision").GetComponent<Tilemap>();
        var markers = root.transform.Find("Markers");
        ground.CompressBounds();
        var gb = ground.cellBounds;

        var region = TableManager.Regions.Get(RegionId);
        if (region.scene != Path.GetFileNameWithoutExtension(ScenePath)) errors.Add($"regions.scene({region.scene})과 씬 이름이 다르다");
        if (gb.size.x == 0 || gb.size.y == 0) errors.Add("Ground에 칠한 타일이 없다");
        if (Mathf.Max(gb.size.x, gb.size.y) > cfg.MaxSide) errors.Add($"지역 최대 변 {Mathf.Max(gb.size.x, gb.size.y)} > {cfg.MaxSide}(90018)");
        var passage = TableManager.Passages.Get(PassageToForest);
        if (passage.region != RegionId || passage.livingPass != 1) errors.Add("passages 62101이 감천에서 이승 통행 가능한 길이 아니다");

        Func<Vector3Int, bool> free = c => ground.HasTile(c) && !collision.HasTile(c);
        Func<string, Transform> marker = name => markers ? markers.Find(name) : null;
        Func<Transform, Vector3Int> cellOf = t => grid.WorldToCell(t.position);

        var spawnT = marker("PlayerSpawn");
        if (!spawnT) { errors.Add("Markers/PlayerSpawn이 없다"); summary = ""; return errors; }
        var spawn = cellOf(spawnT);
        if (!free(spawn)) errors.Add("스폰 칸이 막혀 있거나 지면 밖이다: " + spawn);
        var reach = Flood(spawn, free);

        // 마커 수와 테이블 일치
        foreach (var id in RegionNpcs()) if (!marker("NpcSpot_" + id)) errors.Add("NPC 마커 없음: NpcSpot_" + id);
        foreach (var id in RegionObserveClues()) if (!marker("Clue_" + id)) errors.Add("조사 단서 마커 없음: Clue_" + id);
        int altarCount = markers ? markers.Cast<Transform>().Count(t => t.name.StartsWith("Altar_")) : 0;
        if (altarCount != region.altarCount) errors.Add($"신단 마커 {altarCount}개 ≠ altarCount {region.altarCount}");

        // NPC: 칸 비움 + 스폰에서 닿음
        foreach (var id in RegionNpcs())
        {
            var t = marker("NpcSpot_" + id); if (!t) continue;
            var c = cellOf(t);
            if (!free(c)) errors.Add($"NPC {id} 자리가 막혀 있다: {c}");
            else if (!reach.Contains(c)) errors.Add($"스폰에서 NPC {id}에 닿지 않는다: {c}");
        }

        // 조사 대상: 상하좌우 중 한 칸은 걸어서 닿아야 하고, 서로 간격(90019)
        var clueCells = new List<Vector3Int>();
        foreach (var id in RegionObserveClues())
        {
            var t = marker("Clue_" + id); if (!t) continue;
            var c = cellOf(t); clueCells.Add(c);
            if (!Cross(c).Any(reach.Contains)) errors.Add($"단서 {id}({c}) 앞에 서서 조사할 칸이 없다(상하좌우 중 걸어서 닿는 칸)");
        }
        for (int i = 0; i < clueCells.Count; i++)
            for (int j = i + 1; j < clueCells.Count; j++)
                if (Cheb(clueCells[i], clueCells[j]) < cfg.ClueSpacing) errors.Add($"조사 대상 간격 부족: {clueCells[i]} - {clueCells[j]}");

        // 신단: 주변 8칸 비충돌 + 닿음
        var altarT = marker("Altar_" + RegionId);
        if (altarT)
        {
            var a = cellOf(altarT);
            foreach (var c in Around(a)) if (!free(c)) errors.Add("신단 주변 칸이 막혀 있다: " + c);
            if (!Around(a).Any(reach.Contains)) errors.Add("스폰에서 신단에 닿지 않는다");
        }

        // 출구: 트리거 상자 안 칸이 모두 비어 있고 스폰에서 닿음
        var gateT = marker("Passage_" + PassageToForest);
        if (!gateT) errors.Add("출구 마커 없음: Passage_" + PassageToForest);
        else
        {
            var box = gateT.GetComponent<BoxCollider2D>();
            var cells = box ? BoxCells(grid, box).ToList() : new List<Vector3Int> { cellOf(gateT) };
            foreach (var c in cells) if (!free(c)) errors.Add("숲길 출구 칸이 막혀 있다: " + c);
            if (!cells.Any(reach.Contains)) errors.Add("스폰에서 숲길 출구에 닿지 않는다");
        }

        int blocked = 0;
        foreach (var p in gb.allPositionsWithin) if (!free(p)) blocked++;
        summary = $"지면 {gb.size.x}×{gb.size.y}칸, 걸어서 닿는 칸 {reach.Count}, 막힌 칸 {blocked}";
        return errors;
    }

    static int Cheb(Vector3Int a, Vector3Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    static IEnumerable<Vector3Int> Cross(Vector3Int c) { yield return c + Vector3Int.up; yield return c + Vector3Int.down; yield return c + Vector3Int.left; yield return c + Vector3Int.right; }
    static IEnumerable<Vector3Int> Around(Vector3Int c) { for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dy != 0) yield return c + new Vector3Int(dx, dy, 0); }

    static IEnumerable<Vector3Int> BoxCells(Grid grid, BoxCollider2D box)
    {
        var b = box.bounds;
        var min = grid.WorldToCell(b.min + new Vector3(.01f, .01f));
        var max = grid.WorldToCell(b.max - new Vector3(.01f, .01f));
        for (int y = min.y; y <= max.y; y++) for (int x = min.x; x <= max.x; x++) yield return new Vector3Int(x, y, 0);
    }

    // 대각선 모서리 통과 규칙이 미확정이라 4방향으로 보수적으로 판정한다.
    static HashSet<Vector3Int> Flood(Vector3Int start, Func<Vector3Int, bool> free)
    {
        var seen = new HashSet<Vector3Int>(); if (!free(start)) return seen;
        seen.Add(start); var q = new Queue<Vector3Int>(); q.Enqueue(start);
        while (q.Count > 0) { var c = q.Dequeue(); foreach (var n in Cross(c)) if (free(n) && seen.Add(n)) q.Enqueue(n); }
        return seen;
    }

    // ================= 씬·오브젝트 헬퍼 =================

    static Scene OpenOrCreateScene()
    {
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath) return active;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return default;
        if (File.Exists(ScenePath)) return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        return scene;
    }

    static T Need<T>(GameObject go) where T : Component => go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

    static GameObject FindOrCreate(Transform parent, string name)
    {
        if (parent)
        {
            var t = parent.Find(name); if (t) return t.gameObject;
        }
        else
        {
            var found = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (found) return found;
        }
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject EnsureMarker(Transform parent, string name, string kind, Vector2Int defaultCell, int px)
    {
        // 이전 이름(예: Clue_51102_EmptyGrave)도 같은 마커로 보고 이름만 맞춘다.
        var t = parent.Find(name) ?? parent.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith(name + "_"));
        if (t) t.name = name;
        GameObject go;
        if (t) go = t.gameObject;
        else
        {
            go = new GameObject(name); go.transform.SetParent(parent);
            go.transform.position = new Vector3(defaultCell.x + .5f, defaultCell.y + .5f, 0);
        }
        var vis = go.transform.Find("EditorVisual")?.gameObject;
        if (!vis) { vis = new GameObject("EditorVisual", typeof(SpriteRenderer)); vis.transform.SetParent(go.transform, false); }
        vis.tag = "EditorOnly";
        var sr = vis.GetComponent<SpriteRenderer>();
        sr.sprite = MarkerSprite(kind, px); sr.sortingOrder = 200;
        return go;
    }

    static void SetRefs(UnityEngine.Object target, params (string field, UnityEngine.Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in refs) so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 주인공 IDLE/RUN 8방향 클립을 상태 이름 = 클립 이름으로 담는다. 이미 있으면 그대로 쓴다(GUID 유지).
    static RuntimeAnimatorController ProtagonistController()
    {
        string path = ProtagonistRoot + "/Protagonist.controller";
        var existing = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
        if (existing) return existing;
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        foreach (string anim in new[] { "IDLE", "RUN" })
            foreach (string dir in Enum.GetNames(typeof(IseungDir)))
            {
                string name = anim + "_" + dir;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ProtagonistRoot}/Animations/{name}.anim");
                if (!clip) throw new Exception("주인공 애니메이션 누락: " + name);
                var state = sm.AddState(name); state.motion = clip;
                if (name == "IDLE_SOUTH") sm.defaultState = state;
            }
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (list.Any(s => s.path == ScenePath)) return;
        list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ================= 충돌 타일·팔레트·마커 표시 =================

    static Tile EnsureCollisionTile(int px)
    {
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(CollisionTilePath);
        if (tile && tile.sprite) return tile;
        Directory.CreateDirectory(PlaceholderRoot);
        var sprite = SquareSprite("PH_Collision", new Color(1f, 0.1f, 0.1f, 0.6f), px, outlineOnly: false);
        if (!tile) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, CollisionTilePath); }
        tile.sprite = sprite; tile.colliderType = Tile.ColliderType.None; EditorUtility.SetDirty(tile);
        AssetDatabase.SaveAssets();
        return tile;
    }

    // 충돌 칸 전용 팔레트(타일 1개). Tile Palette 창에서 골라 Collision 타일맵에 칠한다.
    static void EnsureCollisionPalette(Tile tile)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CollisionPalettePath)) return;
        var go = new GameObject("Palette_Collision", typeof(Grid));
        var layer = new GameObject("Layer1", typeof(Tilemap), typeof(TilemapRenderer));
        layer.transform.SetParent(go.transform, false);
        layer.GetComponent<Tilemap>().SetTile(Vector3Int.zero, tile);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, CollisionPalettePath);
        UnityEngine.Object.DestroyImmediate(go);
        var palette = ScriptableObject.CreateInstance<GridPalette>();
        palette.name = "Palette Settings"; palette.cellSizing = GridPalette.CellSizing.Automatic;
        AssetDatabase.AddObjectToAsset(palette, prefab);
        AssetDatabase.SaveAssets();
    }

    static Sprite MarkerSprite(string kind, int px)
    {
        var colors = new Dictionary<string, Color>
        {
            { "Spawn", new Color(0.2f, 1f, 0.3f) }, { "Npc", new Color(1f, 0.85f, 0.1f) }, { "Clue", new Color(0.2f, 0.95f, 1f) },
            { "Altar", new Color(1f, 0.5f, 0.1f) }, { "Passage", new Color(1f, 0.2f, 0.9f) },
        };
        string path = $"{PlaceholderRoot}/PH_Marker_{kind}.png";
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        return s ? s : SquareSprite("PH_Marker_" + kind, colors[kind], px, outlineOnly: true);
    }

    static Sprite SquareSprite(string name, Color color, int px, bool outlineOnly)
    {
        Directory.CreateDirectory(PlaceholderRoot);
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

    // 칠한 지면 전체를 1칸 = 32px로 찍는다. 충돌 레이어는 숨기고 찍는다(플레이 화면과 같게).
    static void RenderOverview(Cfg cfg, GameObject root)
    {
        var grid = root.GetComponentInChildren<Grid>();
        var ground = grid.transform.Find("Ground").GetComponent<Tilemap>();
        var colR = grid.transform.Find("Collision").GetComponent<TilemapRenderer>();
        var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
        ground.CompressBounds();
        var gb = ground.cellBounds;
        if (!cam || gb.size.x == 0) return;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/levels"));
        Directory.CreateDirectory(dir);
        var oldPos = cam.transform.position; float oldSize = cam.orthographicSize; bool colOn = colR.enabled;
        int pw = gb.size.x * cfg.TilePx, ph = gb.size.y * cfg.TilePx;
        Vector3 min = grid.CellToWorld(gb.min), max = grid.CellToWorld(gb.max);
        cam.transform.position = new Vector3((min.x + max.x) / 2, (min.y + max.y) / 2, -10); cam.orthographicSize = (max.y - min.y) / 2;
        colR.enabled = false;
        var rt = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, antiAliasing = 1 };
        // 수동 Render 직전에는 SortingGroup 정렬이 아직 계산되지 않아 일부 프리팹이 지면 뒤로 빠진다.
        SortingGroup.UpdateAllSortingGroups();
        cam.targetTexture = rt; cam.aspect = (float)pw / ph; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null; cam.ResetAspect(); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        File.WriteAllBytes(Path.Combine(dir, "Village_Gamcheon_Overview.png"), tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
        cam.transform.position = oldPos; cam.orthographicSize = oldSize; colR.enabled = colOn;
    }
}
