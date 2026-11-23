using Google.Protobuf.Protocol;
using Server.Game;
using Xunit;

namespace ServerTests
{
	// 혼절·부활('캐릭터 조작 및 전투' 2-11)과 던전 한 판('저승 던전 구조' 2-6·2-7)
	public class FaintAndDungeonTests : TestWorld
	{
		void Faint(Player p, GameObject attacker) => p.OnDamaged(attacker, p.Stat.MaxHp * 10, false);

		[Fact]
		public void 던전에서_체력_0이면_혼절하고_그_자리에_남는다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			AddPlayer(SpiritMelee, At(-3, 0)); // 전멸이 되지 않게 한 명 더
			Monster m = AddMonster(MonsterHanged, At(1, 0));
			m.AddThreat(p, 10f);

			Faint(p, m);

			Assert.Equal(CreatureState.Dead, p.State);
			Assert.Same(Room, p.Room);
			Assert.Equal(Center.x, p.CellPos.x);
			Assert.False(m.IsEngagedWith(p)); // 쓰러지면 위협 표에서 빠진다
			S_Die die = SessionOf(p).Of<S_Die>().Last();
			Assert.Equal(Cfg(CombatConfig.FaintTime), die.FaintTime, 3);
		}

		[Fact]
		public void 혼절_시간이_지나면_입구에서_체력_절반으로_일어난다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			AddPlayer(SpiritMelee, At(-3, 0));
			Faint(p, null);

			p.FaintEndTick = System.Environment.TickCount64 - 1;
			Room.Update();

			Assert.Equal(CreatureState.Idle, p.State);
			Assert.Equal((int)System.Math.Round(p.Stat.MaxHp * Cfg(CombatConfig.ReviveHpRate)), p.Stat.Hp);
			CellRect spawn = Room.Map.Markers.spawn;
			Assert.InRange(p.CellPos.x, spawn.x - 2, spawn.x + spawn.w + 1);
			Assert.InRange(p.CellPos.y, spawn.y - 2, spawn.y + spawn.h + 1);
		}

		[Fact]
		public void 회혼술은_혼절한_아군만_대상으로_삼는다()
		{
			const int Revive = 85054;
			Player healer = AddPlayer(SpiritHealer, Center);
			Player alive = AddPlayer(SpiritMelee, At(1, 0));
			Player fainted = AddPlayer(SpiritMelee, At(2, 0));
			Faint(fainted, null);

			Skill(healer, Revive, alive);
			Assert.False(healer.IsCasting); // 살아 있는 대상은 안 된다

			Skill(healer, Revive, fainted);
			Assert.True(healer.IsCasting);   // 시전 시간 뒤에 일어난다
			Assert.Equal(fainted.Id, healer.CastTargetId);
		}

		[Fact]
		public void 전원_혼절하면_등불을_쓰고_전원_전량_회복으로_다시_시작한다()
		{
			Player a = AddPlayer(SpiritMelee, Center);
			Player b = AddPlayer(SpiritTank, At(1, 0));

			Faint(a, null);
			Faint(b, null);
			Room.Flush(); // 전멸 처리는 판정이 끝난 뒤 잡으로 돈다

			S_DungeonState state = SessionOf(a).Of<S_DungeonState>().Last();
			Assert.Equal((int)Cfg(CombatConfig.Lanterns) - 1, state.Lanterns);
			Assert.Equal(CreatureState.Idle, a.State);
			Assert.Equal(a.Stat.MaxHp, a.Stat.Hp);
			Assert.Equal(b.Stat.MaxHp, b.Stat.Hp);
			Assert.Equal(b.Stat.MaxSpirit, b.Spirit, 3);
		}

		[Fact]
		public void 등불을_다_쓰고_전멸하면_삼천도로_돌아간다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			int lanterns = (int)Cfg(CombatConfig.Lanterns);

			for (int i = 0; i <= lanterns; i++)
			{
				Faint(p, null);
				Room.Flush();
			}

			Assert.Null(p.Room);
			Assert.Same(RoomManager.Instance.Lobby, p.PendingRoom);
			Assert.Equal(RoomManager.LobbySceneName, SessionOf(p).Of<S_ChangeScene>().Last().SceneName);
		}

		[Fact]
		public void 구간_불씨에_닿으면_체크포인트와_전원_회복_혼절자_부활()
		{
			List<Vector2Int> bonfires = DungeonSpawner.Points(DungeonId, DungeonSpawner.ObjTypeBonfire);
			Assert.NotEmpty(bonfires);
			Vector2Int fire = bonfires[0];

			Player walker = AddPlayer(SpiritMelee, Center);
			Player fainted = AddPlayer(SpiritTank, At(1, 0));
			Faint(fainted, null);
			walker.Stat.Hp = 1;
			walker.AddSpirit(-walker.Stat.MaxSpirit);

			Room.Map.ApplyMove(walker, DungeonSpawner.PickCells(Room.Map, fire, 1, new System.Random(1))[0]);
			Room.Update();

			Assert.Equal(walker.Stat.MaxHp, walker.Stat.Hp);
			Assert.Equal(walker.Stat.MaxSpirit, walker.Spirit, 3);
			Assert.Equal(CreatureState.Idle, fainted.State);
			Assert.Equal(fainted.Stat.MaxHp, fainted.Stat.Hp);
			Assert.True((fainted.CellPos - fire).cellDist <= 2); // 불씨 옆에서 일어난다
		}

		[Fact]
		public void 교전이_끝나고_지연이_지나면_비전투_회복()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			p.Stat.Hp = p.Stat.MaxHp / 2;
			p.LastCombatTick = System.Environment.TickCount64 - (long)(Cfg(CombatConfig.OutOfCombatDelay) * 1000) - 1000;
			int before = p.Stat.Hp;

			Room.Update();
			System.Threading.Thread.Sleep(300);
			Room.Update();

			Assert.True(p.Stat.Hp > before);
		}

		[Fact]
		public void 교전_중에는_비전투_회복이_없다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			Monster m = AddMonster(MonsterHanged, At(5, 5));
			p.Stat.Hp = p.Stat.MaxHp / 2;
			p.LastCombatTick = System.Environment.TickCount64 - 60_000;
			m.AddThreat(p, 1f);
			int before = p.Stat.Hp;

			// 망령 AI가 돌지 않게 위협만 확인하고 룸 갱신 중 플레이어 부분만 본다
			System.Threading.Thread.Sleep(50);
			typeof(GameRoom).GetMethod("UpdateDungeon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
				.Invoke(Room, new object[] { 0.3f });

			Assert.Equal(before, p.Stat.Hp);
		}

		[Fact]
		public void 디버그_채우기는_체력_신력_쿨타임을_초기화한다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			Skill(p, 85024); // 피바람(쿨타임)
			p.Stat.Hp = 1;
			p.AddSpirit(-p.Stat.MaxSpirit);

			Room.HandleDebugCommand(p, new C_DebugCommand() { Command = DebugCommand.DebugRefill });

			Assert.Equal(p.Stat.MaxHp, p.Stat.Hp);
			Assert.Equal(p.Stat.MaxSpirit, p.Spirit, 3);
			Assert.False(p.IsOnCooldown(85024, System.Environment.TickCount64));
		}
	}
}
