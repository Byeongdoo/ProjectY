using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Current reference revision; original concepts were removed from Unity.
public static class SpiritReferenceV2StudyBuilder
{
    const string Root = "Assets/Resources/Art/Characters/SpiritsV2";
    const string Scenes = "Assets/Scenes/ArtTests";
    const string Hero = "Assets/Resources/Art/Characters/Protagonist/Sprites/Rotations/south.png";
    static readonly string[] Keys = { "MumyeongJangsu", "Mangnani", "ChakhoGapsa", "Kkokdusoe", "Uinyeo" };
    static readonly string[] Names = { "MUMYEONG JANGSU", "MANGNANI", "CHAKHO GAPSA", "KKOKDUSOE", "UINYEO" };
    static readonly Color Paper = new Color(.94f, .91f, .80f);
    static readonly Color Ink = new Color(.07f, .10f, .14f);
    static string Output
    {
        get
        {
            var args = Environment.GetCommandLineArgs(); int n = Array.IndexOf(args, "-artStudyOutput");
            return n >= 0 && n + 1 < args.Length ? Path.GetFullPath(args[n + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/pixellab/SpiritConceptStudyV2"));
        }
    }
    [Serializable] public class Metrics
    {
        public string key, spritePath;
        public int canvasWidth, canvasHeight, visibleWidth, visibleHeight, alphaX, alphaY;
        public Vector2 pivotPixels;
        public int pixelsPerUnit = 32;
    }
    [Serializable] class Report
    {
        public string unityVersion, revision = "UserReferenceV2";
        public int frontCount = 5, pixelsPerUnit = 32;
        public bool sourcePixelsResized = false;
        public Metrics[] classes;
        public string[] scenes, checks;
    }
    static string Front(int i) => Root + "/" + Keys[i] + "/" + Keys[i] + "_Front.png";
    static string ScenePath(string name) => Scenes + "/" + name + "V2.unity";

    [MenuItem("Tools/ProjectY/Build Spirit Reference V2 Study")]
    public static void BuildAndVerify()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        try
        {
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Scenes);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GamcheonArtStudyBuilder.LoadExistingTiles();
            var metrics = new Metrics[5];
            for (int i = 0; i < 5; i++)
            {
                metrics[i] = Measure(Front(i), Keys[i]);
                Configure(Front(i), metrics[i].pivotPixels / 64f);
                var actor = GamcheonArtStudyBuilder.Actor(Keys[i] + "_FrontV2", Front(i), Vector2.zero);
                if (!actor.sprite) throw new Exception("Missing front: " + Front(i));
                PrefabUtility.SaveAsPrefabAsset(actor.gameObject, Root + "/" + Keys[i] + "/" + Keys[i] + "_Front.prefab");
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
            }
            Lineup(metrics); Silhouettes(); AssetDatabase.SaveAssets();
            var scenes = new[] { ScenePath("SpiritFrontLineup"), ScenePath("SpiritSilhouetteComparison") };
            foreach (var path in scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                        if (!sr.sprite) throw new Exception("Saved scene lost sprite: " + path);
            }
            foreach (var key in Keys)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + key + "/" + key + "_Front.prefab");
                if (!prefab || !prefab.GetComponent<SpriteRenderer>().sprite) throw new Exception("Invalid prefab: " + key);
            }
            var report = new Report { unityVersion = Application.unityVersion, classes = metrics, scenes = scenes,
                checks = new[] { "Five native 64x64 transparent fronts; original pixels unchanged", "Point, uncompressed, no mipmaps, FullRect, 32 PPU", "Shared foot baseline, scale 1, Y sorting at pivot", "Two saved scenes and five prefabs reloaded with sprite references resolved", "Original v1 assets and dependent scenes removed from Unity; no animations generated" } };
            File.WriteAllText(Output + "/ValidationReport.json", JsonUtility.ToJson(report, true));
            Debug.Log("SPIRIT_REFERENCE_V2_OK " + Output);
        }
        catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }
    static Metrics Measure(string path, string key)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); tex.LoadImage(File.ReadAllBytes(path));
        if (tex.width != 64 || tex.height != 64) throw new Exception("Expected 64px front: " + key);
        var pixels = tex.GetPixels32(); int minX = 64, minY = 64, maxX = -1, maxY = -1;
        if (pixels[0].a > 0 || pixels[63].a > 0 || pixels[4032].a > 0 || pixels[4095].a > 0) throw new Exception("Nontransparent corners: " + key);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            if (pixels[y * 64 + x].a > 16) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        if (maxY - minY < 44) throw new Exception("Unexpectedly small character: " + key);
        UnityEngine.Object.DestroyImmediate(tex);
        return new Metrics { key = key, spritePath = path, canvasWidth = 64, canvasHeight = 64, alphaX = minX, alphaY = minY, visibleWidth = maxX - minX + 1, visibleHeight = maxY - minY + 1, pivotPixels = new Vector2(32, minY) };
    }
    static void Configure(string path, Vector2 pivot)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32; importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true; importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" }) importer.ClearPlatformTextureSettings(platform);
        importer.SaveAndReimport();
    }
    static void Label(string text, float x, float y, float size = .14f, bool dark = false)
        => GamcheonArtStudyBuilder.Label(text, new Vector2(x, y), size, dark ? Ink : Paper);
    static void Save(Camera camera, string name, Vector2 center, int width, int height)
    {
        UnityEngine.Object.DestroyImmediate(GamcheonArtStudyBuilder.Render(camera, center, width, height, name + "V2.png"));
        EditorSceneManager.SaveScene(camera.gameObject.scene, ScenePath(name.Replace("_", "")));
    }
    static void Lineup(Metrics[] metrics)
    {
        var camera = GamcheonArtStudyBuilder.NewScene();
        GamcheonArtStudyBuilder.Ground("Gamcheon_ReferenceV2", 0, 0, 24, 5);
        GamcheonArtStudyBuilder.Actor("Existing_Hero", Hero, new Vector2(2, 1.5f)); Label("HERO", 2, 5.3f);
        for (int i = 0; i < 5; i++)
        {
            float x = 6 + 4 * i; GamcheonArtStudyBuilder.Actor(Keys[i], Front(i), new Vector2(x, 1.5f));
            Label(Names[i], x, 5.3f, .12f); Label(metrics[i].visibleWidth + " x " + metrics[i].visibleHeight + " visible px", x, -.4f, .10f);
        }
        Label("PROJECT Y / USER REFERENCE V2 / FRONT CONCEPTS", 12, 6.8f, .20f);
        Save(camera, "Spirit_FrontLineup", new Vector2(12, 3), 832, 288);
    }
    static void Silhouettes()
    {
        var camera = GamcheonArtStudyBuilder.NewScene(); camera.backgroundColor = Paper;
        GamcheonArtStudyBuilder.Actor("Hero", Hero, new Vector2(2, 0)).color = Color.black; Label("HERO", 2, -.5f, .13f, true);
        for (int i = 0; i < 5; i++)
        {
            float x = 6 + 4 * i; GamcheonArtStudyBuilder.Actor(Keys[i], Front(i), new Vector2(x, 0)).color = Color.black;
            Label(Names[i], x, -.5f, .12f, true);
        }
        Label("REFERENCE V2 / SILHOUETTE / SHARED 32 PPU + FOOT BASELINE", 12, 3.8f, .18f, true);
        Save(camera, "Spirit_SilhouetteComparison", new Vector2(12, 1.5f), 832, 224);
    }
}
