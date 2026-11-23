using Google.Protobuf.Protocol;
using Server.Game;
using Xunit;

namespace ServerTests
{
	// 평타('캐릭터 조작 및 전투' 2-4, '원혼(직업) 시스템' 2-4)와 원혼 스킬('스킬 시스템' 2-3~2-10).
	// 피해량 자체는 DamageFormulaTests가 본다. 여기서는 누가 맞는지·자원·시간 규칙을 본다.
	public class SkillTests : TestWorld
	{
		const int SwordDance = 85021;    // 칼춤: 즉시 1칸, 신력
		const int Behead = 85022;        // 참수: 단일 적 2칸, 처형
		const int BloodWind = 85024;     // 피바람: 즉시 자신, 쿨타임
		const int WarCry = 85011;        // 호령: 즉시 3칸, 도발
		const int ShieldBash = 85013;    // 방패 밀치기: 방향 1칸, 밀어내기·기절
		const int Intercept = 85014;     // 대신 맞기: 단일 아군, 수호
		const int PierceArrow = 85031;   // 관통 화살: 방향 6칸
		const int Trap = 85033;          // 덫 놓기
		const int Backstep = 85034;      // 물러서기
		const int Needle = 85051;        // 침 놓기: 단일 아군 회복
		const int Cleanse = 85053;       // 해독

		static bool Hit(TrainingDummy d) => d.Stat.Hp < d.Stat.MaxHp;

		// ---- 평타 ----

		[Fact]
		public void 망나니_평타는_앞과_좌우_앞_3칸을_벤다()
		{
			Player p = AddPlayer(SpiritMelee, Center, MoveDir.Right);
			TrainingDummy front = AddDummy(At(1, 0));
			TrainingDummy frontUp = AddDummy(At(1, 1));
			TrainingDummy frontDown = AddDummy(At(1, -1));
			TrainingDummy behind = AddDummy(At(-1, 0));

			Basic(p);

			Assert.True(Hit(front));
			Assert.True(Hit(frontUp));
			Assert.True(Hit(frontDown));
			Assert.False(Hit(behind));
		}

		[Fact]
		public void 무명장수_평타는_앞_칸만()
		{
			Player p = AddPlayer(SpiritTank, Center, MoveDir.Right);
			TrainingDummy front = AddDummy(At(1, 0));
			TrainingDummy side = AddDummy(At(1, 1));

			Basic(p);

			Assert.True(Hit(front));
			Assert.False(Hit(side));
		}

		[Fact]
		public void 원거리_평타는_직선에서_가장_가까운_적_하나()
		{
			Player p = AddPlayer(SpiritRanged, Center, MoveDir.Right);
			TrainingDummy near = AddDummy(At(2, 0));
			TrainingDummy far = AddDummy(At(4, 0));

			Basic(p);

			Assert.True(Hit(near));
			Assert.False(Hit(far));
			Assert.Equal(near.Id, SessionOf(p).Of<S_Skill>().Last().Info.TargetId);
		}

		[Fact]
		public void 원거리_평타는_사거리_밖을_치지_않는다()
		{
			Player p = AddPlayer(SpiritRanged, Center, MoveDir.Right);
			int range = Table.TableManager.Spirits.Get(SpiritRanged).basicRange;
			TrainingDummy outOfRange = AddDummy(At(range + 1, 0));

			Basic(p);

			Assert.False(Hit(outOfRange));
		}

		[Fact]
		public void 평타_적중_신력은_여러_명을_맞혀도_한_번()
		{
			Player p = AddPlayer(SpiritMelee, Center, MoveDir.Right);
			AddDummy(At(1, 0));
			AddDummy(At(1, 1));
			AddDummy(At(1, -1));
			p.AddSpirit(-p.Stat.MaxSpirit);

			Basic(p);

			Assert.Equal(Cfg(CombatConfig.SpiritOnBasicHit), p.Spirit, 3);
		}

		[Fact]
		public void 평타는_간격_안에_다시_나가지_않는다()
		{
			Player p = AddPlayer(SpiritMelee, Center, MoveDir.Right);
			TrainingDummy d = AddDummy(At(1, 0));

			Basic(p);
			int after1 = d.Stat.Hp;
			Basic(p);

			Assert.Equal(after1, d.Stat.Hp);
		}

		// ---- 자원·시간 ----

		[Fact]
		public void 신력_계열은_신력을_쓰고_공용_재사용_대기가_다음_스킬을_막는다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy adjacent = AddDummy(At(1, 1));
			TrainingDummy twoAway = AddDummy(At(2, 0));
			float before = p.Spirit;

			Skill(p, SwordDance);

			Assert.True(Hit(adjacent));
			Assert.False(Hit(twoAway)); // 칼춤은 주변 1칸
			Assert.Equal(before - Table.TableManager.SpiritSkills.Get(SwordDance).spiritCost, p.Spirit, 3);

			// 공용 재사용 대기 중(초기화하지 않고 바로)
			int hp = twoAway.Stat.Hp;
			Room.HandleSkill(p, new C_Skill() { Info = new SkillInfo() { SkillId = Behead, TargetId = twoAway.Id } });
			Assert.Equal(hp, twoAway.Stat.Hp);
		}

		[Fact]
		public void 신력이_모자라면_쓰지_않는다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy d = AddDummy(At(1, 0));
			p.AddSpirit(-p.Stat.MaxSpirit);

			Skill(p, SwordDance);

			Assert.False(Hit(d));
		}

		[Fact]
		public void 쿨타임_계열은_쿨타임을_걸고_자신에게_상태이상()
		{
			Player p = AddPlayer(SpiritMelee, Center);

			Skill(p, BloodWind);

			Assert.True(p.Status.Has(StatusId.Frenzy));
			Assert.True(p.Status.Has(StatusId.Vulnerable)); // 이득과 대가를 같이
			Assert.True(p.IsOnCooldown(BloodWind, System.Environment.TickCount64));
			Assert.Equal(p.Stat.MaxSpirit, p.Spirit, 3); // 신력은 쓰지 않는다
		}

		[Fact]
		public void 참수는_체력이_낮은_대상에게_배율()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy full = AddDummy(At(1, 0));
			TrainingDummy low = AddDummy(At(0, 1));
			low.Stat.Hp = (int)(low.Stat.MaxHp * Cfg(CombatConfig.ExecuteHpRate)) - 1;
			int fullHp = full.Stat.Hp, lowHp = low.Stat.Hp;

			Skill(p, Behead, full);
			Skill(p, Behead, low);

			float coef = Table.TableManager.SpiritSkills.Get(Behead).coefficient;
			float mult = Cfg(CombatConfig.ExecuteMultiplier);
			int normal = Damage(full, fullHp);
			Assert.Equal((int)System.Math.Round(p.AttackPower * coef * (1f - full.Defence)), normal);
			Assert.Equal((int)System.Math.Round(p.AttackPower * coef * mult * (1f - low.Defence)), Damage(low, lowHp));
		}

		[Fact]
		public void 단일_적_스킬은_사거리_밖이면_발동하지_않는다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			int range = Table.TableManager.SpiritSkills.Get(Behead).range;
			TrainingDummy d = AddDummy(At(range + 1, 0));
			float spirit = p.Spirit;

			Skill(p, Behead, d);

			Assert.False(Hit(d));
			Assert.Equal(spirit, p.Spirit, 3);
		}

		// ---- 범위·대상 ----

		[Fact]
		public void 호령은_주변_범위_안_적에게_피해와_도발()
		{
			Player p = AddPlayer(SpiritTank, Center);
			int range = Table.TableManager.SpiritSkills.Get(WarCry).range;
			TrainingDummy inside = AddDummy(At(range, range));   // 대각선도 칸 수로 센다
			TrainingDummy outside = AddDummy(At(range + 1, 0));

			Skill(p, WarCry);

			Assert.True(Hit(inside));
			Assert.True(inside.Status.Has(StatusId.Taunt));
			Assert.Same(p, inside.Status.Get(StatusId.Taunt).Source);
			Assert.False(Hit(outside));
		}

		[Fact]
		public void 관통_화살은_직선_위_적을_모두_맞힌다()
		{
			Player p = AddPlayer(SpiritRanged, Center, MoveDir.Right);
			int range = Table.TableManager.SpiritSkills.Get(PierceArrow).range;
			TrainingDummy a = AddDummy(At(1, 0));
			TrainingDummy b = AddDummy(At(range, 0));
			TrainingDummy beyond = AddDummy(At(range + 1, 0));
			TrainingDummy offLine = AddDummy(At(2, 1));

			Skill(p, PierceArrow);

			Assert.True(Hit(a));
			Assert.True(Hit(b));
			Assert.False(Hit(beyond));
			Assert.False(Hit(offLine));
		}

		[Fact]
		public void 방패_밀치기는_밀어내고_기절시킨다()
		{
			Player p = AddPlayer(SpiritTank, Center, MoveDir.Right);
			TrainingDummy d = AddDummy(At(1, 0));
			int push = Table.TableManager.SpiritSkills.Get(ShieldBash).knockback;

			Skill(p, ShieldBash);

			Assert.True(Hit(d));
			Assert.Equal(At(1 + push, 0).x, d.CellPos.x);
			Assert.Equal(Center.y, d.CellPos.y);
			Assert.True(d.Status.Has(StatusId.Stun));
		}

		[Fact]
		public void 밀어내기는_다른_망령에_막히면_거기서_멈춘다()
		{
			Player p = AddPlayer(SpiritTank, Center, MoveDir.Right);
			TrainingDummy d = AddDummy(At(1, 0));
			AddDummy(At(3, 0)); // 두 칸째를 막는다

			Skill(p, ShieldBash);

			Assert.Equal(At(2, 0).x, d.CellPos.x);
		}

		[Fact]
		public void 대신_맞기는_아군에게_수호를_건다()
		{
			Player tank = AddPlayer(SpiritTank, Center);
			Player ally = AddPlayer(SpiritMelee, At(2, 0));

			Skill(tank, Intercept, ally);

			Assert.True(ally.Status.Has(StatusId.Guard));
			Assert.Same(tank, ally.Status.Get(StatusId.Guard).Source);
		}

		[Fact]
		public void 침_놓기는_아군을_회복한다()
		{
			Player healer = AddPlayer(SpiritHealer, Center);
			Player ally = AddPlayer(SpiritMelee, At(2, 0));
			ally.Stat.Hp = 1;

			Skill(healer, Needle, ally);

			float coef = Table.TableManager.SpiritSkills.Get(Needle).coefficient;
			Assert.Equal(1 + (int)System.Math.Round(healer.AttackPower * coef), ally.Stat.Hp);
		}

		[Fact]
		public void 해독은_해로운_상태만_지운다()
		{
			Player healer = AddPlayer(SpiritHealer, Center);
			Player ally = AddPlayer(SpiritMelee, At(1, 0));
			ally.Status.Apply(StatusId.Root, 5f, null);
			ally.Status.Apply(StatusId.Vulnerable, 5f, null);
			ally.Status.Apply(StatusId.Empower, 5f, null);

			Skill(healer, Cleanse, ally);

			Assert.False(ally.Status.Has(StatusId.Root));
			Assert.False(ally.Status.Has(StatusId.Vulnerable));
			Assert.True(ally.Status.Has(StatusId.Empower));
		}

		[Fact]
		public void 덫은_밟은_망령을_결박한다()
		{
			Player p = AddPlayer(SpiritRanged, Center);
			Skill(p, Trap);
			Monster m = AddMonster(MonsterHanged, At(1, 0));

			// 덫은 시전자 발밑에 깔린다. 시전자가 비켜선 뒤(망령은 플레이어가 선 칸에 못 들어간다) 망령이 덫 칸에 들어오면 한 틱
			Room.Map.ApplyMove(p, At(-3, 0));
			Assert.True(Room.Map.ApplyMove(m, Center));
			Room.Update();

			Assert.True(m.Status.Has(StatusId.Root));
			Assert.Same(p, m.Status.Get(StatusId.Root).Source);
		}

		[Fact]
		public void 물러서기는_바라보는_반대로_움직이고_방향은_그대로()
		{
			Player p = AddPlayer(SpiritRanged, Center, MoveDir.Right);
			int cells = Table.TableManager.SpiritSkills.Get(Backstep).range;

			Skill(p, Backstep);

			Assert.Equal(Center.x - cells, p.CellPos.x);
			Assert.Equal(MoveDir.Right, p.Dir);
		}

		[Fact]
		public void 기절하면_평타도_스킬도_못_쓰고_움직이지_못한다()
		{
			Player p = AddPlayer(SpiritMelee, Center, MoveDir.Right);
			TrainingDummy d = AddDummy(At(1, 0));
			p.Status.Apply(StatusId.Stun, 5f, null);

			Basic(p);
			Skill(p, SwordDance);
			Room.HandleMove(p, new C_Move() { PosInfo = new PositionInfo() { State = CreatureState.Moving, MoveDir = MoveDir.Up, PosX = Center.x, PosY = Center.y + 1 } });

			Assert.False(Hit(d));
			Assert.Equal(Center.y, p.CellPos.y);
		}
	}
}
