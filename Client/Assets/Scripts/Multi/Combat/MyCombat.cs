using System.Collections.Generic;
using Table;
using UnityEngine;

// 내 캐릭터의 전투 상태(HUD 표시와 입력 거르기용). 판정은 서버가 한다.
// 값은 서버 패킷(S_Skill·S_ChangeSpirit·S_CancelSkill)으로만 바뀐다 — 쿨타임·신력이 모자란 요청을 미리 거르는 데만 쓴다.
public static class MyCombat
{
	const int ConfigBasicInterval = 20032;  // 평타 간격(초, 공격 속도 1.0 기준)
	const int ConfigGlobalCooldown = 20044; // 스킬 공용 재사용 대기(초)
	const int ConfigTargetMaxRange = 20034; // 대상 선택 최대 거리(칸)
	const int HeroCharacterId = 1000001;    // characterLevel 주인공 그룹(서버 Player와 같은 값)

	// 평타를 보냈는데 서버 응답(S_Skill)이 없으면 다시 보낼 수 있을 때까지(입력 처리용, 게임 수치 아님)
	const float BasicResendGuard = 0.5f;

	public static int Spirit;
	public static int MaxSpirit;

	// 마지막으로 확정한 적(Tab·단일 적 스킬). 다음 단일 적 스킬의 첫 커서
	public static int CurrentTargetId;

	// 던전 진행(S_DungeonState): 혼백 등불, 보스방 불씨 남은 횟수
	public static int Lanterns, MaxLanterns;
	public static int ArenaBonfireUses, MaxArenaBonfireUses;

	static float _gcdEnd;
	static readonly Dictionary<int, float> _cooldownEnd = new Dictionary<int, float>();
	static float _nextBasicTime;
	static float _basicSentAt = -1f;

	public static int CastSkillId { get; private set; }
	public static float CastStart { get; private set; }
	public static float CastEnd { get; private set; }
	public static bool IsCasting => CastSkillId != 0 && Time.time < CastEnd;

	public static float TargetMaxRange => TableManager.Configs.Get(ConfigTargetMaxRange).value[0];

	public static void Reset(int spirit, int maxSpirit)
	{
		Spirit = spirit;
		MaxSpirit = maxSpirit;
		CurrentTargetId = 0;
		_gcdEnd = 0f;
		_cooldownEnd.Clear();
		_nextBasicTime = 0f;
		_basicSentAt = -1f;
		CastSkillId = 0;
	}

	// 서버가 받아들인 내 평타·스킬
	public static void OnMySkill(int skillId, float castTime)
	{
		float now = Time.time;
		if (skillId == 0)
		{
			_nextBasicTime = now + BasicInterval();
			_basicSentAt = -1f;
			return;
		}

		_gcdEnd = now + TableManager.Configs.Get(ConfigGlobalCooldown).value[0];
		if (castTime > 0f)
		{
			CastSkillId = skillId;
			CastStart = now;
			CastEnd = now + castTime;
			return;
		}
		StartCooldown(skillId, now);
	}

	public static void OnMyCancel()
	{
		CastSkillId = 0;
	}

	// 개발·테스트 명령(DEBUG_REFILL)과 함께. 서버도 같은 것을 초기화한다
	public static void ClearCooldowns()
	{
		_cooldownEnd.Clear();
		_gcdEnd = 0f;
		_nextBasicTime = 0f;
		_basicSentAt = -1f;
	}

	// 시전이 끝나면 그때부터 쿨타임(서버도 효과가 날 때 쓴다)
	public static void Update()
	{
		if (CastSkillId != 0 && Time.time >= CastEnd)
		{
			StartCooldown(CastSkillId, CastEnd);
			CastSkillId = 0;
		}
	}

	static void StartCooldown(int skillId, float from)
	{
		if (TableManager.SpiritSkills.TryGet(skillId, out SpiritSkillsRow row) && row.gate == 2)
			_cooldownEnd[skillId] = from + row.coolTime;
	}

	public static float GlobalCooldownLeft => Mathf.Max(0f, _gcdEnd - Time.time);

	public static float CooldownLeft(int skillId)
	{
		return _cooldownEnd.TryGetValue(skillId, out float end) ? Mathf.Max(0f, end - Time.time) : 0f;
	}

	// 신명(신력 소모 −)
	public static float SpiritCost(SpiritSkillsRow row)
	{
		float fervor = Managers.Object.MyPlayer != null ? Managers.Object.MyPlayer.StatusValue(CreatureController.StatusFervor) : 0f;
		return row.gate == 1 ? row.spiritCost * (1f - fervor) : 0f;
	}

	public static bool CanUse(SpiritSkillsRow row)
	{
		if (GlobalCooldownLeft > 0f || IsCasting)
			return false;
		if (row.gate == 2)
			return CooldownLeft(row.skillID) <= 0f;
		return Spirit >= SpiritCost(row);
	}

	// ---- 평타 ----

	// 평타 간격 = configs 20032 / 공격 속도(캐릭터 × 원혼 배율 × 광기·둔화). 서버와 같은 식
	public static float BasicInterval()
	{
		MyPlayerController me = Managers.Object.MyPlayer;
		float attackSpeed = 1f;
		if (me != null)
		{
			if (TableManager.CharacterLevel.TryGet((HeroCharacterId, Mathf.Max(1, me.Stat.Level)), out CharacterLevelRow c))
				attackSpeed = c.attackSpeed;
			if (TableManager.Spirits.TryGet(me.SpiritId, out SpiritsRow s))
				attackSpeed *= s.attackSpeedRate;
			attackSpeed *= (1f + me.StatusValue(CreatureController.StatusFrenzy)) * (1f - me.StatusValue(CreatureController.StatusSlow));
		}
		return TableManager.Configs.Get(ConfigBasicInterval).value[0] / Mathf.Max(0.05f, attackSpeed);
	}

	// 다음 평타 시각은 서버 응답을 받은 때부터 센다 → 서버 쪽 간격보다 늦게 도착해 거절당하지 않는다
	public static bool CanSendBasic()
	{
		float now = Time.time;
		if (now < _nextBasicTime)
			return false;
		return _basicSentAt < 0f || now - _basicSentAt > BasicResendGuard;
	}

	public static void OnBasicSent()
	{
		_basicSentAt = Time.time;
	}

	// 내 원혼의 스킬(슬롯 1~4 순서)
	public static SpiritSkillsRow SkillInSlot(int slot)
	{
		MyPlayerController me = Managers.Object.MyPlayer;
		if (me == null || TableManager.Spirits.TryGet(me.SpiritId, out SpiritsRow s) == false)
			return null;
		foreach (int id in s.skillIDs)
			if (TableManager.SpiritSkills.TryGet(id, out SpiritSkillsRow row) && row.slot == slot)
				return row;
		return null;
	}
}
