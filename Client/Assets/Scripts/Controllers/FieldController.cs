using UnityEngine;

// 칸에 깔린 설치물(착호갑사 덫 등). 효과는 서버가 판정한다. 리소스가 나올 때까지 CombatHud가 칸 표시로 그린다.
public class FieldController : MonoBehaviour
{
	public int Id { get; set; }
	public int SkillId { get; set; }  // 만든 스킬(spiritSkills.skillID)
	public Vector3Int CellPos { get; set; }
}
