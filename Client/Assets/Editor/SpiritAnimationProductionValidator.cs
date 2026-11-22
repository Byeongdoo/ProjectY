using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Read-only verification of completed batch outputs; never switches user scenes.
public static class SpiritAnimationProductionValidator
{
    const string Root = "Assets/Resources/Art/Characters/SpiritsV2";
    static readonly string[] Keys = { "MumyeongJangsu", "Mangnani", "ChakhoGapsa", "Kkokdusoe", "Uinyeo" };
    [Serializable] class Source { public string key, characterId; public int pixelsPerUnit; public Clip[] clips; }
    [Serializable] class Clip { public string animation; public string[] directions; public int frameCount, framesPerSecond; public bool loop; }
    [Serializable] class Front { public Metrics metrics; }
    [Serializable] class Metrics { public int canvasWidth, canvasHeight; public Vector2 pivotPixels; }
    [Serializable] class ClassReport { public string key; public int animations, clips, frameReferences; }
    [Serializable] class Report
    {
        public string unityVersion, generatedAtUtc, status;
        public int expectedClips = 440, expectedFrames = 3136, availableClips, frameReferences, trailClips, trailFrameReferences;
        public ClassReport[] classes;
        public string[] checks;
    }
    [Serializable] class PlaybackReport
    {
        public string unityVersion, activeScene;
        public int sampledClips, sampledFrames;
        public bool sceneUnchanged, sceneDirtyStateUnchanged;
    }

    [MenuItem("Tools/ProjectY/Verify Spirit Trail Playback")]
    public static void VerifyTrailPlayback()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string pathBefore = scene.path; bool dirtyBefore = scene.isDirty;
        var report = new PlaybackReport { unityVersion = Application.unityVersion };
        foreach (string key in new[] { "MumyeongJangsu", "Mangnani", "Kkokdusoe" })
        {
            string folder = Root + "/" + key;
            var root = PrefabUtility.LoadPrefabContents(folder + "/" + key + "_Front.prefab");
            try
            {
                // Front-study prefabs are static. Mecanim sprite sampling needs an
                // Animator on this isolated clone; the saved prefab stays unchanged.
                var animator = root.GetComponent<Animator>();
                if (!animator) animator = root.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder + "/Animations" }))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip)[0];
                    var frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (!AssetDatabase.GetAssetPath(frames[0].value).Contains("/SpritesWithTrail/")) continue;
                    for (int f = 0; f < frames.Length; f++)
                    {
                        clip.SampleAnimation(root, (f + .1f) / clip.frameRate);
                        if (root.GetComponent<SpriteRenderer>().sprite != frames[f].value) throw new Exception("Playback mismatch: " + clip.name + " frame " + f);
                        report.sampledFrames++;
                    }
                    report.sampledClips++;
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        report.activeScene = scene.path; report.sceneUnchanged = scene.path == pathBefore;
        report.sceneDirtyStateUnchanged = scene.isDirty == dirtyBefore;
        if (report.sampledClips != 80 || report.sampledFrames != 672 || !report.sceneUnchanged || !report.sceneDirtyStateUnchanged) throw new Exception("Incomplete or intrusive trail playback check");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/pixellab/SpiritAnimationQA"));
        Directory.CreateDirectory(output);
        File.WriteAllText(output + "/UnityTrailPlayback.json", JsonUtility.ToJson(report, true));
        Debug.Log("SPIRIT_TRAIL_PLAYBACK_OK 80 clips / 672 frames");
    }

    [MenuItem("Tools/ProjectY/Verify Available Spirit Animations")]
    public static void VerifyAvailable()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var report = new Report { unityVersion = Application.unityVersion, generatedAtUtc = DateTime.UtcNow.ToString("o"), classes = new ClassReport[5] };
            for (int i = 0; i < Keys.Length; i++)
            {
                string folder = Root + "/" + Keys[i];
                var row = new ClassReport { key = Keys[i] }; report.classes[i] = row;
                string file = folder + "/AnimationSource.json";
                if (!File.Exists(file)) continue;
                var source = JsonUtility.FromJson<Source>(File.ReadAllText(file));
                var front = JsonUtility.FromJson<Front>(File.ReadAllText(folder + "/SourceInfo.json"));
                if (source.pixelsPerUnit != 32) throw new Exception("Wrong PPU: " + Keys[i]);
                foreach (var spec in source.clips)
                {
                    if (spec.directions.Length != 8) throw new Exception("Missing directions: " + Keys[i] + "/" + spec.animation);
                    row.animations++;
                    foreach (string direction in spec.directions)
                    {
                        string path = folder + "/Animations/" + spec.animation + "_" + direction.ToUpperInvariant().Replace('-', '_') + ".anim";
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        if (!clip) throw new Exception("Cannot load animation: " + path);
                        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                        if (bindings.Length != 1 || bindings[0].type != typeof(SpriteRenderer) || bindings[0].propertyName != "m_Sprite") throw new Exception("Invalid sprite curve: " + path);
                        if (AnimationUtility.GetCurveBindings(clip).Length != 0) throw new Exception("Unexpected root motion: " + path);
                        var frames = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
                        int trailFrames = 0;
                        if (frames.Length != spec.frameCount) throw new Exception("Wrong frame count: " + path);
                        if (Math.Abs(clip.frameRate - spec.framesPerSecond) > .001f) throw new Exception("Wrong fps: " + path);
                        if (AnimationUtility.GetAnimationClipSettings(clip).loopTime != spec.loop) throw new Exception("Wrong loop mode: " + path);
                        for (int f = 0; f < frames.Length; f++)
                        {
                            var sprite = frames[f].value as Sprite;
                            if (!sprite) throw new Exception("Broken sprite reference: " + path + " frame " + f);
                            if (Math.Abs(frames[f].time - f / (float)spec.framesPerSecond) > .0001f) throw new Exception("Wrong frame time: " + path);
                            if (sprite.pixelsPerUnit != 32) throw new Exception("Wrong sprite PPU: " + path);
                            string spritePath = AssetDatabase.GetAssetPath(sprite);
                            if (spritePath.Contains("/SpritesWithTrail/")) trailFrames++;
                            var importer = (TextureImporter)AssetImporter.GetAtPath(spritePath);
                            if (importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed) throw new Exception("Wrong pixel-art import: " + spritePath);
                            var expected = new Vector2(front.metrics.pivotPixels.x + (sprite.rect.width - front.metrics.canvasWidth) / 2, front.metrics.pivotPixels.y + (sprite.rect.height - front.metrics.canvasHeight) / 2);
                            if (Vector2.Distance(sprite.pivot, expected) > .001f) throw new Exception("Unstable foot origin: " + spritePath);
                        }
                        if (trailFrames > 0)
                        {
                            if (trailFrames != frames.Length) throw new Exception("Mixed trail/plain frames: " + path);
                            report.trailClips++; report.trailFrameReferences += trailFrames;
                            for (int f = 0; f < frames.Length; f++)
                                if (!File.Exists(folder + "/Sprites/" + spec.animation + "/" + direction + "/frame_" + f.ToString("D3") + ".png")) throw new Exception("Original frame not retained: " + path);
                        }
                        row.clips++; row.frameReferences += frames.Length;
                    }
                }
                report.availableClips += row.clips; report.frameReferences += row.frameReferences;
            }
            if (Keys.Any(key => File.Exists(Root + "/" + key + "/TrailSource.json")) && (report.trailClips != 80 || report.trailFrameReferences != 672)) throw new Exception("Incomplete melee trail integration: " + report.trailClips + " clips / " + report.trailFrameReferences + " frames");
            report.status = report.availableClips == 440 ? "All generated assets imported; visual approval remains separate" : "Partial production: available assets verified, remaining clips still queued";
            report.checks = new[] { "Every available clip loads in Unity and resolves every sprite", "Eight directions per completed motion; expected frame count, timestamps, fps and loop settings", "32 PPU, Point, uncompressed, no mipmaps; source canvas centre offset preserves per-character foot origin", "Only sprite curves, no root-motion curves; existing user scenes untouched" };
            var args = Environment.GetCommandLineArgs(); int n = Array.IndexOf(args, "-artStudyOutput");
            string output = n >= 0 && n + 1 < args.Length ? Path.GetFullPath(args[n + 1]) : Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/pixellab/SpiritAnimationProduction"));
            Directory.CreateDirectory(output);
            File.WriteAllText(output + "/UnityValidationReport.json", JsonUtility.ToJson(report, true));
            Debug.Log("SPIRIT_ANIMATION_AVAILABLE_OK " + report.availableClips + " clips / " + report.frameReferences + " frame references");
        }
        catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }
}
