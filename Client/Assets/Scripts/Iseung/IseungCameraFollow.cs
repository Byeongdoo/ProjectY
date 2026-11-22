using Table;
using UnityEngine;

// 이승 카메라: 주인공을 따라가되 맵 밖은 비추지 않고, 도트가 흔들리지 않게 1픽셀 격자에 맞춘다.
[RequireComponent(typeof(Camera))]
public class IseungCameraFollow : MonoBehaviour
{
	[SerializeField] Transform _target;
	[SerializeField] IseungMap _map;

	Camera _camera;
	float _pixelsPerUnit;
	int _refHeight;

	void Awake()
	{
		_camera = GetComponent<Camera>();
		if (TableManager.Configs == null)
			TableManager.LoadAll(new ResourcesTableReader());
		_pixelsPerUnit = TableManager.Configs.Get(90001).value[0]; // 논리 타일 크기(px) = 1칸
		_refHeight = Mathf.RoundToInt(TableManager.Configs.Get(90003).value[0]);
		_camera.allowMSAA = false;
	}

	void LateUpdate()
	{
		if (_target == null) return;

		// 도트 1픽셀이 화면 픽셀 정수 배가 되게 배율을 고른다. 배율이 소수면 타일 경계에서 옆 텍셀이 비쳐 줄이 생긴다.
		// 기준 높이에 가장 가까운 정수 배를 쓴다. 화면이 기준 해상도의 정수 배가 아니면 기준보다 조금 넓거나 좁게 보인다.
		int zoom = Mathf.Max(1, Mathf.RoundToInt(Screen.height / (float)_refHeight));
		float screenPxPerUnit = _pixelsPerUnit * zoom;
		_camera.orthographicSize = Screen.height / (2f * screenPxPerUnit);

		Vector3 pos = _target.position;
		Bounds b = _map.WorldBounds;
		float halfH = _camera.orthographicSize;
		float halfW = halfH * _camera.aspect;
		pos.x = b.size.x <= halfW * 2 ? b.center.x : Mathf.Clamp(pos.x, b.min.x + halfW, b.max.x - halfW);
		pos.y = b.size.y <= halfH * 2 ? b.center.y : Mathf.Clamp(pos.y, b.min.y + halfH, b.max.y - halfH);

		pos.x = Mathf.Round(pos.x * _pixelsPerUnit) / _pixelsPerUnit;
		pos.y = Mathf.Round(pos.y * _pixelsPerUnit) / _pixelsPerUnit;
		// 화면 크기가 홀수면 화면 중심이 픽셀 가운데에 오므로 반 픽셀 밀어 텍셀 경계를 픽셀 경계에 맞춘다.
		if (Screen.width % 2 == 1) pos.x += 0.5f / screenPxPerUnit;
		if (Screen.height % 2 == 1) pos.y += 0.5f / screenPxPerUnit;
		pos.z = transform.position.z;
		transform.position = pos;
	}
}
