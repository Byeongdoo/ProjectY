using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 적 처치 보상. 던전 룸에 있는 플레이어가 각자 굴린다(개인 굴림, 주사위·분배 없음).
	//   장비: drops(characterInfos.dropID) 확률로 countMin~countMax개, 개마다 dropGroups 등급 분포 → 그 등급의 부위를 균등하게 고른다
	//         ('망령(적) 시스템' 2-8, '장비 아이템 시스템' 2-8)
	//   혼백: ROUND(characterLevel.expReward × configs 40011)('장비 아이템 시스템' 2-5)
	// 받은 것은 본인에게만 S_Loot로 알리고 클라가 바로 가방에 넣는다(자동 획득). 저장은 클라 로컬 세이브 몫이다.
	public static class Loot
	{
		public const int SoulItemId = 9500;        // items.itemID 혼백(재화)
		const int ConfigKillSoulRate = 40011;      // 적 처치 혼백 = 기준 경험치 × 이 값

		static readonly Random _random = new Random();

		public static S_Loot Roll(Monster monster)
		{
			S_Loot loot = new S_Loot() { SourceId = monster.Id };

			if (Table.TableManager.CharacterInfos.TryGet(monster.CharacterId, out Table.CharacterInfosRow info) == false)
				return loot;

			// 혼백
			Table.CharacterLevelRow level = Table.TableManager.CharacterLevel.Rows.FirstOrDefault(r => r.group == monster.CharacterId);
			if (level != null)
			{
				int soul = (int)Math.Round(level.expReward * CombatConfig.Get(ConfigKillSoulRate));
				if (soul > 0)
					loot.Items.Add(new ItemCount() { ItemId = SoulItemId, Count = soul });
			}

			// 장비
			if (info.dropID == 0 || Table.TableManager.Drops.TryGet(info.dropID, out Table.DropsRow drop) == false)
				return loot;
			if (_random.NextDouble() >= drop.itemRate)
				return loot;
			if (Table.TableManager.DropGroups.TryGet(drop.dropGroup, out Table.DropGroupsRow group) == false)
				return loot;

			int count = _random.Next(drop.countMin, Math.Max(drop.countMin, drop.countMax) + 1);
			for (int i = 0; i < count; i++)
			{
				int itemId = RollEquipment(group);
				if (itemId == 0)
					continue;
				ItemCount same = loot.Items.FirstOrDefault(x => x.ItemId == itemId);
				if (same != null)
					same.Count++;
				else
					loot.Items.Add(new ItemCount() { ItemId = itemId, Count = 1 });
			}
			return loot;
		}

		// 등급(1 범 ~ 5 신)을 분포대로 고르고, 그 등급 장비 중 부위를 균등하게 고른다
		static int RollEquipment(Table.DropGroupsRow group)
		{
			float[] rates = { group.rateNormal, group.rateHigh, group.rateRare, group.rateEpic, group.rateLegendary };
			float total = rates.Sum();
			if (total <= 0f)
				return 0;

			double pick = _random.NextDouble() * total;
			int grade = rates.Length;
			for (int i = 0; i < rates.Length; i++)
			{
				if (pick < rates[i])
				{
					grade = i + 1;
					break;
				}
				pick -= rates[i];
			}

			List<Table.ItemsRow> candidates = Table.TableManager.Items.Rows.Where(r => r.slot > 0 && r.grade == grade).ToList();
			if (candidates.Count == 0)
				return 0;
			return candidates[_random.Next(candidates.Count)].itemID;
		}
	}
}
