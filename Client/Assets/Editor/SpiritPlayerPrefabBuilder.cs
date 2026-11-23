using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Table;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 원혼 5종(SpiritsV2)을 저승 던전에서 플레이어 캐릭터로 쓰는 프리팹으로 만든다.
//   - SpiritsV2/<키>/<키>.controller : 11동작 × 8방향 클립을 상태 이름 = 클립 이름(IDLE_SOUTH …)으로 담는다. 전환은 PlayerController가 Play로 한다.
//   - Resources/Prefabs/Creature/Spirit/<prefabName>.prefab : 외형(SpriteRenderer·Animator) + 표시 설정(CreatureVisual)만 담는다.
//     컨트롤러는 생성 시 ObjectManager가 붙인다(내 캐릭터 MyPlayerController / 다른 플레이어 PlayerController).
// 프리팹 이름은 spirits.prefabName(테이블)이 원본이다. 아래 표는 원혼 ID ↔ 아트 폴더 연결만 정한다.
public static class SpiritPlayerPrefabBuilder
{
    const string ArtRoot = "Assets/Resources/Art/Characters/SpiritsV2";
    public const string PrefabDir = "Assets/Resources/Prefabs/Creature/Spirit";

    static readonly (int spiritId, string artKey)[] Spirits =
    {
        (8501, "MumyeongJangsu"), // 무명장수
        (8502, "Mangnani"),       // 망나니
        (8503, "ChakhoGapsa"),    // 착호갑사
        (8504, "Kkokdusoe"),      // 꼭두쇠
        (8505, "Uinyeo"),         // 의녀
    };

    static readonly string[] Anims = { "IDLE", "RUN", "ATTACK", "HIT", "STUN", "DIE", "KNOCKBACK", "SKILL_1", "SKILL_2", "SKILL_3", "SKILL_4" };
    static readonly string[] Dirs = { "SOUTH", "SOUTH_EAST", "EAST", "NORTH_EAST", "NORTH", "NORTH_WEST", "WEST", "SOUTH_WEST" };

    [MenuItem("Tools/ProjectY/Build Spirit Player Prefabs")]
    public static void Build()
    {
        TableManager.LoadAll(new ResourcesTableReader());
        int order = TableManager.TileLayers.Rows.First(r => r.name == "Props").sortOrder;
        Directory.CreateDirectory(PrefabDir);

        foreach (var (spiritId, key) in Spirits)
        {
            var row = TableManager.Spirits.Get(spiritId);
            string root = $"{ArtRoot}/{key}";
            var ctrl = BuildController(root, key);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{root}/Sprites/Rotations/south.png");
            if (!sprite) throw new Exception($"원혼 정면 스프라이트 누락: {root}/Sprites/Rotations/south.png");
            float hpBarHeight = OpaqueTop(sprite);

            var go = new GameObject(row.prefabName, typeof(SpriteRenderer), typeof(Animator), typeof(CreatureVisual));
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sortingOrder = order; sr.spriteSortPoint = SpriteSortPoint.Pivot;
            go.GetComponent<Animator>().runtimeAnimatorController = ctrl;
            var visual = go.GetComponent<CreatureVisual>();
            visual.eightDirClips = true;
            visual.hpBarHeight = hpBarHeight;
            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{row.prefabName}.prefab");
            UnityEngine.Object.DestroyImmediate(go);

            // 예전 구조(내 캐릭터용 _My 프리팹)가 남아 있으면 지운다
            AssetDatabase.DeleteAsset($"{PrefabDir}/{row.prefabName}_My.prefab");
            Debug.Log($"[SpiritPlayerPrefabBuilder] {spiritId} {key} → {row.prefabName}, 체력 막대 높이 {hpBarHeight:0.###}");
        }
        AssetDatabase.SaveAssets();
    }

    static AnimatorController BuildController(string root, string key)
    {
        string path = $"{root}/{key}.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        var missing = new List<string>();
        foreach (string anim in Anims)
            foreach (string dir in Dirs)
            {
                string name = anim + "_" + dir;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{root}/Animations/{name}.anim");
                if (!clip) { missing.Add(name); continue; }
                var state = sm.AddState(name); state.motion = clip;
                if (name == "IDLE_SOUTH") sm.defaultState = state;
            }
        if (missing.Count > 0) throw new Exception($"{key} 애니메이션 누락: {string.Join(", ", missing)}");
        return ctrl;
    }

    // 발밑 피벗 기준, 불투명 픽셀의 가장 높은 곳(칸). 캔버스 여백(무명장수 96px 등)을 빼고 머리 위에 체력 막대를 둔다.
    internal static float OpaqueTop(Sprite sprite)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
        int top = 0;
        for (int y = tex.height - 1; y >= 0 && top == 0; y--)
            for (int x = 0; x < tex.width; x++)
                if (tex.GetPixel(x, y).a > 0) { top = y + 1; break; }
        UnityEngine.Object.DestroyImmediate(tex);
        return (top - sprite.pivot.y) / sprite.pixelsPerUnit;
    }
}
