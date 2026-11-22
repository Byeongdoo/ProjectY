using System.Collections.Generic;
using UnityEngine;

// 멀티 씬(삼천도·던전)에서 F로 상호작용하는 대상. 바라보는 앞 칸에 있으면 Interact가 불린다.
// 클라는 요청만 보내고, 위치·조건 판정은 서버가 한다.
public abstract class NetInteractable : MonoBehaviour
{
	static readonly List<NetInteractable> s_active = new List<NetInteractable>();

	public Vector3Int Cell => Vector3Int.FloorToInt(transform.position);

	protected virtual void OnEnable() { s_active.Add(this); }
	protected virtual void OnDisable() { s_active.Remove(this); }

	public static NetInteractable FindAt(Vector3Int cell)
	{
		foreach (NetInteractable it in s_active)
			if (it.Cell == cell)
				return it;
		return null;
	}

	public abstract void Interact();
}
