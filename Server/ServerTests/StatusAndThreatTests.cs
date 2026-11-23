using Google.Protobuf.Protocol;
using Server.Game;
using Xunit;

namespace ServerTests
{
	// 상태이상('스킬 시스템' 2-5)과 위협('캐릭터 조작 및 전투' 2-9)
	public class StatusAndThreatTests : TestWorld
	{
		// ---- 상태이상 ----

		[Fact]
		public void 다시_걸면_지속시간을_갱신하고_최대_중첩까지_쌓는다()
		{
			TrainingDummy d = AddDummy(Center);
			int max = Table.TableManager.StatusEffects.Get(StatusId.Burn).maxStack;

			for (int i = 0; i < max + 2; i++)
				d.Status.Apply(StatusId.Burn, 5f, null);

			Assert.Equal(max, d.Status.Get(StatusId.Burn).Stack);
		}

		[Fact]
		public void 보스는_기절이_절반만_걸린다()
		{
			Monster boss = AddMonster(BossMokgui, Center);
			TrainingDummy normal = AddDummy(At(3, 0));
			Assert.True(boss.IsBoss);

			long now = System.Environment.TickCount64;
			boss.Status.Apply(StatusId.Stun, 2f, null);
			normal.Status.Apply(StatusId.Stun, 2f, null);

			long bossLeft = boss.Status.Get(StatusId.Stun).ExpireTick - now;
			long normalLeft = normal.Status.Get(StatusId.Stun).ExpireTick - now;
			Assert.InRange(bossLeft, (long)(normalLeft * Cfg(CombatConfig.BossControlRate)) - 50, (long)(normalLeft * Cfg(CombatConfig.BossControlRate)) + 50);
		}

		[Fact]
		public void 시간이_지나면_풀리고_알린다()
		{
			Player watcher = AddPlayer(SpiritMelee, At(-2, 0));
			TrainingDummy d = AddDummy(Center);
			d.Status.Apply(StatusId.Empower, 0.05f, null);

			System.Threading.Thread.Sleep(100);
			d.Status.Update();

			Assert.False(d.Status.Has(StatusId.Empower));
			S_ChangeStatus last = SessionOf(watcher).Of<S_ChangeStatus>().Last(s => s.ObjectId == d.Id);
			Assert.Equal(0f, last.Duration);
		}

		[Fact]
		public void 신속은_이동_속도를_올리고_알린다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			float before = p.Stat.Speed;

			p.Status.Apply(StatusId.Haste, 5f, p);

			float value = Table.TableManager.StatusEffects.Get(StatusId.Haste).value;
			Assert.Equal(before * (1f + value), p.Stat.Speed, 3);
			Assert.Contains(SessionOf(p).Of<S_ChangeStat>(), s => s.ObjectId == p.Id);
		}

		// ---- 위협 ----

		[Fact]
		public void 피해_위협은_원혼_위협_배율을_곱한다()
		{
			Player tank = AddPlayer(SpiritTank, Center);
			Player melee = AddPlayer(SpiritMelee, Center);
			Monster m = AddMonster(MonsterHanged, At(1, 0));

			int hp = m.Stat.Hp;
			Combat.Damage(tank, m, 1f);
			int tankDealt = hp - m.Stat.Hp;
			hp = m.Stat.Hp;
			Combat.Damage(melee, m, 1f);
			int meleeDealt = hp - m.Stat.Hp;

			float perDamage = Cfg(CombatConfig.ThreatPerDamage);
			Assert.Equal(tankDealt * perDamage * Table.TableManager.Spirits.Get(SpiritTank).threatRate, ThreatOf(m, tank), 3);
			Assert.Equal(meleeDealt * perDamage, ThreatOf(m, melee), 3);
		}

		[Fact]
		public void 대상은_설정_배율_이상_높아야_바뀐다()
		{
			Player a = AddPlayer(SpiritMelee, Center);
			Player b = AddPlayer(SpiritMelee, Center);
			Monster m = AddMonster(MonsterHanged, At(1, 0));
			float rate = Cfg(CombatConfig.ThreatSwitchRate);

			m.AddThreat(a, 100f);
			Assert.Same(a, TargetOf(m));

			m.AddThreat(b, 100f * rate - 1f); // 배율 바로 아래
			Assert.Same(a, TargetOf(m));

			m.AddThreat(b, 2f);              // 배율 이상
			Assert.Same(b, TargetOf(m));
		}

		[Fact]
		public void 회복_위협은_교전_중인_망령에게만()
		{
			Player tank = AddPlayer(SpiritTank, Center);
			Player healer = AddPlayer(SpiritHealer, At(-1, 0));
			Monster engaged = AddMonster(MonsterHanged, At(1, 0));
			Monster idle = AddMonster(MonsterHanged, At(4, 4));
			engaged.AddThreat(tank, 10f);
			tank.Stat.Hp = tank.Stat.MaxHp - 100;

			Combat.Heal(healer, tank, 0.5f); // 50 정도: 모자란 100 안쪽
			int healed = SessionOf(tank).Of<S_ChangeHp>().Last(c => c.ObjectId == tank.Id).Amount;

			Assert.Equal(healed * Cfg(CombatConfig.ThreatPerHeal), ThreatOf(engaged, healer), 3);
			Assert.Equal(-1f, ThreatOf(idle, healer)); // 교전하지 않은 망령은 표에 없다
		}

		[Fact]
		public void 허수아비는_위협_표에_오르지_않는다()
		{
			Player p = AddPlayer(SpiritMelee, Center, MoveDir.Right);
			TrainingDummy d = AddDummy(At(1, 0));

			Basic(p);

			Assert.False(d.IsEngaged);
		}

		[Fact]
		public void 허수아비는_쓰러지지_않는다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy d = AddDummy(At(1, 0));

			d.OnDamaged(p, d.Stat.MaxHp * 2, false);

			Assert.Equal(d.Stat.MaxHp, d.Stat.Hp);
			Assert.Same(Room, d.Room);
		}
	}
}
