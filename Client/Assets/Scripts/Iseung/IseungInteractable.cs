using System.Collections.Generic;
using UnityEngine;

// 이승에서 F로 상호작용하는 대상(신단·NPC·조사 대상). 오브젝트가 놓인 칸을 바라보고 F를 누르면 Interact가 불린다.
// 서버·패킷에 의존하지 않는다(이승은 클라 단독).
public abstract class IseungInteractable : MonoBehaviour
{
	static readonly List<IseungInteractable> s_active = new List<IseungInteractable>();

	[SerializeField] protected Grid _grid;

	public Vector3Int Cell => _grid != null ? _grid.WorldToCell(transform.position) : Vector3Int.FloorToInt(transform.position);

	protected virtual void OnEnable() { s_active.Add(this); }
	protected virtual void OnDisable() { s_active.Remove(this); }

	public static IseungInteractable FindAt(Vector3Int cell)
	{
		foreach (IseungInteractable it in s_active)
			if (it.Cell == cell)
				return it;
		return null;
	}

	public abstract void Interact(IseungPlayerController player);
}
