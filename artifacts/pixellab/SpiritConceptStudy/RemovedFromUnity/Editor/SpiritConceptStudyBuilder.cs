using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Front-facing design study only; gameplay, rotations and animations are separate assets.
public static class SpiritConceptStudyBuilder
{
    const string Root = "Assets/Resources/Art/Characters/Spirits";
    const string Scenes = "Assets/Scenes/ArtTests";
    const string Hero = "Assets/Resources/Art/Characters/Protagonist/Sprites/Rotations/south.png";
    static readonly string[] Keys = { "MumyeongJangsu", "Mangnani", "ChakhoGapsa", "Kkokdusoe", "Uinyeo" };
    static readonly string[] Names = { "MUMYEONG JANGSU", "MANGNANI", "CHAKHO GAPSA", "KKOKDUSOE", "UINYEO" };
    static readonly string[] Roles = { "TANK", "MELEE", "RANGED", "BUFFER", "HEALER" };
    static readonly string[] Weapons = { "ROUND SHIELD + SABER", "EXECUTION BLADE", "GAKGUNG + QUIVER", "SMALL GONG + MALLET", "NEEDLE CASE + NEEDLE" };
    static readonly Color Ink = new Color(.07f, .10f, .14f);
    static readonly Color Paper = new Color(.94f, .91f, .80f);
    static readonly Color Muted = new Color(.64f, .69f, .69f);

    static string Output
    {
        get
        {
            var args = Environment.GetCommandLineArgs(); int n = Array.IndexOf(args, "-artStudyOutput");
            return n >= 0 && n + 1 < args.Length ? Path.GetFullPath(args[n + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/pixellab/SpiritConceptStudy"));
        }
    }

    [Serializable] public class SpriteMetrics
    {
        public string key, role, spritePath, weaponPath;
        public int canvasWidth, canvasHeight, alphaX, alphaY, visibleWidth, visibleHeight, pixelsPerUnit = 32;
        public float worldWidth, worldHeight, heightRelativeToHero;
        public Vector2 pivotPixels;
    }
    [Serializable] class Report
    {
        public string unityVersion;
        public int frontCount = 5, weaponConceptCount = 5, pixelsPerUnit = 32;
        public bool imagesResized = false;
        public SpriteMetrics[] classes;
        public string[] scenes, screenshots, checks;
    }

    [MenuItem("Tools/ProjectY/Build Spirit Front Concept Study")]
    public static void BuildAndVerify()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        try
        {
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Scenes);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GamcheonArtStudyBuilder.LoadExistingTiles();
            var metrics = new SpriteMetrics[5];
            for (int i = 0; i < Keys.Length; i++)
            {
                string front = Root + "/" + Keys[i] + "/" + Keys[i] + "_Front.png";
                string weapon = Root + "/" + Keys[i] + "/" + Keys[i] + "_WeaponConcept.png";
                metrics[i] = Measure(Keys[i], front, weapon, Roles[i]);
                if (metrics[i].canvasWidth != 64 || metrics[i].canvasHeight != 64) throw new Exception("Front canvas must be 64px: " + Keys[i]);
                if (metrics[i].visibleHeight < 45 || metrics[i].visibleHeight > 64) throw new Exception("Front height out of study range: " + Keys[i]);
                Configure(front, new Vector2(.5f, metrics[i].alphaY / 64f));
                Configure(weapon, new Vector2(.5f, .5f));
                var actor = GamcheonArtStudyBuilder.Actor(Keys[i] + "_Front", front, Vector2.zero);
                PrefabUtility.SaveAsPrefabAsset(actor.gameObject, Root + "/" + Keys[i] + "/" + Keys[i] + "_Front.prefab");
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
            }
            BuildFrontLineup(metrics);
            BuildSilhouettes(metrics);
            BuildWeaponBoard();
            var report = new Report { unityVersion = Application.unityVersion, classes = metrics,
                scenes = new[] { Scenes + "/SpiritFrontLineup.unity", Scenes + "/SpiritSilhouetteComparison.unity", Scenes + "/SpiritWeaponConcepts.unity" },
                screenshots = new[] { "Spirit_FrontLineup.png", "Spirit_SilhouetteComparison.png", "Spirit_WeaponConcepts.png" },
                checks = new[] { "Five front sprites are native 64x64 RGBA PNGs with transparent corners", "All front sprites and concepts use Point, no mipmaps, uncompressed, 32 PPU", "Foot anchors use a shared x=32 and each unchanged PNG's measured lowest visible row", "Hero and all five classes use transform scale 1 with a common foot baseline", "Source pixels are not resized or repainted in Unity", "Native saved scenes and five prefabs reloaded with all sprite references resolved" } };
            AssetDatabase.SaveAssets();
            foreach (string scenePath in report.scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                        if (!renderer.sprite) throw new Exception("Saved scene lost a sprite: " + scenePath);
            }
            foreach (string key in Keys)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + key + "/" + key + "_Front.prefab");
                if (!prefab || !prefab.GetComponent<SpriteRenderer>().sprite) throw new Exception("Invalid spirit prefab: " + key);
            }
            File.WriteAllText(Output + "/ValidationReport.json", JsonUtility.ToJson(report, true));
            Debug.Log("SPIRIT_CONCEPT_STUDY_OK " + Output);
        }
        catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }

    static SpriteMetrics Measure(string key, string file, string weapon, string role)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(file))) throw new Exception("Invalid PNG: " + file);
        var pixels = texture.GetPixels32();
        if (pixels[0].a > 0 || pixels[texture.width - 1].a > 0 || pixels[pixels.Length - 1].a > 0) throw new Exception("PNG is not transparent: " + key);
        int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
        for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
            if (pixels[y * texture.width + x].a > 16) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        var result = new SpriteMetrics { key = key, role = role, spritePath = file, weaponPath = weapon,
            canvasWidth = texture.width, canvasHeight = texture.height, alphaX = minX, alphaY = minY,
            visibleWidth = maxX - minX + 1, visibleHeight = maxY - minY + 1, worldWidth = (maxX - minX + 1) / 32f,
            worldHeight = (maxY - minY + 1) / 32f, heightRelativeToHero = (maxY - minY + 1) / 59f, pivotPixels = new Vector2(32, minY) };
        UnityEngine.Object.DestroyImmediate(texture); return result;
    }

    static void Configure(string file, Vector2 pivot)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(file);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32; importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true; importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" }) importer.ClearPlatformTextureSettings(platform);
        importer.SaveAndReimport();
        if (importer.spritePixelsPerUnit != 32 || importer.filterMode != FilterMode.Point) throw new Exception("Import failed: " + file);
    }

    static void Save(Camera camera, string file, string scene, Vector2 center, int width, int height)
    {
        UnityEngine.Object.DestroyImmediate(GamcheonArtStudyBuilder.Render(camera, center, width, height, file));
        EditorSceneManager.SaveScene(camera.gameObject.scene, Scenes + "/" + scene);
    }

    static void BuildFrontLineup(SpriteMetrics[] metrics)
    {
        var camera = GamcheonArtStudyBuilder.NewScene();
        GamcheonArtStudyBuilder.Ground("Gamcheon_Lineup_Ground", 0, 0, 24, 5);
        var hero = GamcheonArtStudyBuilder.Actor("Existing_Hero_Reference", Hero, new Vector2(2, 1.5f));
        Label("HERO", new Vector2(2, 5.4f)); Label("STYLE / HEIGHT REFERENCE", new Vector2(2, -.3f), .09f);
        for (int i = 0; i < Keys.Length; i++)
        {
            float x = 6 + 4 * i;
            GamcheonArtStudyBuilder.Actor(Keys[i], metrics[i].spritePath, new Vector2(x, 1.5f));
            Label(Names[i], new Vector2(x, 5.4f), .12f);
            Label(Roles[i], new Vector2(x, 4.8f), .115f);
            Label(Weapons[i], new Vector2(x, -.3f), .09f);
            Label(metrics[i].visibleWidth + " x " + metrics[i].visibleHeight + " visible px", new Vector2(x, -.8f), .09f);
        }
        Label("PROJECT Y / FIVE SPIRIT FRONT CONCEPTS", new Vector2(12, 6.8f), .21f);
        Save(camera, "Spirit_FrontLineup.png", "SpiritFrontLineup.unity", new Vector2(12, 3), 832, 288);
    }

    static void BuildSilhouettes(SpriteMetrics[] metrics)
    {
        var camera = GamcheonArtStudyBuilder.NewScene(); camera.backgroundColor = Paper;
        GamcheonArtStudyBuilder.Actor("Hero_Silhouette", Hero, new Vector2(2, 0)).color = Color.black;
        Label("HERO", new Vector2(2, -.5f), .13f, Ink);
        for (int i = 0; i < Keys.Length; i++)
        {
            float x = 6 + 4 * i;
            GamcheonArtStudyBuilder.Actor(Keys[i] + "_Silhouette", metrics[i].spritePath, new Vector2(x, 0)).color = Color.black;
            Label(Names[i], new Vector2(x, -.5f), .12f, Ink);
            Label(metrics[i].worldWidth.ToString("F2") + " x " + metrics[i].worldHeight.ToString("F2") + " grid units", new Vector2(x, -1.1f), .105f, Ink);
        }
        Label("SILHOUETTE / SAME 32 PPU / SAME FOOT BASELINE", new Vector2(12, 4.5f), .19f, Ink);
        Label("Source pixel sizes preserved. Equipment forms count toward the visible bounds.", new Vector2(12, 3.8f), .12f, Ink);
        Save(camera, "Spirit_SilhouetteComparison.png", "SpiritSilhouetteComparison.unity", new Vector2(12, 1.5f), 832, 224);
    }

    static void BuildWeaponBoard()
    {
        var camera = GamcheonArtStudyBuilder.NewScene(); camera.backgroundColor = Paper;
        for (int i = 0; i < Keys.Length; i++)
        {
            float x = 2 + 4 * i;
            GamcheonArtStudyBuilder.Actor(Keys[i] + "_Equipment_Concept", Root + "/" + Keys[i] + "/" + Keys[i] + "_WeaponConcept.png", new Vector2(x, 2.2f));
            Label(Names[i], new Vector2(x, 4.6f), .125f, Ink);
            Label(Weapons[i], new Vector2(x, -.1f), .095f, Ink);
        }
        Label("EQUIPMENT DESIGN STUDIES / FRONT - BACK - SIDE", new Vector2(10, 5.8f), .20f, Ink);
        Label("Concept plates; final held-equipment scale is shown on the 64px character fronts.", new Vector2(10, -.8f), .12f, Ink);
        Save(camera, "Spirit_WeaponConcepts.png", "SpiritWeaponConcepts.unity", new Vector2(10, 2.5f), 704, 256);
    }

    static void Label(string text, Vector2 position, float size = .15f, Color? color = null)
        => GamcheonArtStudyBuilder.Label(text, position, size, color ?? Paper);
}
