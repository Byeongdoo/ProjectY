using Table;
using UnityEngine;

// 도트 카메라 공통 규칙(이승·멀티 카메라가 같이 쓴다).
// 도트 1픽셀이 화면 픽셀 정수 배가 되게 배율을 고르고, 카메라를 그 픽셀 격자에 맞춘다.
// 배율이 소수면 카메라가 움직일 때 타일 경계에 옆 텍셀이 비쳐 줄이 생긴다.
public class PixelCamera
{
	readonly Camera _camera;
	readonly float _pixelsPerUnit;
	readonly int _refHeight;
	float _screenPxPerUnit;

	// 도트 1픽셀 = 1 / PixelsPerUnit 월드 단위
	public float PixelsPerUnit => _pixelsPerUnit;

	public PixelCamera(Camera camera)
	{
		_camera = camera;
		if (TableManager.Configs == null)
			TableManager.LoadAll(new ResourcesTableReader());
		_pixelsPerUnit = TableManager.Configs.Get(90001).value[0]; // 논리 타일 크기(px) = 1칸
		_refHeight = Mathf.RoundToInt(TableManager.Configs.Get(90003).value[0]); // 기준 해상도 높이
		_camera.orthographic = true;
		_camera.allowMSAA = false;
	}

	// 기준 높이에 가장 가까운 정수 배를 쓴다. 화면이 기준 해상도의 정수 배가 아니면 기준보다 조금 넓거나 좁게 보인다.
	// 매 프레임 호출한다(창 크기가 바뀔 수 있다).
	public void UpdateZoom()
	{
		int zoom = Mathf.Max(1, Mathf.RoundToInt(Screen.height / (float)_refHeight));
		_screenPxPerUnit = _pixelsPerUnit * zoom;
		_camera.orthographicSize = Screen.height / (2f * _screenPxPerUnit);
	}

	// 대상 위치를 맵 범위 안으로 가두고 픽셀 격자에 맞춰 카메라를 옮긴다. UpdateZoom 뒤에 부른다.
	public void Follow(Vector3 target, Bounds map)
	{
		Vector3 pos = target;
		float halfH = _camera.orthographicSize;
		float halfW = halfH * _camera.aspect;
		pos.x = map.size.x <= halfW * 2 ? map.center.x : Mathf.Clamp(pos.x, map.min.x + halfW, map.max.x - halfW);
		pos.y = map.size.y <= halfH * 2 ? map.center.y : Mathf.Clamp(pos.y, map.min.y + halfH, map.max.y - halfH);

		pos.x = Mathf.Round(pos.x * _pixelsPerUnit) / _pixelsPerUnit;
		pos.y = Mathf.Round(pos.y * _pixelsPerUnit) / _pixelsPerUnit;
		// 화면 크기가 홀수면 화면 중심이 픽셀 가운데에 오므로 반 픽셀 밀어 텍셀 경계를 픽셀 경계에 맞춘다.
		if (Screen.width % 2 == 1) pos.x += 0.5f / _screenPxPerUnit;
		if (Screen.height % 2 == 1) pos.y += 0.5f / _screenPxPerUnit;
		pos.z = _camera.transform.position.z;
		_camera.transform.position = pos;
	}
}
