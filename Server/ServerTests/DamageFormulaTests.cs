using Google.Protobuf.Protocol;
using Server.Game;
using Xunit;

namespace ServerTests
{
	// 피해·회복 공식('캐릭터 조작 및 전투' 2-7)과 받는 쪽 배율('원혼(직업) 시스템' 2-4, '스킬 시스템' 2-5)
	public class DamageFormulaTests : TestWorld
	{
		// 서버(Combat.Damage)와 같은 순서·형식으로 계산한 기대값
		static int Expected(GameObject attacker, GameObject target, float coef, float extra = 1f, bool crit = false, double varianceRoll = 0.5)
		{
			float raw = attacker.AttackPower * coef * extra;
			if (crit)
				raw *= attacker.CriticalMultiplier;
			raw *= 1f - System.Math.Min(target.Defence, Cfg(CombatConfig.DefenceCap));
			raw *= 1f + (float)(varianceRoll * 2 - 1) * Cfg(CombatConfig.DamageVariance);
			return System.Math.Max((int)Cfg(CombatConfig.MinDamage), (int)System.Math.Round(raw));
		}

		[Fact]
		public void 기본_피해는_공격력x계수x방어율감소()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy d = AddDummy(At(1, 0));
			int hp = d.Stat.Hp;

			Combat.Damage(p, d, 1f);

			// 망나니 Lv1: 공격력 100 × 1.35, 허수아비 방어율 configs 99002
			Assert.Equal(135, p.Stat.Attack);
			Assert.Equal(Expected(p, d, 1f), Damage(d, hp));
		}

		[Fact]
		public void 치명타는_치명타_배율을_곱한다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy d = AddDummy(At(1, 0));
			int hp = d.Stat.Hp;

			Rolls(0.0, 0.5); // 치명타 확률보다 작은 값 → 치명타, 편차 0
			Combat.Damage(p, d, 1f);

			Assert.Equal(Expected(p, d, 1f, crit: true), Damage(d, hp));
			Assert.True(Damage(d, hp) > Expected(p, d, 1f));
		}

		[Theory]
		[InlineData(0.0)]   // −편차
		[InlineData(0.999)] // +편차에 가깝게
		public void 편차는_설정값_범위_안(double roll)
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy d = AddDummy(At(1, 0));
			int hp = d.Stat.Hp;

			Rolls(0.999999, roll);
			Combat.Damage(p, d, 1f);

			int dealt = Damage(d, hp);
			Assert.Equal(Expected(p, d, 1f, varianceRoll: roll), dealt);
			float baseline = p.AttackPower * (1f - d.Defence);
			float v = Cfg(CombatConfig.DamageVariance);
			Assert.InRange(dealt, (int)System.Math.Floor(baseline * (1 - v)), (int)System.Math.Ceiling(baseline * (1 + v)));
		}

		class ArmoredDummy : TrainingDummy
		{
			public override float Defence => 0.95f;
		}

		[Fact]
		public void 방어율은_상한에서_멈춘다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			ArmoredDummy d = ObjectManager.Instance.Add<ArmoredDummy>();
			d.InitDummy(At(1, 0));
			Room.EnterGame(d);
			int hp = d.Stat.Hp;

			Combat.Damage(p, d, 1f);

			float cap = Cfg(CombatConfig.DefenceCap);
			Assert.Equal((int)System.Math.Round(p.AttackPower * (1f - cap)), Damage(d, hp));
		}

		[Fact]
		public void 피해는_최소값_아래로_내려가지_않는다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy d = AddDummy(At(1, 0));
			int hp = d.Stat.Hp;

			Combat.Damage(p, d, 0.0001f);

			Assert.Equal((int)Cfg(CombatConfig.MinDamage), Damage(d, hp));
		}

		[Fact]
		public void 무명장수는_받는_피해_배율이_적용된다()
		{
			Player tank = AddPlayer(SpiritTank, Center);
			Player melee = AddPlayer(SpiritMelee, Center);
			Monster m = AddMonster(MonsterHanged, At(1, 0));
			int tankHp = tank.Stat.Hp, meleeHp = melee.Stat.Hp;

			Combat.Damage(m, tank, 1f);
			Combat.Damage(m, melee, 1f);

			int raw = Expected(m, melee, 1f); // 망나니 받는 피해 ×1.0
			Assert.Equal(raw, Damage(melee, meleeHp));
			float rate = Table.TableManager.Spirits.Get(SpiritTank).damageTakenRate;
			Assert.Equal((int)System.Math.Round(Expected(m, tank, 1f) * rate), Damage(tank, tankHp));
		}

		[Fact]
		public void 취약은_받는_피해를_늘리고_버팀은_줄인다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			TrainingDummy plain = AddDummy(At(1, 0));
			TrainingDummy weak = AddDummy(At(1, 1));
			Player braced = AddPlayer(SpiritMelee, At(-1, 0));
			Monster m = AddMonster(MonsterHanged, At(-2, 0));

			weak.Status.Apply(StatusId.Vulnerable, 5f, p);
			braced.Status.Apply(StatusId.Brace, 5f, braced);
			int plainHp = plain.Stat.Hp, weakHp = weak.Stat.Hp, bracedHp = braced.Stat.Hp;

			Combat.Damage(p, plain, 1f);
			Combat.Damage(p, weak, 1f);
			Combat.Damage(m, braced, 1f);

			float vuln = Table.TableManager.StatusEffects.Get(StatusId.Vulnerable).value;
			float brace = Table.TableManager.StatusEffects.Get(StatusId.Brace).value;
			Assert.Equal((int)System.Math.Round(Damage(plain, plainHp) * (1f + vuln)), Damage(weak, weakHp));
			Assert.Equal((int)System.Math.Round(Expected(m, braced, 1f) * (1f - brace)), Damage(braced, bracedHp));
		}

		[Fact]
		public void 수호는_피해_일부를_시전자가_대신_받는다()
		{
			Player tank = AddPlayer(SpiritTank, Center);
			Player ally = AddPlayer(SpiritMelee, At(1, 0));
			Monster m = AddMonster(MonsterHanged, At(2, 0));
			ally.Status.Apply(StatusId.Guard, 5f, tank);
			int tankHp = tank.Stat.Hp, allyHp = ally.Stat.Hp;

			Combat.Damage(m, ally, 1f);

			int total = Expected(m, ally, 1f);
			int shared = (int)System.Math.Round(total * Table.TableManager.StatusEffects.Get(StatusId.Guard).value);
			Assert.Equal(total - shared, Damage(ally, allyHp));
			float tankRate = Table.TableManager.Spirits.Get(SpiritTank).damageTakenRate;
			Assert.Equal((int)System.Math.Round(shared * tankRate), Damage(tank, tankHp));
		}

		[Fact]
		public void 회복은_최대_체력을_넘지_않고_방어율과_무관()
		{
			Player healer = AddPlayer(SpiritHealer, Center);
			Player ally = AddPlayer(SpiritMelee, At(1, 0));
			ally.Stat.Hp = ally.Stat.MaxHp - 10;

			Combat.Heal(healer, ally, 2.2f);

			Assert.Equal(ally.Stat.MaxHp, ally.Stat.Hp);
			S_ChangeHp last = SessionOf(ally).Of<S_ChangeHp>().Last(c => c.ObjectId == ally.Id);
			Assert.True(last.Heal);
			Assert.Equal(10, last.Amount); // 실제로 찬 양만 알린다
		}

		[Fact]
		public void 강화는_공격력을_올린다()
		{
			Player p = AddPlayer(SpiritMelee, Center);
			float before = p.AttackPower;

			p.Status.Apply(StatusId.Empower, 5f, p);

			float value = Table.TableManager.StatusEffects.Get(StatusId.Empower).value;
			Assert.Equal(before * (1f + value), p.AttackPower, 3);
		}
	}
}
