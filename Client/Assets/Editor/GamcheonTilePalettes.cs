using System.Collections.Generic;
using System.IO;
using System.Linq;
using Table;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

// 감천 타일 시트(Ground/Deco/Struct)를 한 칸(32px) 단위 타일로 잘라 시트마다 Tile Palette 하나에 등록한다.
// 큰 그림은 여러 칸으로 나뉘고, 팔레트에서 그 칸 묶음을 드래그로 골라 한 번에 칠한다.
// 다시 실행해도 같은 이름의 스프라이트·타일·팔레트는 GUID를 유지한 채 갱신한다(이미 칠한 맵의 참조가 살아 있다).
public static class GamcheonTilePalettes
{
    const string SheetRoot = "Assets/Resources/Art/Tiles/Iseung/Gamcheon";
    const string PaletteRoot = "Assets/Art/Palettes/Gamcheon";
    // 떨어진 조각이 이 거리(px) 안이면 한 그림으로 묶는다(풀잎·자갈처럼 조각난 소품용).
    const int MergeGapPx = 6;
    // 어느 그림에도 붙지 않은 이 크기 이하의 점은 버린다.
    const int NoisePx = 2;
    // 팔레트에서 그림 묶음을 한 줄에 이 칸 수까지 늘어놓고 다음 줄로 내린다.
    const int PaletteRowCells = 16;

    enum Slice { Grid, Objects }

    static readonly (string folder, string sheet, Slice slice)[] Sheets =
    {
        ("Ground", "Tile_Gamcheon_Ground", Slice.Grid),
        ("Deco", "Tile_Gamcheon_Deco", Slice.Objects),
        ("Struct", "Tile_Gamcheon_Struct", Slice.Objects),
    };

    // 스프라이트 한 장 = 타일 한 개 = 팔레트 한 칸.
    struct Piece
    {
        public string name;
        public Rect rect;
        public Vector2 pivot;
        public Vector3Int pos;
    }

    [MenuItem("Tools/ProjectY/Levels/Gamcheon/Build Tile Palettes")]
    static void BuildAll()
    {
        TableManager.LoadAll(new ResourcesTableReader());
        int cell = Mathf.RoundToInt(TableManager.Configs.Get(90001).value[0]); // 타일 px = PPU
        foreach (var s in Sheets)
            Build(s.folder, s.sheet, s.slice, cell);
        AssetDatabase.SaveAssets();
    }

    static void Build(string folder, string sheet, Slice slice, int cell)
    {
        string texPath = $"{SheetRoot}/{folder}/{sheet}.png";
        if (AssetImporter.GetAtPath(texPath) is not TextureImporter importer)
        {
            Debug.LogError($"[GamcheonTilePalettes] {texPath}를 찾지 못했습니다. Unity가 import를 마쳤는지 확인하세요.");
            return;
        }

        // 1) 도트 규격으로 import 설정(32 PPU, Point, 무압축).
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = cell;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();

        // 2) 칸 단위로 자르기.
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(texPath));
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        Object.DestroyImmediate(tex);

        var pieces = slice == Slice.Grid ? GridPieces(sheet, px, w, h, cell) : ObjectPieces(sheet, px, w, h, cell);

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
        dp.InitSpriteEditorDataProvider();
        var old = dp.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);

        var spriteRects = pieces.Select(p => new SpriteRect
        {
            name = p.name,
            rect = p.rect,
            alignment = SpriteAlignment.Custom,
            pivot = p.pivot,
            spriteID = old.TryGetValue(p.name, out var id) ? id : GUID.Generate(),
        }).ToArray();
        dp.SetSpriteRects(spriteRects);
        var names = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
        names?.SetNameFileIdPairs(spriteRects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        dp.Apply();
        importer.SaveAndReimport();

        // 3) 타일 에셋(스프라이트당 1개). 충돌은 Collision 레이어에 따로 칠하므로 타일 자체 충돌은 없다.
        string tileDir = EnsureFolder($"{SheetRoot}/{folder}/Tiles");
        var sprites = AssetDatabase.LoadAllAssetsAtPath(texPath).OfType<Sprite>().ToDictionary(sp => sp.name);
        var tiles = new List<(Tile tile, Vector3Int pos)>();
        foreach (var p in pieces)
        {
            if (!sprites.TryGetValue(p.name, out var sp)) continue;
            string tilePath = $"{tileDir}/{p.name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (!tile)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = sp;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            tiles.Add((tile, p.pos));
        }
        WarnStaleTiles(tileDir, pieces.Select(p => p.name));

        // 4) 팔레트.
        string palettePath = $"{EnsureFolder(PaletteRoot)}/Palette_Gamcheon_{folder}.prefab";
        WritePalette(palettePath, $"Palette_Gamcheon_{folder}", tiles);
        Debug.Log($"[GamcheonTilePalettes] {sheet}: 타일 {tiles.Count}개 → {palettePath}");
    }

    // 바닥 시트: 시트 격자 그대로 자르고 빈 칸은 건너뛴다. 팔레트에도 시트 배치 그대로 놓는다.
    static List<Piece> GridPieces(string sheet, Color32[] px, int w, int h, int cell)
    {
        var list = new List<Piece>();
        for (int y = 0; y + cell <= h; y += cell)
            for (int x = 0; x + cell <= w; x += cell)
                if (AnyOpaque(px, w, x, y, cell, cell))
                    list.Add(new Piece
                    {
                        name = $"{sheet}_{x / cell}_{y / cell}",
                        rect = new Rect(x, y, cell, cell),
                        pivot = new Vector2(0.5f, 0.5f),
                        pos = new Vector3Int(x / cell, y / cell, 0),
                    });
        return list;
    }

    // 오브젝트 시트: 그림 하나를 찾아 그 그림만 덮는 칸 묶음으로 자른다.
    // 가로는 묶음 가운데에 오고, 세로는 그림 바닥이 묶음 바닥 칸에 닿는다(한 칸짜리 소품은 칸 가운데).
    // 칸이 그림 밖으로 넘치는 부분은 잘라 내고 pivot으로 칸 자리를 맞춰서, 옆 그림 픽셀이 섞이지 않는다.
    static List<Piece> ObjectPieces(string sheet, Color32[] px, int w, int h, int cell)
    {
        var boxes = ObjectBoxes(px, w, h).OrderByDescending(b => b.yMax).ThenBy(b => b.x).ToList();
        var list = new List<Piece>();
        int curX = 0, rowTop = 0, rowH = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            int cw = Mathf.CeilToInt(box.width / (float)cell), ch = Mathf.CeilToInt(box.height / (float)cell);
            int offX = (cw * cell - box.width) / 2;
            int offY = ch == 1 ? (cell - box.height) / 2 : 0;

            // 팔레트 배치: 시트 순서대로 왼→오른쪽, 한 줄이 차면 아래 줄. 묶음 사이 한 칸을 띄운다.
            if (curX > 0 && curX + cw > PaletteRowCells)
            {
                rowTop -= rowH + 1;
                curX = 0; rowH = 0;
            }
            var origin = new Vector3Int(curX, rowTop - ch, 0);
            curX += cw + 1;
            rowH = Mathf.Max(rowH, ch);

            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                {
                    int x0 = box.x - offX + cx * cell, y0 = box.y - offY + cy * cell;
                    int rx = Mathf.Max(x0, box.xMin), ry = Mathf.Max(y0, box.yMin);
                    int rw = Mathf.Min(x0 + cell, box.xMax) - rx, rh = Mathf.Min(y0 + cell, box.yMax) - ry;
                    if (rw <= 0 || rh <= 0 || !AnyOpaque(px, w, rx, ry, rw, rh)) continue;
                    list.Add(new Piece
                    {
                        name = $"{sheet}_{i}_{cx}_{cy}",
                        rect = new Rect(rx, ry, rw, rh),
                        // 칸 중심(타일 앵커)이 잘린 사각형 안에서 어디인지.
                        pivot = new Vector2((x0 + cell / 2f - rx) / rw, (y0 + cell / 2f - ry) / rh),
                        pos = origin + new Vector3Int(cx, cy, 0),
                    });
                }
        }
        return list;
    }

    static bool AnyOpaque(Color32[] px, int w, int x0, int y0, int cw, int ch)
    {
        for (int y = y0; y < y0 + ch; y++)
            for (int x = x0; x < x0 + cw; x++)
                if (px[y * w + x].a > 0) return true;
        return false;
    }

    // 불투명 픽셀 덩어리(8방향 연결)를 찾고, MergeGapPx 안으로 붙은 덩어리를 합친다.
    static List<RectInt> ObjectBoxes(Color32[] px, int w, int h)
    {
        var seen = new bool[px.Length];
        var boxes = new List<RectInt>();
        var stack = new Stack<int>();
        for (int i = 0; i < px.Length; i++)
        {
            if (seen[i] || px[i].a == 0) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            seen[i] = true; stack.Push(i);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % w, y = p / w;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (seen[n] || px[n].a == 0) continue;
                        seen[n] = true; stack.Push(n);
                    }
            }
            boxes.Add(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
        }

        for (bool merged = true; merged;)
        {
            merged = false;
            for (int a = 0; a < boxes.Count && !merged; a++)
                for (int b = a + 1; b < boxes.Count; b++)
                {
                    if (!Near(boxes[a], boxes[b])) continue;
                    var A = boxes[a]; var B = boxes[b];
                    int x0 = Mathf.Min(A.xMin, B.xMin), y0 = Mathf.Min(A.yMin, B.yMin);
                    boxes[a] = new RectInt(x0, y0, Mathf.Max(A.xMax, B.xMax) - x0, Mathf.Max(A.yMax, B.yMax) - y0);
                    boxes.RemoveAt(b);
                    merged = true;
                    break;
                }
        }
        return boxes.Where(b => b.width > NoisePx || b.height > NoisePx).ToList();
    }

    static bool Near(RectInt a, RectInt b) =>
        a.xMin - MergeGapPx < b.xMax && b.xMin - MergeGapPx < a.xMax &&
        a.yMin - MergeGapPx < b.yMax && b.yMin - MergeGapPx < a.yMax;

    static void WritePalette(string path, string name, List<(Tile tile, Vector3Int pos)> tiles)
    {
        bool isContents = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject root = isContents ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name, typeof(Grid));

        var grid = root.GetComponent<Grid>();
        grid.cellLayout = GridLayout.CellLayout.Rectangle;
        grid.cellSize = new Vector3(1, 1, 0);

        Tilemap tm = root.GetComponentInChildren<Tilemap>();
        if (!tm)
        {
            var layer = new GameObject("Layer1", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(root.transform, false);
            tm = layer.GetComponent<Tilemap>();
        }
        tm.ClearAllTiles();
        foreach (var (tile, pos) in tiles) tm.SetTile(pos, tile);
        tm.CompressBounds();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        if (isContents) PrefabUtility.UnloadPrefabContents(root);
        else Object.DestroyImmediate(root);

        if (!AssetDatabase.LoadAllAssetsAtPath(path).OfType<GridPalette>().Any())
        {
            var palette = ScriptableObject.CreateInstance<GridPalette>();
            palette.name = "Palette Settings";
            palette.cellSizing = GridPalette.CellSizing.Automatic;
            AssetDatabase.AddObjectToAsset(palette, path);
            AssetDatabase.SaveAssets();
        }
    }

    // 시트를 다시 그려 조각이 줄면 남는 타일이 생긴다. 칠한 맵이 쓰고 있을 수 있어 지우지 않고 알린다.
    static void WarnStaleTiles(string tileDir, IEnumerable<string> current)
    {
        var keep = new HashSet<string>(current);
        foreach (var guid in AssetDatabase.FindAssets("t:Tile", new[] { tileDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!keep.Contains(Path.GetFileNameWithoutExtension(p)))
                Debug.LogWarning($"[GamcheonTilePalettes] 시트에 없는 타일이 남아 있습니다: {p} (칠한 곳이 없으면 지워도 됩니다)");
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
}
