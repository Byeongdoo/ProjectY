namespace Server.Game
{
	// 전투 상수(configs). 수치는 MasterTable에서 읽는다('캐릭터 조작 및 전투' 2-7~2-9, '스킬 시스템' 2-3·2-7·2-8)
	public static class CombatConfig
	{
		public const int DefenceCap = 20001;          // 방어율 상한
		public const int DamageVariance = 20002;      // 피해 편차 ±
		public const int DefaultCritMultiplier = 20003;
		public const int MinDamage = 20004;
		public const int SpiritOnBasicHit = 20013;    // 평타 적중 신력
		public const int SpiritOnKill = 20014;        // 처치(막타) 신력
		public const int BossControlRate = 20022;     // 보스 결박·둔화·기절 지속시간 배율
		public const int BasicInterval = 20032;       // 평타 간격(초, 공격 속도 1.0 기준)
		public const int TargetMaxRange = 20034;      // 대상 선택 최대 거리(칸)
		public const int SpiritRegenRate = 20035;     // 신력 자연 회복(최대 신력 대비 초당)
		public const int GlobalCooldown = 20044;      // 스킬 공용 재사용 대기(초)
		public const int ExecuteHpRate = 20050;       // 참수 조건 체력 비율
		public const int ExecuteMultiplier = 20051;   // 참수 피해 배율
		public const int TrapLifetime = 20052;        // 덫 유지 시간(초)
		public const int ThreatPerDamage = 20036;     // 피해 1당 위협
		public const int ThreatPerHeal = 20037;       // 회복 1당 위협
		public const int ThreatSwitchRate = 20038;    // 대상 변경 배율
		public const int FaintTime = 20039;           // 혼절 유지(초). 지나면 마지막 불씨에서 부활
		public const int CallSoulTime = 20040;        // 혼 부르기 F 홀드(초)
		public const int ReviveHpRate = 20041;        // 부활 체력 비율(혼 부르기·회혼술·불씨 자동 부활)
		public const int ArenaBonfireUses = 60001;    // 보스방 불씨 사용 횟수(파티당)
		public const int BonfireHoldTime = 60002;     // 보스방 불씨 F 홀드(초)
		public const int Lanterns = 60003;            // 혼백 등불(전멸 허용 횟수)
		public const int OutOfCombatDelay = 60026;    // 비전투 회복 시작 지연(초)
		public const int OutOfCombatRegen = 60027;    // 비전투 회복률(최대 체력 대비 초당)

		public static float Get(int configId) => Table.TableManager.Configs.Get(configId).value[0];
	}

	// statusEffects.statusID. 효과 값은 테이블(value)에서 읽는다('스킬 시스템' 2-5)
	public static class StatusId
	{
		public const int Root = 8001;        // 결박: 이동 불가
		public const int Burn = 8002;        // 영화: 초당 지속 피해(중첩)
		public const int Vulnerable = 8003;  // 취약: 받는 피해 증가
		public const int Slow = 8004;        // 둔화: 이동·공격 속도 감소
		public const int Stun = 8005;        // 기절: 이동·공격·스킬 불가
		public const int Empower = 8006;     // 강화: 공격력 증가
		public const int Haste = 8007;       // 신속: 이동 속도 증가
		public const int Fervor = 8008;      // 신명: 스킬 신력 소모 감소
		public const int Brace = 8009;       // 버팀: 받는 피해 감소, 이동 불가
		public const int Guard = 8010;       // 수호: 대상 피해 일부를 시전자가 대신 받음
		public const int Frenzy = 8011;      // 광기: 공격 속도 증가
		public const int Taunt = 8012;       // 도발: 시전자만 노린다
	}

	// #타입 정의
	public enum StatusKind { Harmful = 1, Beneficial = 2 }
	public enum SkillTargetType { Instant = 0, Enemy = 1, Ally = 2, Facing = 3 }
	public enum SkillGate { Spirit = 1, Cooldown = 2 }
	public enum CoefType { None = 0, Damage = 1, Heal = 2 }
	public enum SkillEffect { None = 0, Execute = 1, DashToTarget = 2, PlaceTrap = 3, Backstep = 4, Cleanse = 5, Revive = 6 }
	public enum CharacterType { PC = 1, Monster = 2, Boss = 3 }
}
