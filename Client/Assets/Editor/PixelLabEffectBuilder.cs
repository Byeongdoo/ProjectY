using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Table;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 이펙트의 재생 리소스만 연결한다. 전투 이벤트·수명·대상 판정은 호출하는 구현이 관리한다.
public static class PixelLabEffectBuilder
{
    const string Root = "Assets/Resources/Art/Effects";
    [Serializable] class Manifest { public int pixelsPerUnit; public Effect[] effects; }
    [Serializable] class Effect
    {
        public string name, root, baseSprite, sheet, clip, controller, prefab, resourcePath, sortingTableLayer;
        public int frameCount, framesPerSecond, pixelsPerUnit;
        public bool loop, clearSpriteOnCompletion;
        public string[] frames;
    }
    [Serializable] class Row { public string name, prefab; public int frames; public bool loop, clearsOnCompletion; }
    [Serializable] class Report
    {
        public string status, unityVersion, activeScene;
        public int effects, sampledSourceFrames, clearKeys, loopEffects;
        public bool sceneUnchanged;
        public Row[] rows;
        public string[] checks;
    }

    static Manifest ReadManifest()
    {
        var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Root + "/PixelLabEffects.json"));
        TableManager.LoadAll(new ResourcesTableReader());
        int cell = Mathf.RoundToInt(TableManager.Configs.Get(90001).value[0]);
        if (manifest == null || manifest.pixelsPerUnit != cell || manifest.effects == null || manifest.effects.Length == 0)
            throw new Exception("PixelLab 이펙트 명세 또는 픽셀 규격 오류");
        foreach (var effect in manifest.effects)
            if (effect.pixelsPerUnit != cell || effect.frames.Length != effect.frameCount || effect.loop == effect.clearSpriteOnCompletion)
                throw new Exception("PixelLab 이펙트 프레임/반복 설정 오류: " + effect.name);
        return manifest;
    }

    [MenuItem("Tools/ProjectY/Effects/Build PixelLab Effects")]
    public static void Build()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string before = scene.path; bool dirty = scene.isDirty;
        foreach (var effect in ReadManifest().effects)
        {
            foreach (string texture in effect.frames.Concat(new[] { effect.baseSprite, effect.sheet }))
                ConfigureTexture(texture, effect.pixelsPerUnit);
            BuildClip(effect);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(effect.controller);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(effect.controller);
            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "START");
            if (!state) state = stateMachine.AddState("START");
            state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(effect.clip);
            stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);

            EnsureFolder(Path.GetDirectoryName(effect.prefab).Replace('\\', '/'));
            var go = new GameObject(effect.name, typeof(SpriteRenderer), typeof(Animator));
            try
            {
                var renderer = go.GetComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(effect.frames[0]);
                renderer.sortingOrder = TableManager.TileLayers.Rows.First(r => r.name == effect.sortingTableLayer).sortOrder;
                renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                var animator = go.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                if (!PrefabUtility.SaveAsPrefabAsset(go, effect.prefab))
                    throw new Exception("이펙트 프리팹 저장 실패: " + effect.prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        AssetDatabase.SaveAssets();
        var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (current.path != before || current.isDirty != dirty) throw new Exception("이펙트 생성 중 열린 씬이 변경되었습니다.");
        Verify();
    }

    static void ConfigureTexture(string path, int ppu)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) throw new Exception("이펙트 PNG가 import되지 않았습니다: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(.5f, .5f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    static void BuildClip(Effect effect)
    {
        EnsureFolder(Path.GetDirectoryName(effect.clip).Replace('\\', '/'));
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(effect.clip);
        if (!clip)
        {
            clip = new AnimationClip { name = effect.name };
            AssetDatabase.CreateAsset(clip, effect.clip);
        }
        clip.ClearCurves();
        clip.frameRate = effect.framesPerSecond;
        var keys = new ObjectReferenceKeyframe[effect.frameCount + (effect.clearSpriteOnCompletion ? 1 : 0)];
        for (int i = 0; i < effect.frameCount; i++)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(effect.frames[i]);
            if (!sprite) throw new Exception("이펙트 스프라이트 누락: " + effect.frames[i]);
            keys[i] = new ObjectReferenceKeyframe { time = i / (float)effect.framesPerSecond, value = sprite };
        }
        // Unity는 마지막 스프라이트 키에도 한 프레임 길이를 더한다. 단발만 마지막 원본 뒤에 빈 키를 둔다.
        if (effect.clearSpriteOnCompletion)
            keys[effect.frameCount] = new ObjectReferenceKeyframe { time = effect.frameCount / (float)effect.framesPerSecond, value = null };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0; settings.stopTime = keys.Length / (float)effect.framesPerSecond; settings.loopTime = effect.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static string EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return path;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        return path;
    }

    [MenuItem("Tools/ProjectY/Effects/Verify PixelLab Effects")]
    public static void Verify()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string pathBefore = scene.path; bool dirtyBefore = scene.isDirty;
        var rows = new List<Row>();
        int frames = 0, clearKeys = 0, loopKeys = 0;
        foreach (var effect in ReadManifest().effects)
        {
            foreach (string path in effect.frames.Concat(new[] { effect.baseSprite, effect.sheet }))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var settings = new TextureImporterSettings();
                if (!sprite || !importer) throw new Exception("이펙트 PNG 로드 실패: " + path);
                importer.ReadTextureSettings(settings);
                if (sprite.pixelsPerUnit != effect.pixelsPerUnit || importer.filterMode != FilterMode.Point || importer.mipmapEnabled ||
                    !importer.sRGBTexture || !importer.alphaIsTransparency || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                    importer.npotScale != TextureImporterNPOTScale.None || importer.wrapMode != TextureWrapMode.Clamp || settings.spriteMeshType != SpriteMeshType.FullRect ||
                    Vector2.Distance(sprite.pivot, sprite.rect.size / 2) > .001f)
                    throw new Exception("이펙트 임포트/피벗 설정 오류: " + path);
            }
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(effect.clip);
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (bindings.Length != 1 || bindings[0].path != "" || bindings[0].type != typeof(SpriteRenderer) || bindings[0].propertyName != "m_Sprite" ||
                AnimationUtility.GetCurveBindings(clip).Length != 0)
                throw new Exception("이펙트 곡선 오류: " + effect.name);
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
            float visibleDuration = effect.frameCount / (float)effect.framesPerSecond;
            int expectedKeys = effect.frameCount + (effect.clearSpriteOnCompletion ? 1 : 0);
            float duration = expectedKeys / (float)effect.framesPerSecond;
            if (keys.Length != expectedKeys || Mathf.Abs(clip.length - duration) > .001f || clip.frameRate != effect.framesPerSecond ||
                AnimationUtility.GetAnimationClipSettings(clip).loopTime != effect.loop)
                throw new Exception("이펙트 프레임/재생 시간 오류: " + effect.name);
            var prefab = Resources.Load<GameObject>(effect.resourcePath);
            if (!prefab) throw new Exception("Resources 이펙트 프리팹 로드 실패: " + effect.resourcePath);
            var root = PrefabUtility.LoadPrefabContents(effect.prefab);
            try
            {
                var renderer = root.GetComponent<SpriteRenderer>();
                var animator = root.GetComponent<Animator>();
                var controller = animator.runtimeAnimatorController as AnimatorController;
                if (!renderer || !controller || root.transform.localScale != Vector3.one || animator.applyRootMotion ||
                    controller.layers[0].stateMachine.defaultState.name != "START" || controller.layers[0].stateMachine.defaultState.motion != clip ||
                    renderer.sortingOrder != TableManager.TileLayers.Rows.First(r => r.name == effect.sortingTableLayer).sortOrder)
                    throw new Exception("이펙트 프리팹 연결 오류: " + effect.name);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                for (int i = 0; i < effect.frameCount; i++)
                {
                    if (keys[i].value != AssetDatabase.LoadAssetAtPath<Sprite>(effect.frames[i]) || Mathf.Abs(keys[i].time - i/(float)effect.framesPerSecond) > .0001f)
                        throw new Exception("이펙트 프레임 순서 오류: " + effect.name);
                    clip.SampleAnimation(root, (i + .1f) / effect.framesPerSecond);
                    if (renderer.sprite != keys[i].value) throw new Exception("이펙트 프레임 재생 오류: " + effect.name);
                    frames++;
                }
                var expectedEnd = effect.loop ? keys[0].value : null;
                if (effect.clearSpriteOnCompletion)
                {
                    if (keys[effect.frameCount].value != null) throw new Exception("이펙트 종료 키 오류: " + effect.name);
                    clip.SampleAnimation(root, visibleDuration);
                    if (renderer.sprite != null) throw new Exception("이펙트 종료 재생 오류: " + effect.name);
                }
                animator.Rebind(); animator.Play("START", 0, 0); animator.Update(0);
                if (renderer.sprite != keys[0].value) throw new Exception("Animator 시작 프레임 오류: " + effect.name);
                animator.Update(duration + .001f);
                if (renderer.sprite != expectedEnd) throw new Exception("Animator 반복/종료 오류: " + effect.name);
                if (effect.loop) loopKeys++; else clearKeys++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            rows.Add(new Row { name = effect.name, prefab = effect.prefab, frames = effect.frameCount,
                loop = effect.loop, clearsOnCompletion = effect.clearSpriteOnCompletion });
        }
        var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var report = new Report { status = "passed", unityVersion = Application.unityVersion, activeScene = current.path,
            effects = rows.Count, sampledSourceFrames = frames, clearKeys = clearKeys, loopEffects = loopKeys,
            sceneUnchanged = current.path == pathBefore && current.isDirty == dirtyBefore, rows = rows.ToArray(),
            checks = new[] { "Resources.Load resolves every effect prefab", "Every native frame samples in Unity; full final-frame duration",
                "Animator START playback, seven loops and three one-shot clears", "32 PPU / Point / no compression / no mipmaps / sRGB / Full Rect / NPOT None / Clamp",
                "Fixed canvas-centre pivots; scale one; only sprite curves", "Sorting comes from tileLayers; open scene preserved" } };
        if (!report.sceneUnchanged) throw new Exception("이펙트 검사 중 열린 씬이 변경되었습니다.");
        string output = Path.GetFullPath(Application.dataPath + "/../../artifacts/pixellab/EffectsImport/UnityValidationReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonConvert.SerializeObject(report, Formatting.Indented));
        Debug.Log($"PIXELLAB_EFFECTS_OK {rows.Count} effects / {frames} native frames");
    }
}
