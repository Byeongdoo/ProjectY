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

	void Awake()
	{
		_camera = GetComponent<Camera>();
		if (TableManager.Configs == null)
			TableManager.LoadAll(new ResourcesTableReader());
		_pixelsPerUnit = TableManager.Configs.Get(90001).value[0]; // 논리 타일 크기(px) = 1칸
	}

	void LateUpdate()
	{
		if (_target == null) return;

		Vector3 pos = _target.position;
		Bounds b = _map.WorldBounds;
		float halfH = _camera.orthographicSize;
		float halfW = halfH * _camera.aspect;
		pos.x = b.size.x <= halfW * 2 ? b.center.x : Mathf.Clamp(pos.x, b.min.x + halfW, b.max.x - halfW);
		pos.y = b.size.y <= halfH * 2 ? b.center.y : Mathf.Clamp(pos.y, b.min.y + halfH, b.max.y - halfH);

		pos.x = Mathf.Round(pos.x * _pixelsPerUnit) / _pixelsPerUnit;
		pos.y = Mathf.Round(pos.y * _pixelsPerUnit) / _pixelsPerUnit;
		pos.z = transform.position.z;
		transform.position = pos;
	}
}
