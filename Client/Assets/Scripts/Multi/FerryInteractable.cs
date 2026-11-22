using UnityEngine;

// 삼천도 나루터의 사공(lobbySpots 8802). F → 원혼 선택 화면 → 던전 입장 요청.
// 임시: 큐·매칭·수락 없이 지정한 던전으로 바로 들어간다('파티 및 매칭 시스템'의 정식 흐름은 이후 단계).
public class FerryInteractable : NetInteractable
{
	[SerializeField] int _dungeonId = 87101; // dungeons.dungeonID — 산신당 저승길

	public override void Interact()
	{
		SpiritSelectPanel.Open(_dungeonId);
	}
}
