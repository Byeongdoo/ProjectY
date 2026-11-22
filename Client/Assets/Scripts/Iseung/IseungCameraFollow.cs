using UnityEngine;

// 이승 카메라: 주인공을 따라가되 맵 밖은 비추지 않고, 도트 픽셀이 화면 픽셀 정수 배로 그려지게 한다(PixelCamera 규칙).
[RequireComponent(typeof(Camera))]
public class IseungCameraFollow : MonoBehaviour
{
	[SerializeField] Transform _target;
	[SerializeField] IseungMap _map;

	PixelCamera _pixel;

	void Awake()
	{
		_pixel = new PixelCamera(GetComponent<Camera>());
	}

	void LateUpdate()
	{
		_pixel.UpdateZoom();
		if (_target == null) return;
		_pixel.Follow(_target.position, _map.WorldBounds);
	}
}
