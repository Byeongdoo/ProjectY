using UnityEngine;

// 멀티 씬 카메라: 내 캐릭터를 따라가되 맵 밖은 비추지 않고, 도트 픽셀이 화면 픽셀 정수 배로 그려지게 한다(이승 카메라와 같은 PixelCamera 규칙).
// 보스방 고정 카메라('레벨 디자인 가이드' 2-7)는 보스전 구현 때 붙인다.
[RequireComponent(typeof(Camera))]
public class NetCameraFollow : MonoBehaviour
{
	PixelCamera _pixel;

	// 화면 흔들림('캐릭터 조작 및 전투' 2-10): 치명타·보스 강공격에만. 칸 단위 화면에서 잦으면 위치가 안 읽힌다.
	// 도트 픽셀 단위로 흔들어 픽셀 격자를 지킨다. 아래 값은 화면 연출용이며 게임 수치가 아니다.
	public const float CriticalShakeTime = 0.12f;
	public const int CriticalShakePixels = 1;

	static float s_shakeUntil;
	static int s_shakePixels;

	public static void Shake(float duration, int pixels)
	{
		s_shakePixels = Time.time < s_shakeUntil ? Mathf.Max(pixels, s_shakePixels) : pixels;
		s_shakeUntil = Mathf.Max(s_shakeUntil, Time.time + duration);
	}

	void Awake()
	{
		_pixel = new PixelCamera(GetComponent<Camera>());
	}

	void LateUpdate()
	{
		_pixel.UpdateZoom();

		MyPlayerController me = Managers.Object.MyPlayer;
		if (me == null || Managers.Map.CurrentGrid == null)
			return;

		_pixel.Follow(me.transform.position, Managers.Map.WorldBounds);

		if (Time.time < s_shakeUntil && s_shakePixels > 0)
		{
			float px = 1f / _pixel.PixelsPerUnit;
			Vector3 offset = new Vector3(Random.Range(-s_shakePixels, s_shakePixels + 1), Random.Range(-s_shakePixels, s_shakePixels + 1), 0f) * px;
			transform.position += offset;
		}
	}
}
