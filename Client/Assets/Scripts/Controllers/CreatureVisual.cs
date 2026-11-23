using UnityEngine;

// 컨트롤러 없이 외형만 담은 프리팹(원혼)의 표시 설정. 프리팹 생성 도구가 스프라이트에서 정한다.
// 생성 시 ObjectManager가 붙이는 컨트롤러(내 캐릭터 MyPlayerController / 다른 플레이어 PlayerController)가 Init에서 읽어 간다.
public class CreatureVisual : MonoBehaviour
{
	// 체력 막대 높이(발밑 피벗 기준, 칸)
	public float hpBarHeight = 0.5f;

	// 8방향 클립(IDLE_SOUTH_EAST, RUN_NORTH …)을 가진 프리팹이면 켠다
	public bool eightDirClips = false;
}
