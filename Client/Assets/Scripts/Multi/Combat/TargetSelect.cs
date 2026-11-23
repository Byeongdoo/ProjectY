using System.Collections.Generic;
using Google.Protobuf.Protocol;
using Table;
using UnityEngine;

// 단일 대상 스킬의 대상 선택('캐릭터 조작 및 전투' 2-5).
//   스킬 키 → 선택 상태(첫 커서: 단일 적 = 현재 대상, 없으면 가장 가까운 적 / 단일 아군 = 자신)
//   방향키 → 그 방향의 가장 가까운 다음 후보. Enter 또는 같은 스킬 키 → 확정. Esc → 취소(신력·쿨타임 소모 없음)
//   F1~F4 → 파티원 즉시 지정(F1 = 자신, 단일 아군 스킬)
//   후보는 대상 선택 최대 거리(configs 20034) 안. 선택 중에는 이동하지 않는다(MyPlayerController가 방향키를 이동에 쓰지 않음).
public static class TargetSelect
{
	const int TargetEnemy = 1;
	const int TargetAlly = 2;

	public static bool Active => Skill != null;
	public static SpiritSkillsRow Skill { get; private set; }
	public static int CursorId { get; private set; }
	static KeyCode _skillKey;
	static int _beganFrame;

	static readonly KeyCode[] PartyKeys = { KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4 };

	public static void Begin(SpiritSkillsRow skill, KeyCode skillKey)
	{
		List<CreatureController> candidates = Candidates(skill);
		if (candidates.Count == 0)
			return;

		Skill = skill;
		_skillKey = skillKey;
		_beganFrame = Time.frameCount;
		MyPlayerController me = Managers.Object.MyPlayer;
		if (skill.targetType == TargetAlly)
			CursorId = me.Id;
		else if (candidates.Exists(c => c.Id == MyCombat.CurrentTargetId))
			CursorId = MyCombat.CurrentTargetId;
		else
			CursorId = Nearest(candidates, me.CellPos).Id;
	}

	public static void End()
	{
		Skill = null;
		CursorId = 0;
	}

	// 선택 상태의 입력. 확정하면 C_Skill을 보낸다
	public static void Update()
	{
		// 선택을 연 스킬 키 입력이 같은 프레임에 확정으로 넘어오지 않게 한다
		if (Active == false || Time.frameCount == _beganFrame)
			return;

		MyPlayerController me = Managers.Object.MyPlayer;
		if (me == null)
		{
			End();
			return;
		}

		List<CreatureController> candidates = Candidates(Skill);
		CreatureController cursor = candidates.Find(c => c.Id == CursorId);
		if (cursor == null)
		{
			// 커서 대상이 쓰러지거나 멀어졌다
			if (candidates.Count == 0)
			{
				End();
				return;
			}
			cursor = Nearest(candidates, me.CellPos);
			CursorId = cursor.Id;
		}

		if (Input.GetKeyDown(KeyCode.Escape))
		{
			End();
			return;
		}

		Vector3Int dir = ArrowDown();
		if (dir != Vector3Int.zero)
		{
			CreatureController next = NextInDirection(candidates, cursor, dir);
			if (next != null)
				CursorId = next.Id;
		}

		if (Skill.targetType == TargetAlly)
		{
			for (int i = 0; i < PartyKeys.Length; i++)
			{
				if (Input.GetKeyDown(PartyKeys[i]) == false)
					continue;
				CreatureController member = PartyMember(i);
				if (member != null && candidates.Contains(member))
				{
					CursorId = member.Id;
					Confirm();
				}
				return;
			}
		}

		if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(_skillKey))
			Confirm();
	}

	// 사거리 밖이면 발동하지 않는다(커서는 회색으로 보인다, '스킬 시스템' 2-4)
	static void Confirm()
	{
		CreatureController target = Managers.Object.FindById(CursorId)?.GetComponent<CreatureController>();
		if (target != null && InRange(Skill, target))
		{
			C_Skill packet = new C_Skill() { Info = new SkillInfo() { SkillId = Skill.skillID, TargetId = target.Id } };
			Managers.Network.Send(packet);
			if (Skill.targetType == TargetEnemy)
				MyCombat.CurrentTargetId = target.Id;
		}
		End();
	}

	public static bool InRange(SpiritSkillsRow skill, CreatureController target)
	{
		MyPlayerController me = Managers.Object.MyPlayer;
		if (me == null || target == null)
			return false;
		int range = Mathf.Min(skill.range, Mathf.RoundToInt(MyCombat.TargetMaxRange));
		return CellDist(target.CellPos, me.CellPos) <= range;
	}

	// Tab: 가까운 적부터 현재 대상을 순환한다
	public static void CycleEnemyTarget()
	{
		MyPlayerController me = Managers.Object.MyPlayer;
		if (me == null)
			return;
		List<CreatureController> enemies = Managers.Object.FindCreatures(GameObjectType.Monster, me.CellPos, Mathf.RoundToInt(MyCombat.TargetMaxRange));
		if (enemies.Count == 0)
		{
			MyCombat.CurrentTargetId = 0;
			return;
		}
		enemies.Sort((a, b) => CellDist(a.CellPos, me.CellPos).CompareTo(CellDist(b.CellPos, me.CellPos)));
		int index = enemies.FindIndex(c => c.Id == MyCombat.CurrentTargetId);
		MyCombat.CurrentTargetId = enemies[(index + 1) % enemies.Count].Id;
	}

	static List<CreatureController> Candidates(SpiritSkillsRow skill)
	{
		MyPlayerController me = Managers.Object.MyPlayer;
		GameObjectType type = skill.targetType == TargetAlly ? GameObjectType.Player : GameObjectType.Monster;
		return Managers.Object.FindCreatures(type, me.CellPos, Mathf.RoundToInt(MyCombat.TargetMaxRange));
	}

	// F1 = 자신, F2~F4 = 다른 플레이어(파티 시스템 전에는 같은 던전의 플레이어를 접속 순으로)
	static CreatureController PartyMember(int index)
	{
		MyPlayerController me = Managers.Object.MyPlayer;
		if (index == 0)
			return me;
		List<CreatureController> others = Managers.Object.FindCreatures(GameObjectType.Player, me.CellPos, int.MaxValue);
		others.RemoveAll(c => c.Id == me.Id);
		others.Sort((a, b) => a.Id.CompareTo(b.Id));
		return index - 1 < others.Count ? others[index - 1] : null;
	}

	static CreatureController Nearest(List<CreatureController> list, Vector3Int from)
	{
		CreatureController best = null;
		int bestDist = int.MaxValue;
		foreach (CreatureController c in list)
		{
			int d = CellDist(c.CellPos, from);
			if (d < bestDist)
			{
				best = c;
				bestDist = d;
			}
		}
		return best;
	}

	// 커서에서 누른 방향 쪽(반평면)에 있는 후보 중 가장 가까운 것. 방향에서 벗어날수록 멀게 친다
	static CreatureController NextInDirection(List<CreatureController> list, CreatureController cursor, Vector3Int dir)
	{
		Vector2 d = new Vector2(dir.x, dir.y).normalized;
		CreatureController best = null;
		float bestScore = float.MaxValue;
		foreach (CreatureController c in list)
		{
			if (c == cursor)
				continue;
			Vector2 delta = new Vector2(c.CellPos.x - cursor.CellPos.x, c.CellPos.y - cursor.CellPos.y);
			float along = Vector2.Dot(delta, d);
			if (along <= 0f)
				continue;
			float cos = along / delta.magnitude;
			float score = delta.magnitude * (2f - cos);
			if (score < bestScore)
			{
				best = c;
				bestScore = score;
			}
		}
		return best;
	}

	static Vector3Int ArrowDown()
	{
		if (Input.GetKeyDown(KeyCode.UpArrow)) return Vector3Int.up;
		if (Input.GetKeyDown(KeyCode.DownArrow)) return Vector3Int.down;
		if (Input.GetKeyDown(KeyCode.LeftArrow)) return Vector3Int.left;
		if (Input.GetKeyDown(KeyCode.RightArrow)) return Vector3Int.right;
		return Vector3Int.zero;
	}

	public static int CellDist(Vector3Int a, Vector3Int b)
	{
		return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
	}
}
