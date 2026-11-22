using UnityEngine;

// 멀티 씬 카메라: 내 캐릭터를 따라가되 맵 밖은 비추지 않고, 도트 픽셀이 화면 픽셀 정수 배로 그려지게 한다(이승 카메라와 같은 PixelCamera 규칙).
// 보스방 고정 카메라('레벨 디자인 가이드' 2-7)는 보스전 구현 때 붙인다.
[RequireComponent(typeof(Camera))]
public class NetCameraFollow : MonoBehaviour
{
	PixelCamera _pixel;

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
	}
}
