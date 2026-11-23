using System;

namespace Server.Game
{
	// 피해 판정 공식('캐릭터 조작 및 전투' 2-7). 플레이어·망령이 같은 규칙을 쓴다.
	//   기본 피해 = 공격력 × 스킬 계수
	//   치명타   = 난수 < 치명타 확률 이면 × 치명타 배율
	//   피해 감소 = × (1 − MIN(방어율, 방어율 상한))
	//   받는 피해 배율(원혼) = 방어율 상한 뒤에 곱한다('원혼(직업) 시스템' 2-4)
	//   편차     = × (1 ± 피해 편차)
	//   최종 피해 = ROUND(위 결과), 최소 피해 이상
	public static class Combat
	{
		const int ConfigDefenceCap = 20001;     // 방어율 상한
		const int ConfigDamageVariance = 20002; // 피해 편차
		const int ConfigDefaultCritMul = 20003; // 기본 치명타 배율(criticalAttack 미지정 시)
		const int ConfigMinDamage = 20004;      // 최소 피해

		static readonly Random _random = new Random();

		static float Config(int id)
		{
			return Table.TableManager.Configs.Get(id).value[0];
		}

		public static int CalcDamage(GameObject attacker, GameObject target, float coef)
		{
			float damage = attacker.Stat.Attack * coef;

			if (_random.NextDouble() < attacker.Critical)
				damage *= attacker.CriticalAttack > 0 ? attacker.CriticalAttack : Config(ConfigDefaultCritMul);

			damage *= 1f - Math.Min(target.Defence, Config(ConfigDefenceCap));
			damage *= target.DamageTakenRate;

			float variance = Config(ConfigDamageVariance);
			damage *= 1f + (float)(_random.NextDouble() * 2 - 1) * variance;

			int minDamage = (int)Math.Round(Config(ConfigMinDamage));
			return Math.Max(minDamage, (int)Math.Round(damage));
		}

		// 피해를 계산해 대상에게 준다. attacker는 스탯·위협의 주인(화살이면 쏜 플레이어)이다.
		public static void Hit(GameObject attacker, GameObject target, float coef)
		{
			if (attacker == null || target == null || target.Room == null)
				return;
			target.OnDamaged(attacker, CalcDamage(attacker, target, coef));
		}
	}
}
