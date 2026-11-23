using System.Collections.Generic;
using Google.Protobuf.Protocol;
using UnityEngine;

// 개발·테스트용 허수아비(서버 TrainingDummy, ObjectInfo.trainingDummy)의 클라 표시.
// 리소스가 없어서 코드로 만든 단색 플레이스홀더 스프라이트를 쓴다(허수아비 스프라이트는 요청 목록 docs/art-requests/spirit-combat-vfx.md).
// 판정·체력은 서버가 한다. 클라는 MonsterController로 피격 연출을 그대로 받고, 피해 측정기(TrainingDummyMeter)를 붙인다.
public static class TrainingDummyView
{
	// 플레이스홀더 크기(px). 기준 캐릭터 32×48보다 조금 작게(화면 연출용, 게임 수치 아님)
	const int Width = 20;
	const int Height = 40;
	const int ConfigPixelsPerUnit = 90001; // 논리 타일 크기(px) = 1칸

	static Sprite s_sprite;

	public static GameObject Create(ObjectInfo info)
	{
		GameObject go = new GameObject("TrainingDummy");
		SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
		sr.sprite = PlaceholderSprite();
		sr.spriteSortPoint = SpriteSortPoint.Pivot;
		foreach (Table.TileLayersRow layer in Table.TableManager.TileLayers.Rows)
			if (layer.name == "Props")
				sr.sortingOrder = layer.sortOrder; // 캐릭터와 같은 층(발밑 기준 가림)

		CreatureVisual visual = go.AddComponent<CreatureVisual>();
		visual.hpBarHeight = sr.sprite.bounds.max.y;
		go.AddComponent<TrainingDummyMeter>();
		go.AddComponent<MonsterController>(); // Init은 Start에서 돈다 — CreatureVisual을 먼저 붙인다
		return go;
	}

	// 발밑 피벗의 말뚝 + 가로대 + 짚단 머리
	static Sprite PlaceholderSprite()
	{
		if (s_sprite != null)
			return s_sprite;

		Color clear = new Color(0f, 0f, 0f, 0f);
		Color wood = new Color(0.45f, 0.30f, 0.17f);
		Color straw = new Color(0.86f, 0.74f, 0.42f);
		Color dark = new Color(0.25f, 0.17f, 0.10f);

		Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
		Color[] px = new Color[Width * Height];
		for (int i = 0; i < px.Length; i++)
			px[i] = clear;

		void Rect(int x0, int y0, int w, int h, Color c)
		{
			for (int y = y0; y < y0 + h; y++)
				for (int x = x0; x < x0 + w; x++)
					if (x >= 0 && x < Width && y >= 0 && y < Height)
						px[y * Width + x] = c;
		}

		Rect(9, 0, 2, 30, wood);       // 말뚝
		Rect(2, 22, 16, 2, wood);      // 가로대(팔)
		Rect(4, 10, 12, 14, straw);    // 짚 몸통
		Rect(6, 28, 8, 10, straw);     // 짚 머리
		Rect(4, 16, 12, 1, dark);      // 허리 끈
		Rect(6, 33, 8, 1, dark);       // 목 끈

		tex.SetPixels(px);
		tex.Apply();
		float ppu = Table.TableManager.Configs.Get(ConfigPixelsPerUnit).value[0];
		s_sprite = Sprite.Create(tex, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0f), ppu);
		s_sprite.name = "TrainingDummyPlaceholder";
		return s_sprite;
	}
}

// 허수아비가 받은 피해 측정(합계·초당 피해·시간). 서버와 같은 간격(configs 99003)으로 맞지 않으면 새 측정을 시작한다.
public class TrainingDummyMeter : MonoBehaviour
{
	const int ConfigResetDelay = 99003;

	public static readonly List<TrainingDummyMeter> All = new List<TrainingDummyMeter>();

	public long Total { get; private set; }
	public int Hits { get; private set; }
	float _start;
	float _last;

	void OnEnable() { All.Add(this); }
	void OnDisable() { All.Remove(this); }

	float ResetDelay => Table.TableManager.Configs.Get(ConfigResetDelay).value[0];

	public bool Active => Hits > 0 && Time.time - _last <= ResetDelay;

	public void Add(int amount)
	{
		float now = Time.time;
		if (Active == false)
		{
			_start = now;
			Total = 0;
			Hits = 0;
		}
		Total += amount;
		Hits++;
		_last = now;
	}

	// 첫 타격부터 마지막 타격까지(측정이 끝나면 고정)
	public float Elapsed => _last - _start;

	// 한 대만 맞았으면 시간이 0이라 초당 피해를 내지 않는다
	public float Dps => Elapsed > 0f ? Total / Elapsed : 0f;
}
