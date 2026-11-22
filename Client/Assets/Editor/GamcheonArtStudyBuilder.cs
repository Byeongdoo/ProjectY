using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

// Isolated resource authoring/QA tool. Does not run multiplayer or change Game.unity.
public static class GamcheonArtStudyBuilder
{
    const string TerrainRoot = "Assets/Resources/Art/Tiles/Iseung/Gamcheon";
    const string HeroRoot = "Assets/Resources/Art/Characters/Protagonist";
    const string StudyRoot = "Assets/ArtTests/Gamcheon";
    const string SceneRoot = "Assets/Scenes/ArtTests";
    const float Ppu = 32;
    static readonly Vector2 HeroPivot = new Vector2(32f / 64, 3f / 64);
    static readonly Dictionary<int, Tile> Tiles = new Dictionary<int, Tile>();
    static readonly Color Ink = new Color(0.07f, 0.10f, 0.14f);
    static readonly Color Paper = new Color(0.94f, 0.91f, 0.80f);
    static string Output
    {
        get
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-artStudyOutput");
            return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/pixellab/GamcheonStudy"));
        }
    }

    [Serializable] class Source { public TileData tileset_data; }
    [Serializable] class TileData { public Descriptor[] tiles; }
    [Serializable] class Descriptor { public string name; public Corners corners; public Box bounding_box; }
    [Serializable] class Corners { public string NW, NE, SW, SE; }
    [Serializable] class Box { public int x, y, width, height; }
    [Serializable] class Measurement { public string file; public int width, height, alphaX, alphaY, alphaWidth, alphaHeight; public float visibleWidthUnits, visibleHeightUnits; }
    [Serializable] class Report
    {
        public string unityVersion, tilesetId, lowerBaseTileId, upperBaseTileId, treeId, rockId;
        public int tilePixels = 32, pixelsPerUnit = 32, sourceTileCount, uniqueCornerPatterns, heroSpriteCount, heroAnimationCount, verifiedAnimationFrames;
        public Vector2 heroPivotPixels = new Vector2(32, 3);
        public Vector2 treePivotPixels = new Vector2(46, 26);
        public Vector2 rockPivotPixels = new Vector2(34, 11);
        public Measurement[] measurements;
        public string[] checks, scenes, screenshots;
    }

    [MenuItem("Tools/ProjectY/Build Gamcheon Art Study")]
    public static void BuildAndVerify()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        try
        {
            Directory.CreateDirectory(Output);
            Folder(StudyRoot + "/References"); Folder(SceneRoot); Folder(TerrainRoot + "/Ground"); Folder(TerrainRoot + "/TileAssets");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrepareTiles();
            Configure(TerrainRoot + "/Props/Gamcheon_PineTree.png", new Vector2(46f / 96, 26f / 128));
            Configure(TerrainRoot + "/Props/Gamcheon_Rock.png", new Vector2(34f / 64, 11f / 64));
            string hero = HeroRoot + "/Sprites/Rotations/south.png";
            MakeReference(hero, "Hero_Legacy64_Center", 64, new Vector2(0.5f, 0.5f));
            MakeReference(hero, "Hero_32_Center", 32, new Vector2(0.5f, 0.5f));
            MakeReference(hero, "Hero_32_Feet", 32, HeroPivot);
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                tilesetId = "db0f26a0-d2f5-4ae4-9a82-8e6a992190d4",
                lowerBaseTileId = "ba00427d-a4e7-4b8c-acb5-2d5637f5fa6a",
                upperBaseTileId = "0f5c159f-51b4-4b36-8c33-81ea00762333",
                treeId = "44b93379-51ff-4895-9956-9350933ebdde", rockId = "fd27ee9f-7e04-457f-b0dc-3beda9197f37",
                sourceTileCount = 16, uniqueCornerPatterns = Tiles.Count,
                measurements = new[] { Measure(hero), Measure(TerrainRoot + "/Props/Gamcheon_PineTree.png"), Measure(TerrainRoot + "/Props/Gamcheon_Rock.png") }
            };
            if (Tiles.Count != 16) throw new Exception("Wang set must cover all 16 corner masks.");
            BuildScaleComparison();
            VerifyOcclusion();
            BuildOverview();
            // Apply the measured study result to this newly generated hero set only.
            var heroFiles = Directory.GetFiles(HeroRoot + "/Sprites", "*.png", SearchOption.AllDirectories);
            foreach (string file in heroFiles) Configure(file.Replace('\\', '/'), HeroPivot);
            report.heroSpriteCount = heroFiles.Length;
            if (heroFiles.Length != 104) throw new Exception("Expected 104 hero sprites.");
            ValidateImports(heroFiles);
            foreach (string clipGuid in AssetDatabase.FindAssets("t:AnimationClip", new[] { HeroRoot + "/Animations" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(clipGuid));
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                if (bindings.Length != 1) throw new Exception("Expected one sprite curve: " + clip.name);
                var frames = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
                int expected = clip.name.StartsWith("IDLE_") ? 4 : 8;
                if (frames.Length != expected || frames.Any(frame => !(frame.value is Sprite))) throw new Exception("Missing animation sprite: " + clip.name);
                report.heroAnimationCount++; report.verifiedAnimationFrames += frames.Length;
            }
            if (report.heroAnimationCount != 16 || report.verifiedAnimationFrames != 96) throw new Exception("Hero animation reference count mismatch.");
            report.checks = new[] {
                "16 unique Wang corner masks, source bounding boxes used (not wang_N positions)",
                "32px tiles at 32 PPU = one Unity grid cell; all study transforms use scale 1",
                "104 hero sprites imported at 32 PPU with identical fixed (32,3) pixel foot pivot",
                "Point, uncompressed RGBA, no mipmaps, full-rectangle sprite mesh",
                "Front/behind tree sorting rendered and pixel-verified in Unity",
                "Tree canopy and trunk preserve original pixels in one foot-anchored SortingGroup",
                "Game.unity and multiplayer controllers are not required by the study scenes"
            };
            report.scenes = new[] { SceneRoot + "/GamcheonArtStudy.unity", SceneRoot + "/GamcheonScaleComparison.unity", SceneRoot + "/GamcheonOcclusionComparison.unity" };
            report.screenshots = new[] { "Gamcheon_Overview.png", "Gamcheon_ScaleComparison.png", "Gamcheon_OcclusionComparison.png" };
            AssetDatabase.SaveAssets();
            foreach (string scenePath in report.scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                        if (!renderer.sprite) throw new Exception("Saved scene has a missing sprite: " + scenePath + " / " + renderer.name);
                    foreach (var renderer in root.GetComponentsInChildren<LineRenderer>(true))
                        if (!renderer.sharedMaterial) throw new Exception("Saved scene has a missing marker material: " + scenePath);
                    foreach (var tilemap in root.GetComponentsInChildren<Tilemap>(true))
                        if (tilemap.GetUsedTilesCount() == 0) throw new Exception("Saved scene lost its terrain: " + scenePath);
                }
            }
            File.WriteAllText(Output + "/ValidationReport.json", JsonUtility.ToJson(report, true));
            AssetDatabase.SaveAssets();
            Debug.Log("GAMCHEON_ART_STUDY_OK " + Output);
        }
        catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }

    static void Folder(string path)
    {
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    static void Configure(string path, Vector2 pivot, float ppu = Ppu)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048; importer.spritePixelsPerUnit = ppu;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        // Remove inherited platform compression overrides as well.
        foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" }) importer.ClearPlatformTextureSettings(platform);
        importer.SaveAndReimport();
    }

    static Texture2D Raw(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!t.LoadImage(File.ReadAllBytes(path))) throw new Exception("Could not read PNG " + path);
        return t;
    }

    static Measurement Measure(string path)
    {
        var t = Raw(path); Color32[] p = t.GetPixels32();
        int minX = t.width, minY = t.height, maxX = -1, maxY = -1;
        for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++) if (p[y * t.width + x].a > 16)
        { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        var result = new Measurement { file = path, width = t.width, height = t.height, alphaX = minX, alphaY = minY,
            alphaWidth = maxX - minX + 1, alphaHeight = maxY - minY + 1,
            visibleWidthUnits = (maxX - minX + 1) / Ppu, visibleHeightUnits = (maxY - minY + 1) / Ppu };
        UnityEngine.Object.DestroyImmediate(t); return result;
    }

    static void PrepareTiles()
    {
        var source = JsonUtility.FromJson<Source>(File.ReadAllText(TerrainRoot + "/Gamcheon_Terrain_Source.json"));
        var atlas = Raw(TerrainRoot + "/Gamcheon_Terrain.png"); Tiles.Clear();
        foreach (Descriptor d in source.tileset_data.tiles)
        {
            Box b = d.bounding_box;
            var t = new Texture2D(b.width, b.height, TextureFormat.RGBA32, false);
            t.SetPixels(atlas.GetPixels(b.x, atlas.height - b.y - b.height, b.width, b.height)); t.Apply();
            int mask = Mask(d.corners.NW == "upper", d.corners.NE == "upper", d.corners.SW == "upper", d.corners.SE == "upper");
            string png = TerrainRoot + "/Ground/Gamcheon_Wang_" + mask.ToString("D2") + ".png";
            File.WriteAllBytes(png, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            Configure(png, new Vector2(0.5f, 0.5f));
            string asset = TerrainRoot + "/TileAssets/Gamcheon_Wang_" + mask.ToString("D2") + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(asset);
            if (!tile) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, asset); }
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png); tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile); Tiles.Add(mask, tile);
        }
        UnityEngine.Object.DestroyImmediate(atlas);
        Configure(TerrainRoot + "/Gamcheon_Terrain.png", new Vector2(0.5f, 0.5f));
    }

    static int Mask(bool nw, bool ne, bool sw, bool se) => (nw ? 8 : 0) | (ne ? 4 : 0) | (sw ? 2 : 0) | (se ? 1 : 0);
    static void MakeReference(string source, string name, int ppu, Vector2 pivot)
    {
        string target = StudyRoot + "/References/" + name + ".png";
        File.Copy(source, target, true); Configure(target, pivot, ppu);
    }

    public static Camera NewScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Art Study Camera"); var camera = go.AddComponent<Camera>();
        camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Ink;
        camera.transparencySortMode = TransparencySortMode.CustomAxis; camera.transparencySortAxis = Vector3.up;
        camera.nearClipPlane = 0.1f; camera.farClipPlane = 100;
        camera.allowMSAA = false;
        return camera;
    }

    public static Tilemap Ground(string name, int x, int y, int w, int h, bool path = true)
    {
        var grid = new GameObject(name); grid.AddComponent<Grid>();
        var go = new GameObject("Ground"); go.transform.SetParent(grid.transform);
        var tm = go.AddComponent<Tilemap>(); go.AddComponent<TilemapRenderer>().sortingOrder = -100;
        Func<int, int, bool> grass = (vx, vy) => !path || !(Math.Abs(vx - w / 2) <= 1 || Math.Abs(vy - h / 2) <= 1);
        for (int cy = 0; cy < h; cy++) for (int cx = 0; cx < w; cx++)
            tm.SetTile(new Vector3Int(x + cx, y + cy, 0), Tiles[Mask(grass(cx, cy + 1), grass(cx + 1, cy + 1), grass(cx, cy), grass(cx + 1, cy))]);
        return tm;
    }

    public static SpriteRenderer Actor(string name, string sprite, Vector2 foot)
    {
        var go = new GameObject(name); go.transform.position = foot;
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sprite);
        sr.spriteSortPoint = SpriteSortPoint.Pivot; return sr;
    }

    static SpriteRenderer Hero(Vector2 foot, string variant = "Hero_32_Feet") => Actor(variant, StudyRoot + "/References/" + variant + ".png", foot);

    static GameObject Tree(Vector2 foot)
    {
        var root = new GameObject("PineTree_FootSortingGroup"); root.transform.position = foot;
        root.AddComponent<SortingGroup>();
        // Separate render parts share the same base; no original pixels are rescaled or repainted.
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainRoot + "/Props/Gamcheon_PineTree.png");
        TreePart(root, texture, "Trunk", new Rect(0, 0, 96, 64), 0);
        TreePart(root, texture, "Canopy", new Rect(0, 64, 96, 64), 1);
        var collider = root.AddComponent<BoxCollider2D>(); collider.size = new Vector2(0.5f, 0.35f); collider.offset = new Vector2(0, 0.18f);
        return root;
    }

    static void TreePart(GameObject parent, Texture2D texture, string name, Rect rect, int order)
    {
        string asset = StudyRoot + "/" + name + ".asset";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(asset);
        if (!sprite) { sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), Ppu, 0, SpriteMeshType.FullRect); AssetDatabase.CreateAsset(sprite, asset); }
        var part = new GameObject(name); part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = new Vector3((rect.center.x - 46) / Ppu, (rect.center.y - 26) / Ppu, 0);
        var renderer = part.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order;
    }

    public static void Label(string text, Vector2 position, float size = 0.15f, Color? color = null)
    {
        var go = new GameObject("Label_" + text); go.transform.position = new Vector3(position.x, position.y, -1);
        var tm = go.AddComponent<TextMesh>(); tm.text = text; tm.characterSize = size; tm.fontSize = 32; tm.anchor = TextAnchor.UpperCenter;
        tm.color = color ?? Paper; go.GetComponent<MeshRenderer>().sortingOrder = 300;
    }

    static void Marker(Vector2 foot, Color color)
    {
        var line = new GameObject("Foot anchor marker").AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 5; line.startWidth = line.endWidth = 1f / Ppu;
        line.material = new Material(Shader.Find("Sprites/Default")); line.startColor = line.endColor = color; line.sortingOrder = 200;
        line.SetPositions(new[] { (Vector3)(foot + Vector2.left * .15f), (Vector3)(foot + Vector2.right * .15f), (Vector3)foot, (Vector3)(foot + Vector2.down * .15f), (Vector3)(foot + Vector2.up * .15f) });
    }

    public static Texture2D Render(Camera camera, Vector2 center, int width, int height, string filename, bool save = true)
    {
        camera.transform.position = new Vector3(center.x, center.y, -10); camera.orthographicSize = height / (2f * Ppu);
        camera.aspect = (float)width / height;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, antiAliasing = 1 };
        camera.targetTexture = rt; camera.Render(); var old = RenderTexture.active; RenderTexture.active = rt;
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false); result.ReadPixels(new Rect(0, 0, width, height), 0, 0); result.Apply();
        camera.targetTexture = null; RenderTexture.active = old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        if (save)
        {
            // Nearest-neighbour integer preview only; source textures remain native resolution.
            var big = new Texture2D(width * 2, height * 2, TextureFormat.RGBA32, false); var pixels = result.GetPixels32(); var expanded = new Color32[width * height * 4];
            for (int y = 0; y < height * 2; y++) for (int x = 0; x < width * 2; x++) expanded[y * width * 2 + x] = pixels[(y / 2) * width + x / 2];
            big.SetPixels32(expanded); big.Apply(); File.WriteAllBytes(Output + "/" + filename, big.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(big);
        }
        return result;
    }

    static void BuildScaleComparison()
    {
        var camera = NewScene();
        string[] variants = { "Hero_Legacy64_Center", "Hero_32_Center", "Hero_32_Feet" };
        string[] titles = { "A / 64 PPU + CENTER", "B / 32 PPU + CENTER", "C / 32 PPU + FEET" };
        for (int i = 0; i < 3; i++)
        {
            int x = i * 7; Ground("Scale_Panel_" + i, x, 0, 6, 4);
            Vector2 anchor = new Vector2(x + 3, 1.5f); Hero(anchor, variants[i]); Marker(anchor, Color.cyan);
            Label(titles[i], new Vector2(x + 3, 5.4f));
            Label(i == 0 ? "1 source pixel = 0.5 terrain pixel" : "1 source pixel = 1 terrain pixel", new Vector2(x + 3, -.3f), .105f);
            Label(i == 2 ? "Ground contact matches the cyan anchor" : "Cyan marker is the object origin", new Vector2(x + 3, -1), .10f);
        }
        Label("GAMCHEON / SCALE AND FOOT ANCHOR", new Vector2(10, 6.6f), .20f);
        UnityEngine.Object.DestroyImmediate(Render(camera, new Vector2(10, 2.5f), 704, 288, "Gamcheon_ScaleComparison.png"));
        EditorSceneManager.SaveScene(camera.gameObject.scene, SceneRoot + "/GamcheonScaleComparison.unity");
    }

    static void VerifyOcclusion()
    {
        var camera = NewScene();
        for (int i = 0; i < 3; i++) Ground("Occlusion_Ground_" + i, i * 6, 0, 5, 5, false);
        Tree(new Vector2(2.5f, 1)); Hero(new Vector2(2.5f, 1.6f));
        Tree(new Vector2(8.5f, 1.6f)); Hero(new Vector2(8.5f, 1));
        Actor("Rock", TerrainRoot + "/Props/Gamcheon_Rock.png", new Vector2(14.5f, 1.6f)); Hero(new Vector2(14.9f, 1));
        Label("BEHIND TREE", new Vector2(2.5f, 5.5f)); Label("IN FRONT OF TREE", new Vector2(8.5f, 5.5f)); Label("IN FRONT OF ROCK", new Vector2(14.5f, 5.5f));
        Label("Foot-root Y sorting / canopy + trunk share a SortingGroup", new Vector2(8.5f, 6.5f), .17f);
        UnityEngine.Object.DestroyImmediate(Render(camera, new Vector2(8.5f, 3), 608, 256, "Gamcheon_OcclusionComparison.png"));
        EditorSceneManager.SaveScene(camera.gameObject.scene, SceneRoot + "/GamcheonOcclusionComparison.unity");

        // Render a controlled overlap at identical positions with the foreground alternately visible.
        NewScene(); camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        var tree = Tree(Vector2.zero); var hero = Hero(new Vector2(0, .5f));
        var treeOnly = Render(camera, new Vector2(0, 1.5f), 128, 128, "unused", false);
        hero.gameObject.SetActive(false); var baseline = Render(camera, new Vector2(0, 1.5f), 128, 128, "unused", false);
        hero.gameObject.SetActive(true); tree.SetActive(false); var heroOnly = Render(camera, new Vector2(0, 1.5f), 128, 128, "unused", false);
        tree.SetActive(true); hero.transform.position = new Vector3(0, -.5f, 0);
        var inFront = Render(camera, new Vector2(0, 1.5f), 128, 128, "unused", false);
        var back = treeOnly.GetPixels32(); var ground = baseline.GetPixels32(); var alone = heroOnly.GetPixels32(); var front = inFront.GetPixels32();
        Color32 background = ground[0];
        int behindHidden = 0, overlap = 0, frontVisible = 0;
        for (int i = 0; i < back.Length; i++)
        {
            bool treePixel = !Same(ground[i], background), heroPixel = !Same(alone[i], background);
            if (treePixel && heroPixel) { overlap++; if (Same(back[i], ground[i])) behindHidden++; }
            if (treePixel && !Same(front[i], ground[i])) frontVisible++;
        }
        if (overlap < 10 || behindHidden < overlap * .9f || frontVisible < 10) throw new Exception($"Occlusion failed: overlap={overlap}, hidden={behindHidden}, foreground={frontVisible}");
        File.WriteAllText(Output + "/OcclusionCheck.json", $"{{\"overlapPixels\":{overlap},\"behindHiddenPixels\":{behindHidden},\"frontVisiblePixels\":{frontVisible},\"passed\":true}}");
        foreach (var t in new[] { treeOnly, baseline, heroOnly, inFront }) UnityEngine.Object.DestroyImmediate(t);
    }

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    static void BuildOverview()
    {
        var camera = NewScene(); Ground("Gamcheon_Test_Tilemap", 0, 0, 16, 11);
        Tree(new Vector2(3.5f, 7)); Tree(new Vector2(12.5f, 7)); Tree(new Vector2(3.5f, 2.5f));
        Actor("GraniteBoulder", TerrainRoot + "/Props/Gamcheon_Rock.png", new Vector2(11.5f, 2.5f));
        Hero(new Vector2(8, 5));
        // Ensure this reference uses the proposed import standard before rendering the overview.
        Configure(HeroRoot + "/Sprites/Rotations/north.png", HeroPivot);
        Actor("Hero_North", HeroRoot + "/Sprites/Rotations/north.png", new Vector2(5, 5));
        Configure(HeroRoot + "/Sprites/Rotations/east.png", HeroPivot);
        Actor("Hero_East", HeroRoot + "/Sprites/Rotations/east.png", new Vector2(10, 5));
        Label("GAMCHEON / PIXEL SCALE STUDY", new Vector2(8, 12.2f), .22f);
        Label("32px terrain / 64px hero canvas / 32 PPU / foot-root Y sorting", new Vector2(8, -.4f), .125f);
        var treePrefab = Tree(Vector2.zero); PrefabUtility.SaveAsPrefabAsset(treePrefab, TerrainRoot + "/Props/Gamcheon_PineTree.prefab"); UnityEngine.Object.DestroyImmediate(treePrefab);
        var rock = Actor("GraniteBoulder_FootPivot", TerrainRoot + "/Props/Gamcheon_Rock.png", new Vector2(-100, -100));
        var box = rock.gameObject.AddComponent<BoxCollider2D>(); box.size = new Vector2(.9f, .45f); box.offset = new Vector2(0, .23f);
        rock.transform.position = Vector3.zero;
        PrefabUtility.SaveAsPrefabAsset(rock.gameObject, TerrainRoot + "/Props/Gamcheon_Rock.prefab"); UnityEngine.Object.DestroyImmediate(rock.gameObject);
        UnityEngine.Object.DestroyImmediate(Render(camera, new Vector2(8, 6), 576, 480, "Gamcheon_Overview.png"));
        EditorSceneManager.SaveScene(camera.gameObject.scene, SceneRoot + "/GamcheonArtStudy.unity");
    }

    static void ValidateImports(string[] files)
    {
        foreach (string path in files)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (importer.spritePixelsPerUnit != Ppu || importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new Exception("Import settings mismatch: " + path);
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            if (Vector2.Distance(settings.spritePivot, HeroPivot) > .0001f) throw new Exception("Hero anchor mismatch: " + path);
        }
    }

    public static void LoadExistingTiles()
    {
        Tiles.Clear();
        for (int mask = 0; mask < 16; mask++)
        {
            string file = TerrainRoot + "/TileAssets/Gamcheon_Wang_" + mask.ToString("D2") + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(file);
            if (!tile || !tile.sprite) throw new Exception("Missing Gamcheon terrain tile: " + file);
            Tiles.Add(mask, tile);
        }
    }
}
