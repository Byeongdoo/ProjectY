using UnityEngine;
using UnityEngine.Tilemaps;

// 서버가 붙는 씬(삼천도·저승 던전)의 루트. 씬 타일맵으로 칸 판정을 만들고, 서버에 접속해 배정된 룸에 들어간다.
// 씬은 빌더(Tools > ProjectY > Levels > Build Multi Prototype)가 만든다. 서버는 같은 빌더가 내보낸 Common/MapData/<씬>.txt를 읽는다.
public class MultiScene : BaseScene
{
	[SerializeField] Define.Scene _sceneType = Define.Scene.Lobby;
	[SerializeField] bool _combatEnabled;   // 삼천도 false(전투 없음), 던전 true
	[SerializeField] Grid _grid;
	[SerializeField] Tilemap _ground;
	[SerializeField] Tilemap _collision;

	static MultiScene s_current;

	// 멀티 씬 밖(레거시 씬)에서는 기존처럼 전투 입력을 받는다
	public static bool CombatEnabled => s_current == null || s_current._combatEnabled;

	protected override void Init()
	{
		base.Init();
		s_current = this;
		SceneType = _sceneType;

		_collision.GetComponent<TilemapRenderer>().enabled = false;
		Managers.Map.LoadFromTilemaps(_grid, _ground, _collision);

		// 캐릭터 이름은 로컬 세이브가 생기면 거기서 읽는다. 비워 보내면 서버 기본 이름을 쓴다.
		Managers.Network.Connect();
		Managers.Network.RequestEnterGame("");
	}

	void OnDestroy()
	{
		if (s_current == this)
			s_current = null;
	}

	public override void Clear()
	{
	}
}
