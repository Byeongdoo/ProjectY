using Table;
using UnityEngine;
using UnityEngine.SceneManagement;

// 이승 신단. F → 강림 확인 → 화면이 바래며 삼천도로 건너간다('이승·저승 전환 시스템' 2-2, 2-6).
// 이승 쪽은 씬만 바꾼다. 삼천도 씬이 서버 접속과 입장을 맡는다(이승 코드는 서버·패킷에 의존하지 않는다).
//
// 임시: 신단 해금(스토리 단계) 검사가 없다. 챕터 진행 시스템이 생기면 Interact에서 막는다.
// 임시: 확인 창은 OnGUI 플레이스홀더다. '인게임 HUD' 구현 때 교체한다.
public class IseungAltar : IseungInteractable
{
	const string SamcheondoScene = "Samcheondo";   // 삼천도 씬(서버 RoomManager.LobbySceneName과 같은 이름)
	const int ConfigTransitionLock = 10001;          // 강림·부활 연출 중 조작 잠금(초)

	// strings 시트에 추가가 필요한 표시 문자열(없으면 [ID]로 보인다)
	const string StrConfirm = "Ui_Descent_Confirm";
	const string StrKeys = "Ui_Confirm_Keys";

	[SerializeField] int _regionId;

	enum Phase { None, Confirm, Fading }
	Phase _phase = Phase.None;
	IseungPlayerController _player;
	float _fadeElapsed;
	float _fadeDuration;
	static Texture2D s_pixel;

	public int RegionId => _regionId;

	public override void Interact(IseungPlayerController player)
	{
		if (_phase != Phase.None)
			return;
		if (TableManager.Configs == null)
			TableManager.LoadAll(new ResourcesTableReader());

		_player = player;
		_player.InputLocked = true;
		_phase = Phase.Confirm;
	}

	void Update()
	{
		if (_phase == Phase.Confirm)
		{
			if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
			{
				_phase = Phase.Fading;
				_fadeElapsed = 0;
				_fadeDuration = TableManager.Configs.Get(ConfigTransitionLock).value[0];
			}
			else if (Input.GetKeyDown(KeyCode.Escape))
			{
				_phase = Phase.None;
				_player.InputLocked = false;
			}
		}
		else if (_phase == Phase.Fading)
		{
			_fadeElapsed += Time.deltaTime;
			if (_fadeElapsed >= _fadeDuration)
			{
				_phase = Phase.None;
				SceneManager.LoadScene(SamcheondoScene);
			}
		}
	}

	void OnGUI()
	{
		if (_phase == Phase.None)
			return;
		if (s_pixel == null)
		{
			s_pixel = new Texture2D(1, 1);
			s_pixel.SetPixel(0, 0, Color.white);
			s_pixel.Apply();
		}

		if (_phase == Phase.Confirm)
		{
			float w = Screen.width * 0.5f, h = Screen.height * 0.2f;
			Rect box = new Rect((Screen.width - w) / 2, Screen.height * 0.62f, w, h);
			GUI.color = new Color(0, 0, 0, 0.8f);
			GUI.DrawTexture(box, s_pixel);
			GUI.color = Color.white;
			var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Mathf.RoundToInt(h * 0.18f) };
			GUI.Label(new Rect(box.x, box.y, box.width, box.height * 0.65f), Localization.Get(StrConfirm), style);
			style.fontSize = Mathf.RoundToInt(h * 0.13f);
			GUI.Label(new Rect(box.x, box.y + box.height * 0.6f, box.width, box.height * 0.35f), Localization.Get(StrKeys), style);
		}
		else
		{
			// 이승 화면이 바랜다 — 하얗게 바랜 뒤 삼천도 강가에서 눈을 뜬다
			GUI.color = new Color(0.85f, 0.87f, 0.88f, Mathf.Clamp01(_fadeElapsed / _fadeDuration));
			GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), s_pixel);
			GUI.color = Color.white;
		}
	}
}
