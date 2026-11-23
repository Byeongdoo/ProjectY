using System;

namespace Server.Game
{
	// 피해·회복 판정('캐릭터 조작 및 전투' 2-7). 곱연산만 쓴다.
	//   기본 피해 = 공격력 × 계수 → 치명타 × 배율 → × (1 − MIN(방어율, 상한)) → × (1 ± 편차) → 반올림, 최소 1
	//   회복     = 공격력 × 계수 × 치명타 × (1 ± 편차). 방어율의 영향을 받지 않는다
	// 받는 쪽 배율(원혼 받는 피해 배율·취약·버팀·수호)은 GameObject.OnDamaged에서 방어율 상한 뒤에 곱한다('원혼(직업) 시스템' 2-4).
	public static class Combat
	{
		// C_Skill·S_Skill의 skillId 0 = 평타(Space). 그 외는 spiritSkills.skillID
		public const int BasicSkillId = 0;

		static readonly Random _random = new Random();

		// 치명타·편차 주사위(0 이상 1 미만). 테스트가 결과를 고정할 때 바꾼다. 한 번의 피해 판정은 치명타 → 편차 순으로 두 번 굴린다
		public static Func<double> Roll { get; set; } = () => _random.NextDouble();

		public static void Damage(GameObject attacker, GameObject target, float coefficient, float extraMultiplier = 1f)
		{
			if (attacker == null || target == null || target.IsAlive == false)
				return;

			float raw = attacker.AttackPower * coefficient * extraMultiplier;
			bool critical = Roll() < attacker.CriticalRate;
			if (critical)
				raw *= attacker.CriticalMultiplier;
			raw *= 1f - Math.Min(target.Defence, CombatConfig.Get(CombatConfig.DefenceCap));
			raw *= Variance();

			int damage = Math.Max((int)CombatConfig.Get(CombatConfig.MinDamage), (int)Math.Round(raw));
			target.OnDamaged(attacker, damage, critical);
		}

		public static void Heal(GameObject healer, GameObject target, float coefficient)
		{
			if (healer == null || target == null || target.IsAlive == false)
				return;

			float raw = healer.AttackPower * coefficient;
			bool critical = Roll() < healer.CriticalRate;
			if (critical)
				raw *= healer.CriticalMultiplier;
			raw *= Variance();

			target.OnHealed(healer, Math.Max(0, (int)Math.Round(raw)), critical);
		}

		static float Variance()
		{
			float v = CombatConfig.Get(CombatConfig.DamageVariance);
			return 1f + (float)(Roll() * 2 - 1) * v;
		}
	}
}
