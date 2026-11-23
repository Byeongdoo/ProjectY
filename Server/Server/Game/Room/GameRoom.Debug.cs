using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Server.Game
{
	// 개발·테스트 명령(C_DebugCommand). 서버 config.json의 debugCommands가 true이고 전투가 있는 던전 룸에서만 받는다.
	public partial class GameRoom
	{
		public void HandleDebugCommand(Player player, C_DebugCommand packet)
		{
			if (ConfigManager.Config.debugCommands == false)
				return;
			if (player == null || player.Room != this || Kind != RoomKind.Dungeon)
				return;

			switch (packet.Command)
			{
				case DebugCommand.DebugSpawnDummy:
					SpawnDummy(player);
					break;
				case DebugCommand.DebugClearDummies:
					ClearDummies();
					break;
				case DebugCommand.DebugRefill:
					Refill(player);
					break;
				case DebugCommand.DebugReload:
					ReloadAndRestartRun(player);
					break;
			}
		}

		// 밸런스 조정용: 서버를 끄지 않고 xlsx → 변환 → 이 명령으로 새 수치를 본다.
		//   1) 테이블 JSON을 다시 읽는다(실패하면 원래 테이블로 되돌리고 아무것도 바꾸지 않는다)
		//   2) 이 던전의 한 판을 새로 시작한다: 망령·설치물 제거 → 등불·불씨·체크포인트 초기화 → 플레이어 스탯을 새 테이블로 다시 계산
		//      (혼절자는 일으키고 체력·신력·상태이상·쿨타임 초기화) → 망령 다시 배치, 있던 허수아비는 같은 칸에 새 수치로
		// 다른 룸(삼천도·다른 던전)도 새 테이블을 읽지만 이미 나와 있는 오브젝트는 그대로다. 생성 코드(Tables.g.cs)가 바뀐 변경은 서버를 다시 빌드해야 한다.
		void ReloadAndRestartRun(Player requester)
		{
			if (ReloadTables() == false)
				return;

			List<Vector2Int> dummyCells = _monsters.Values.OfType<TrainingDummy>().Select(d => d.SpawnCell).ToList();
			foreach (Monster m in _monsters.Values.ToList())
				LeaveGame(m.Id);
			foreach (Field f in _fields.Values.ToList())
				LeaveGame(f.Id);

			_bonfires = null;      // 불씨 위치도 levelObjects에서 다시 읽는다
			_arenaBonfires = null;
			ResetDungeonRun();

			foreach (Player p in _players.Values.ToList())
			{
				if (p.State == CreatureState.Dead)
					Revive(p, 1f, true, null);
				if (p.IsCasting)
					CancelCast(p);
				CancelChannel(p);
				p.Status.RemoveAll();
				p.ApplyStats();
				Broadcast(new S_ChangeStat() { ObjectId = p.Id, StatInfo = p.Stat });
				Broadcast(new S_ChangeHp() { ObjectId = p.Id, Hp = p.Stat.Hp });
				p.Session?.Send(new S_ChangeSpirit() { Spirit = p.Stat.Spirit, MaxSpirit = p.Stat.MaxSpirit });
			}
			Broadcast(DungeonState());

			SpawnDungeonMonsters();
			foreach (Vector2Int cell in dummyCells)
			{
				TrainingDummy dummy = ObjectManager.Instance.Add<TrainingDummy>();
				dummy.InitDummy(cell);
				EnterGame(dummy);
			}
			Console.WriteLine($"[{SceneName}] 디버그: 테이블 다시 읽음, 한 판 새로 시작 by {requester.Info.Name}");
		}

		static bool ReloadTables()
		{
			PropertyInfo[] tables = typeof(Table.TableManager).GetProperties(BindingFlags.Public | BindingFlags.Static);
			Dictionary<PropertyInfo, object> snapshot = tables.ToDictionary(p => p, p => p.GetValue(null));
			try
			{
				Table.TableManager.LoadAll(new FileTableReader(ConfigManager.Config.dataPath));
				return true;
			}
			catch (Exception e)
			{
				foreach (KeyValuePair<PropertyInfo, object> kv in snapshot)
					kv.Key.SetValue(null, kv.Value);
				Console.Error.WriteLine($"디버그: 테이블 다시 읽기 실패 — 원래 테이블을 유지한다. {e.Message}");
				return false;
			}
		}

		const int ConfigTestMonsterAttackRate = 99004; // 테스트용 망령 공격력 배율(1 = 실제 밸런스)
		const int ConfigTestMonsterHpRate = 99005;     // 테스트용 망령 체력 배율(1 = 실제 밸런스)

		// 혼자·낮은 레벨로 PC 전투를 시험할 수 있게 던전 망령을 약하게 한다. 특수기 피해도 공격력 기준이라 같이 줄어든다
		void ApplyTestMonsterScale(Monster monster)
		{
			float attackRate = CombatConfig.Get(ConfigTestMonsterAttackRate);
			float hpRate = CombatConfig.Get(ConfigTestMonsterHpRate);
			if (attackRate == 1f && hpRate == 1f)
				return;
			monster.Stat.Attack = Math.Max(1, (int)Math.Round(monster.Stat.Attack * attackRate));
			monster.Stat.MaxHp = Math.Max(1, (int)Math.Round(monster.Stat.MaxHp * hpRate));
			monster.Stat.Hp = monster.Stat.MaxHp;
		}

		// 바라보는 앞 칸(막혔으면 가장 가까운 빈 칸)에 허수아비
		void SpawnDummy(Player player)
		{
			List<Vector2Int> cells = DungeonSpawner.PickCells(Map, player.GetFrontCellPos(), 1, _random);
			if (cells.Count == 0)
				return;

			TrainingDummy dummy = ObjectManager.Instance.Add<TrainingDummy>();
			dummy.InitDummy(cells[0]);
			EnterGame(dummy);
			Console.WriteLine($"[{SceneName}] 디버그: 허수아비 소환 ({cells[0].x}, {cells[0].y}) by {player.Info.Name}");
		}

		void ClearDummies()
		{
			List<TrainingDummy> dummies = _monsters.Values.OfType<TrainingDummy>().ToList();
			foreach (TrainingDummy d in dummies)
				LeaveGame(d.Id);
			Console.WriteLine($"[{SceneName}] 디버그: 허수아비 {dummies.Count}개 정리");
		}

		// 체력·신력을 채우고 쿨타임·공용 재사용 대기·평타 간격을 초기화한다(혼절 중이면 그 자리에서 일으킨다)
		void Refill(Player player)
		{
			if (player.State == CreatureState.Dead)
				Revive(player, 1f, true, null);

			int healed = player.Stat.MaxHp - player.Stat.Hp;
			player.Stat.Hp = player.Stat.MaxHp;
			Broadcast(new S_ChangeHp() { ObjectId = player.Id, Hp = player.Stat.Hp, Amount = healed, Heal = healed > 0 });
			player.AddSpirit(player.Stat.MaxSpirit);
			player.ClearCooldowns();
		}
	}
}
