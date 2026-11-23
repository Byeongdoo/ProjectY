using System.Collections.Generic;

namespace Server.Game
{
	// 한 번에 스폰된 적 무리(spawnGroups 1회분). 팩 연동과 특수기 동시 시전 제한을 함께 맡는다('망령(적) 시스템' 2-5, 2-5-1).
	public class MonsterPack
	{
		const int ConfigSpecialLimit = 20050;    // 팩 동시 시전 수
		const int ConfigSpecialInterval = 20051; // 다음 특수기 최소 시작 간격(초)

		public List<Monster> Members { get; } = new List<Monster>();

		// 예고 중이거나 판정·투사체가 아직 끝나지 않은 특수기 수
		public int ActiveSpecials { get; private set; }
		long _lastStartTick = long.MinValue / 2;

		// 다음 특수기 시작은 동시 시전 수 미만이고, 이전 시작 후 최소 간격이 지나야 한다.
		// 동시 시전 수가 1이면 "이전 판정·투사체 종료 이후"도 함께 만족한다.
		public bool CanStartSpecial(long now)
		{
			int limit = (int)Table.TableManager.Configs.Get(ConfigSpecialLimit).value[0];
			long interval = (long)(1000 * Table.TableManager.Configs.Get(ConfigSpecialInterval).value[0]);
			return ActiveSpecials < limit && now >= _lastStartTick + interval;
		}

		public void OnSpecialStart(long now)
		{
			ActiveSpecials++;
			_lastStartTick = now;
		}

		public void OnSpecialEnd()
		{
			if (ActiveSpecials > 0)
				ActiveSpecials--;
		}
	}
}
