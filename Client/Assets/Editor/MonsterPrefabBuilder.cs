using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Table;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 망령(적) 리소스(Resources/Art/Monsters/<characterID>)를 저승 던전 몬스터 프리팹으로 만든다.
//   - Resources/Prefabs/Creature/Monster/<prefabName>.prefab (MonsterController, 8방향 클립)
// 프리팹 이름은 characterInfos.prefabName(테이블)이 원본이다. 아트 폴더가 있는 characterID만 만든다.
// Animator 컨트롤러는 리소스 제작 쪽이 만든 <characterID>.controller를 그대로 쓰고(GUID 유지), 없을 때만 만든다.
public static class MonsterPrefabBuilder
{
    const string ArtRoot = "Assets/Resources/Art/Monsters";
    public const string PrefabDir = "Assets/Resources/Prefabs/Creature/Monster";

    // 서버가 보내는 상태(이동·스킬·사망)를 보여 주는 데 꼭 필요한 동작. 나머지(HIT·STUN 등)는 있으면 함께 담긴다.
    static readonly string[] RequiredAnims = { "IDLE", "RUN", "ATTACK", "DIE" };
    static readonly string[] Dirs = { "SOUTH", "SOUTH_EAST", "EAST", "NORTH_EAST", "NORTH", "NORTH_WEST", "WEST", "SOUTH_WEST" };

    [MenuItem("Tools/ProjectY/Build Monster Prefabs")]
    public static void Build()
    {
        TableManager.LoadAll(new ResourcesTableReader());
        int order = TableManager.TileLayers.Rows.First(r => r.name == "Props").sortOrder;
        Directory.CreateDirectory(PrefabDir);

        int built = 0;
        foreach (CharacterInfosRow row in TableManager.CharacterInfos.Rows)
        {
            string root = $"{ArtRoot}/{row.characterID}";
            if (AssetDatabase.IsValidFolder(root) == false)
                continue;

            var ctrl = LoadOrBuildController(root, row.characterID.ToString());
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{root}/Sprites/Rotations/south.png");
            if (!sprite) throw new Exception($"망령 정면 스프라이트 누락: {root}/Sprites/Rotations/south.png");
            float hpBarHeight = SpiritPlayerPrefabBuilder.OpaqueTop(sprite);

            var go = new GameObject(row.prefabName, typeof(SpriteRenderer), typeof(Animator));
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sortingOrder = order; sr.spriteSortPoint = SpriteSortPoint.Pivot;
            go.GetComponent<Animator>().runtimeAnimatorController = ctrl;
            var mc = go.AddComponent<MonsterController>();
            var so = new SerializedObject(mc);
            so.FindProperty("_eightDirClips").boolValue = true;
            so.FindProperty("_hpBarHeight").floatValue = hpBarHeight;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{row.prefabName}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            built++;
            Debug.Log($"[MonsterPrefabBuilder] {row.characterID} → {row.prefabName}, 체력 막대 높이 {hpBarHeight:0.###}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[MonsterPrefabBuilder] {built}종 생성. 아트 폴더가 없는 characterInfos 행은 건너뛴다.");
    }

    static RuntimeAnimatorController LoadOrBuildController(string root, string key)
    {
        string path = $"{root}/{key}.controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null)
        {
            var names = new HashSet<string>(existing.layers[0].stateMachine.states.Select(s => s.state.name));
            var missing = RequiredAnims.SelectMany(a => Dirs.Select(d => a + "_" + d)).Where(n => names.Contains(n) == false).ToList();
            if (missing.Count > 0) throw new Exception($"{path} 상태 누락: {string.Join(", ", missing)}");
            return existing;
        }

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{root}/Animations" })
            .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)));
        foreach (var clip in clips)
        {
            var state = sm.AddState(clip.name); state.motion = clip;
            if (clip.name == "IDLE_SOUTH") sm.defaultState = state;
        }
        return ctrl;
    }
}
