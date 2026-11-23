using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Table;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

// 원본 오브젝트별 경계를 알고 있으므로 자동 덩어리 검출 없이 32px 묶음을 그대로 보존한다.
public static class SamcheondoTilePalettes
{
    const string Root = "Assets/Resources/Art/Tiles/Samcheondo";
    const string PaletteRoot = "Assets/Editor/Art/Palettes/Samcheondo";

    [Serializable] class Manifest { public int cellSize, pixelsPerUnit; public Sheet[] sheets; }
    [Serializable] class Sheet { public string category, sheet; public int width, height; public Piece[] pieces; }
    [Serializable] class Piece { public string name; public int x, y, width, height, paletteX, paletteY; }
    class PlacedTile { public Tile tile; public Vector3Int position; }
    [Serializable] class SheetReport { public string category, palette; public int tiles, width, height; }
    [Serializable] class Report
    {
        public string status, unityVersion, activeScene;
        public int tileCount, pixelsPerUnit;
        public bool sceneUnchanged;
        public SheetReport[] sheets;
        public string[] checks;
    }

    static Manifest ReadManifest()
    {
        var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Root + "/SourceInfo.json"));
        TableManager.LoadAll(new ResourcesTableReader());
        int cell = Mathf.RoundToInt(TableManager.Configs.Get(90001).value[0]);
        if (manifest.cellSize != cell || manifest.pixelsPerUnit != cell)
            throw new Exception("삼천도 원본 픽셀 규격과 configs 90001이 다릅니다.");
        if (manifest.sheets == null || manifest.sheets.Length != 3)
            throw new Exception("삼천도 Ground/Struct/Deco 시트가 필요합니다.");
        return manifest;
    }

    [MenuItem("Tools/ProjectY/Levels/Samcheondo/Build Tile Palettes")]
    public static void Build()
    {
        var manifest = ReadManifest();
        foreach (var sheet in manifest.sheets)
            BuildSheet(sheet, manifest.cellSize);
        AssetDatabase.SaveAssets();
        Verify();
    }

    static void BuildSheet(Sheet sheet, int cell)
    {
        var importer = AssetImporter.GetAtPath(sheet.sheet) as TextureImporter;
        if (!importer) throw new Exception("삼천도 시트를 먼저 import하세요: " + sheet.sheet);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = cell;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(2048, sheet.width, sheet.height));
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var old = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        var rects = sheet.pieces.Select(p => new SpriteRect
        {
            name = p.name,
            rect = new Rect(p.x, p.y, p.width, p.height),
            alignment = SpriteAlignment.Custom,
            pivot = new Vector2(.5f, .5f),
            spriteID = old.ContainsKey(p.name) ? old[p.name] : GUID.Generate(),
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(
            rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();

        string tileDir = EnsureFolder(Root + "/" + sheet.category + "/Tiles");
        var sprites = AssetDatabase.LoadAllAssetsAtPath(sheet.sheet).OfType<Sprite>().ToDictionary(s => s.name);
        var tiles = new List<PlacedTile>();
        foreach (var piece in sheet.pieces)
        {
            Sprite sprite;
            if (!sprites.TryGetValue(piece.name, out sprite))
                throw new Exception("삼천도 스프라이트 누락: " + piece.name);
            string path = tileDir + "/" + piece.name + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (!tile)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            tiles.Add(new PlacedTile { tile = tile, position = new Vector3Int(piece.paletteX, piece.paletteY, 0) });
        }
        WritePalette(sheet.category, tiles);
        Debug.Log($"[SamcheondoTilePalettes] {sheet.category}: {tiles.Count} tiles");
    }

    static void WritePalette(string category, List<PlacedTile> tiles)
    {
        string name = "Palette_Samcheondo_" + category;
        string path = EnsureFolder(PaletteRoot) + "/" + name + ".prefab";
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name, typeof(Grid));
        try
        {
            var grid = root.GetComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSize = new Vector3(1, 1, 0);
            var tilemap = root.GetComponentInChildren<Tilemap>();
            if (!tilemap)
            {
                var layer = new GameObject("Layer1", typeof(Tilemap), typeof(TilemapRenderer));
                layer.transform.SetParent(root.transform, false);
                tilemap = layer.GetComponent<Tilemap>();
            }
            tilemap.tileAnchor = new Vector3(.5f, .5f, 0);
            tilemap.ClearAllTiles();
            foreach (var placed in tiles) tilemap.SetTile(placed.position, placed.tile);
            tilemap.CompressBounds();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
        if (!AssetDatabase.LoadAllAssetsAtPath(path).OfType<GridPalette>().Any())
        {
            var palette = ScriptableObject.CreateInstance<GridPalette>();
            palette.name = "Palette Settings";
            palette.cellSizing = GridPalette.CellSizing.Automatic;
            AssetDatabase.AddObjectToAsset(palette, path);
        }
    }

    static string EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return path;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        return path;
    }

    [MenuItem("Tools/ProjectY/Levels/Samcheondo/Verify Tile Palettes")]
    public static void Verify()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string scenePath = scene.path;
        bool dirty = scene.isDirty;
        var manifest = ReadManifest();
        var reports = new List<SheetReport>();
        int total = 0;
        foreach (var sheet in manifest.sheets)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(sheet.sheet);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (importer.spriteImportMode != SpriteImportMode.Multiple || importer.spritePixelsPerUnit != manifest.cellSize ||
                importer.filterMode != FilterMode.Point || importer.mipmapEnabled || !importer.sRGBTexture ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.npotScale != TextureImporterNPOTScale.None ||
                importer.wrapMode != TextureWrapMode.Clamp || settings.spriteMeshType != SpriteMeshType.FullRect)
                throw new Exception("삼천도 임포트 설정 오류: " + sheet.sheet);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sheet.sheet);
            if (texture.width != sheet.width || texture.height != sheet.height)
                throw new Exception("삼천도 원본 시트가 리사이즈되었습니다: " + sheet.sheet);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheet.sheet).OfType<Sprite>().ToDictionary(s => s.name);
            string palettePath = PaletteRoot + "/Palette_Samcheondo_" + sheet.category + ".prefab";
            if (!AssetDatabase.LoadAllAssetsAtPath(palettePath).OfType<GridPalette>().Any())
                throw new Exception("Tile Palette 설정 누락: " + palettePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(palettePath);
            var tilemap = prefab.GetComponentInChildren<Tilemap>();
            var positions = new HashSet<Vector3Int>();
            if (prefab.GetComponent<Grid>().cellSize != new Vector3(1, 1, 0))
                throw new Exception("Tile Palette Grid 크기 오류");
            if (sprites.Count != sheet.pieces.Length) throw new Exception("슬라이스 수 불일치: " + sheet.category);
            foreach (var piece in sheet.pieces)
            {
                var sprite = sprites[piece.name];
                if (sprite.rect != new Rect(piece.x, piece.y, manifest.cellSize, manifest.cellSize) ||
                    sprite.pixelsPerUnit != manifest.cellSize || Vector2.Distance(sprite.pivot, Vector2.one * manifest.cellSize / 2) > .001f)
                    throw new Exception("타일 크기·피벗 오류: " + piece.name);
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(Root + "/" + sheet.category + "/Tiles/" + piece.name + ".asset");
                if (!tile || tile.sprite != sprite || tile.colliderType != Tile.ColliderType.None || tile.transform != Matrix4x4.identity)
                    throw new Exception("타일 참조 오류: " + piece.name);
                var position = new Vector3Int(piece.paletteX, piece.paletteY, 0);
                if (!positions.Add(position) || tilemap.GetTile(position) != tile)
                    throw new Exception("팔레트 배치 오류: " + piece.name);
            }
            if (tilemap.GetUsedTilesCount() != sheet.pieces.Length) throw new Exception("팔레트 타일 수 불일치");
            total += sheet.pieces.Length;
            reports.Add(new SheetReport { category = sheet.category, palette = palettePath,
                tiles = sheet.pieces.Length, width = sheet.width, height = sheet.height });
        }
        var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var report = new Report { status = "passed", unityVersion = Application.unityVersion, activeScene = current.path,
            tileCount = total, pixelsPerUnit = manifest.pixelsPerUnit, sceneUnchanged = current.path == scenePath && current.isDirty == dirty,
            sheets = reports.ToArray(), checks = new[] { "Native sheet dimensions; 32px sprites and 32 PPU", "Point / no compression / no mipmaps / sRGB / Full Rect / NPOT None / Clamp",
                "Every tile resolves its sprite; no tile collision", "All grouped palette cells resolve the matching tile", "Three registered GridPalette assets; existing scene preserved" } };
        if (!report.sceneUnchanged) throw new Exception("타일 검사 중 열린 씬이 변경되었습니다.");
        string output = Path.GetFullPath(Application.dataPath + "/../../artifacts/pixellab/SamcheondoTiles/UnityValidationReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonConvert.SerializeObject(report, Formatting.Indented));
        Debug.Log($"SAMCHEONDO_TILES_OK {total} tiles / 3 palettes");
    }
}
