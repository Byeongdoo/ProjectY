using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;

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
			}
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
