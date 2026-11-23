using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 저승 던전 전투 판정(서버 권위). 클라는 입력(C_Skill)만 보내고 결과(S_Skill·S_ChangeHp·S_ChangeStatus…)로 연출한다.
	//   평타: '캐릭터 조작 및 전투' 2-4, '원혼(직업) 시스템' 2-4
	//   원혼 스킬: '스킬 시스템' 2-2~2-10 (spiritSkills 시트가 대상 타입·범위·자원·계수·상태이상을 정한다)
	public partial class GameRoom
	{
		public void HandleSkill(Player player, C_Skill skillPacket)
		{
			if (player == null || player.Room != this || skillPacket.Info == null)
				return;

			// 삼천도는 전투가 없다('삼천도(로비) 시스템' 2-4)
			if (Kind == RoomKind.Lobby)
				return;

			if (player.CanAct == false || player.IsCasting || player.IsChanneling)
				return;

			if (skillPacket.Info.SkillId == Combat.BasicSkillId)
				BasicAttack(player);
			else
				UseSpiritSkill(player, skillPacket.Info.SkillId, skillPacket.Info.TargetId);
		}

		public void HandleCancelSkill(Player player)
		{
			if (player == null || player.Room != this || player.IsCasting == false)
				return;
			CancelCast(player);
		}

		// ---- 평타 ----

		// Space. 간격 = 평타 간격(configs 20032) / 공격 속도. 적중하면 신력 +configs 20013(한 번 휘두를 때 한 번).
		//   근접(사거리 1): 바라보는 앞 칸을 가운데로 좌우 45도씩 basicWidth칸(망나니 3칸)
		//   원거리(사거리 2+): 바라보는 방향 직선에서 가장 가까운 망령(벽에서 멈춘다)
		void BasicAttack(Player player)
		{
			long now = Environment.TickCount64;
			if (player.NextBasicTick > now)
				return;
			player.NextBasicTick = now + (long)(1000 * CombatConfig.Get(CombatConfig.BasicInterval) / Math.Max(0.05f, player.AttackSpeed));

			List<Monster> targets = new List<Monster>();
			int lineTargetId = 0;
			if (player.BasicRange <= 1)
			{
				int half = (player.BasicWidth - 1) / 2;
				for (int step = -half; step <= half; step++)
				{
					MoveDir dir = GameObject.RotateDir(player.Dir, step);
					if (Map.Find(player.GetFrontCellPos(dir)) is Monster m && m.IsAlive)
						targets.Add(m);
				}
			}
			else
			{
				Monster m = LineMonsters(player.CellPos, player.Dir, player.BasicRange).FirstOrDefault();
				if (m != null)
				{
					targets.Add(m);
					lineTargetId = m.Id;
				}
			}

			Broadcast(new S_Skill() { ObjectId = player.Id, Info = new SkillInfo() { SkillId = Combat.BasicSkillId, TargetId = lineTargetId } });

			foreach (Monster m in targets)
				Combat.Damage(player, m, player.BasicCoef);
			if (targets.Count > 0)
				player.AddSpirit(CombatConfig.Get(CombatConfig.SpiritOnBasicHit));
		}

		// ---- 원혼 스킬 ----

		void UseSpiritSkill(Player player, int skillId, int targetId)
		{
			if (Table.TableManager.Spirits.TryGet(player.Info.SpiritId, out Table.SpiritsRow spirit) == false)
				return;
			if (spirit.skillIDs.Contains(skillId) == false)
				return;
			Table.SpiritSkillsRow skill = Table.TableManager.SpiritSkills.Get(skillId);

			long now = Environment.TickCount64;
			if (player.GlobalCooldownTick > now)
				return;
			if (skill.gate == (int)SkillGate.Cooldown && player.IsOnCooldown(skillId, now))
				return;
			if (skill.gate == (int)SkillGate.Spirit && player.Spirit < SpiritCost(player, skill))
				return;

			GameObject target = null;
			if (skill.targetType == (int)SkillTargetType.Enemy || skill.targetType == (int)SkillTargetType.Ally)
			{
				target = ResolveTarget(player, skill, targetId);
				if (target == null)
					return;
				FaceTarget(player, target);
			}

			player.GlobalCooldownTick = now + (long)(1000 * CombatConfig.Get(CombatConfig.GlobalCooldown));

			// 시전 시간도 공격 속도의 영향을 받는다('캐릭터 조작 및 전투' 2-6)
			float castTime = skill.castTime / Math.Max(0.05f, player.AttackSpeed);
			Broadcast(new S_Skill()
			{
				ObjectId = player.Id,
				Info = new SkillInfo() { SkillId = skillId, TargetId = target?.Id ?? 0 },
				CastTime = castTime,
			});

			if (castTime > 0f)
			{
				player.CastSkillId = skillId;
				player.CastTargetId = target?.Id ?? 0;
				int token = ++player.CastToken;
				PushAfter((int)(castTime * 1000), CompleteCast, player, token);
				return;
			}

			ResolveSkill(player, skill, target);
		}

		// 시전이 끝났다. 시전 중에 맞아도 끊기지 않지만, 대상이 사라졌거나 사거리를 벗어났거나 기절했으면 취소한다
		void CompleteCast(Player player, int token)
		{
			if (player.Room != this || player.CastToken != token || player.IsCasting == false)
				return;

			Table.SpiritSkillsRow skill = Table.TableManager.SpiritSkills.Get(player.CastSkillId);
			GameObject target = null;
			if (player.CastTargetId != 0)
				target = ResolveTarget(player, skill, player.CastTargetId);

			if (player.CanAct == false || (player.CastTargetId != 0 && target == null))
			{
				CancelCast(player);
				return;
			}

			player.CastSkillId = 0;
			player.CastTargetId = 0;
			ResolveSkill(player, skill, target);
		}

		void CancelCast(Player player)
		{
			player.CastSkillId = 0;
			player.CastTargetId = 0;
			player.CastToken++;
			Broadcast(new S_CancelSkill() { ObjectId = player.Id });
		}

		// 신명(신력 소모 −)
		static float SpiritCost(Player player, Table.SpiritSkillsRow skill)
		{
			return skill.spiritCost * (1f - player.Status.Value(StatusId.Fervor));
		}

		// 자원을 쓰고 효과를 낸다. 신력·쿨타임은 효과가 실제로 날 때만 쓴다(Esc·취소는 무료, '스킬 시스템' 2-3)
		void ResolveSkill(Player player, Table.SpiritSkillsRow skill, GameObject target)
		{
			long now = Environment.TickCount64;
			if (skill.gate == (int)SkillGate.Spirit)
			{
				float cost = SpiritCost(player, skill);
				if (player.Spirit < cost)
					return;
				player.AddSpirit(-cost);
			}
			else
			{
				player.StartCooldown(skill.skillID, now + (long)(1000 * skill.coolTime));
			}

			SkillEffect effect = (SkillEffect)skill.effect;
			switch (effect)
			{
				case SkillEffect.DashToTarget:
					DashNextTo(player, target);
					break;
				case SkillEffect.Backstep:
					Backstep(player, skill.range);
					return;
				case SkillEffect.PlaceTrap:
					PlaceTrap(player, skill);
					return;
				case SkillEffect.Cleanse:
					target?.Status.RemoveHarmful();
					break;
				case SkillEffect.Revive:
					// 회혼술: 혼절한 아군을 그 자리에서 체력 configs 20041 비율로('스킬 시스템' 2-10)
					if (target is Player fainted)
						Revive(fainted, CombatConfig.Get(CombatConfig.ReviveHpRate), false, null);
					return;
			}

			float extra = 1f;
			foreach (GameObject t in CollectTargets(player, skill, target))
			{
				if (effect == SkillEffect.Execute && t.Stat.Hp <= t.Stat.MaxHp * CombatConfig.Get(CombatConfig.ExecuteHpRate))
					extra = CombatConfig.Get(CombatConfig.ExecuteMultiplier);
				else
					extra = 1f;

				if (skill.coefType == (int)CoefType.Damage)
					Combat.Damage(player, t, skill.coefficient, extra);
				else if (skill.coefType == (int)CoefType.Heal)
					Combat.Heal(player, t, skill.coefficient);

				if (t.Room != this)
					continue; // 피해로 쓰러졌다
				foreach (int statusId in skill.statusIDs)
					t.Status.Apply(statusId, skill.statusTime, player);
				if (skill.knockback > 0 && t is Monster m)
					Knockback(player, m, skill.knockback);
			}
		}

		// 효과를 받을 대상.
		//   단일 적·단일 아군: 고른 대상
		//   방향: 바라보는 방향 직선 range칸 위의 망령(벽에서 멈춤), 가까운 순 최대 maxTarget
		//   즉시: range 0이면 자신. 그 외 자기 중심 range칸 안 — 피해·해로운 효과면 망령, 회복·이로운 효과면 플레이어(자신 포함). 가까운 순 최대 maxTarget
		List<GameObject> CollectTargets(Player player, Table.SpiritSkillsRow skill, GameObject target)
		{
			List<GameObject> list = new List<GameObject>();
			int maxTarget = Math.Max(1, skill.maxTarget);

			switch ((SkillTargetType)skill.targetType)
			{
				case SkillTargetType.Enemy:
				case SkillTargetType.Ally:
					if (target != null && target.IsAlive)
						list.Add(target);
					break;

				case SkillTargetType.Facing:
					list.AddRange(LineMonsters(player.CellPos, player.Dir, skill.range).Take(maxTarget));
					break;

				case SkillTargetType.Instant:
					if (skill.range <= 0)
					{
						list.Add(player);
						break;
					}
					IEnumerable<GameObject> pool = HitsEnemies(skill)
						? _monsters.Values.Where(m => m.IsAlive)
						: _players.Values.Where(p => p.IsAlive);
					list.AddRange(pool
						.Where(o => (o.CellPos - player.CellPos).cellDist <= skill.range)
						.OrderBy(o => (o.CellPos - player.CellPos).cellDist)
						.Take(maxTarget));
					break;
			}
			return list;
		}

		static bool HitsEnemies(Table.SpiritSkillsRow skill)
		{
			if (skill.coefType == (int)CoefType.Damage)
				return true;
			if (skill.coefType == (int)CoefType.Heal)
				return false;
			return skill.statusIDs.Count > 0 && StatusSet.IsHarmful(skill.statusIDs[0]);
		}

		// 단일 대상 스킬의 대상 확인. 사거리는 스킬 범위와 대상 선택 최대 거리(configs 20034) 중 작은 값
		GameObject ResolveTarget(Player player, Table.SpiritSkillsRow skill, int targetId)
		{
			GameObject target = null;
			if (skill.targetType == (int)SkillTargetType.Enemy && _monsters.TryGetValue(targetId, out Monster m))
				target = m;
			else if (skill.targetType == (int)SkillTargetType.Ally && _players.TryGetValue(targetId, out Player p))
				target = p;

			if (target == null)
				return null;

			// 회혼술은 혼절한 아군만, 그 밖의 스킬은 살아 있는 대상만
			bool wantsFainted = skill.effect == (int)SkillEffect.Revive;
			if (wantsFainted ? (target is Player p2 && p2.IsFainted) == false : target.IsAlive == false)
				return null;

			int range = Math.Min(skill.range, (int)CombatConfig.Get(CombatConfig.TargetMaxRange));
			if ((target.CellPos - player.CellPos).cellDist > range)
				return null;
			return target;
		}

		void FaceTarget(Player player, GameObject target)
		{
			Vector2Int diff = target.CellPos - player.CellPos;
			if (diff.x == 0 && diff.y == 0)
				return;
			MoveDir dir = GameObject.GetDirFromVec(diff);
			if (player.Dir == dir)
				return;
			player.Dir = dir;
			Broadcast(new S_Move() { ObjectId = player.Id, PosInfo = player.PosInfo });
		}

		// 바라보는 방향 직선 위의 망령을 가까운 순으로. 벽(충돌 칸)에서 멈춘다
		IEnumerable<Monster> LineMonsters(Vector2Int from, MoveDir dir, int range)
		{
			Vector2Int step = GameObject.DirToVec(dir);
			Vector2Int cell = from;
			for (int i = 0; i < range; i++)
			{
				Vector2Int next = cell + step;
				if (Map.CanGo(next, checkObjects: false) == false || Map.IsCornerBlocked(cell, next))
					yield break;
				cell = next;
				if (Map.Find(cell) is Monster m && m.IsAlive)
					yield return m;
			}
		}

		// ---- 위치를 옮기는 효과 ----

		// 밀어내기: 시전자에서 멀어지는 방향으로 칸 수만큼. 막히면 거기서 멈춘다
		void Knockback(Player player, Monster target, int cells)
		{
			Vector2Int diff = target.CellPos - player.CellPos;
			Vector2Int step = diff.x == 0 && diff.y == 0 ? GameObject.DirToVec(player.Dir) : GameObject.DirToVec(GameObject.GetDirFromVec(diff));
			Vector2Int dest = WalkFrom(target.CellPos, step, cells, blockPlayers: true);
			if (dest.x == target.CellPos.x && dest.y == target.CellPos.y)
				return;
			Map.ApplyMove(target, dest);
			Broadcast(new S_Move() { ObjectId = target.Id, PosInfo = target.PosInfo, Forced = true });
		}

		// 물러서기: 바라보는 반대 방향으로 칸 수만큼. 바라보는 방향은 그대로('스킬 시스템' 2-8)
		void Backstep(Player player, int cells)
		{
			Vector2Int step = GameObject.DirToVec(GameObject.RotateDir(player.Dir, 4));
			Vector2Int dest = WalkFrom(player.CellPos, step, cells, blockPlayers: false);
			if (dest.x == player.CellPos.x && dest.y == player.CellPos.y)
				return;
			Map.ApplyMove(player, dest);
			Broadcast(new S_Move() { ObjectId = player.Id, PosInfo = player.PosInfo, Forced = true });
		}

		// 돌진베기: 대상 옆 빈 칸 중 시전자에게 가장 가까운 칸으로 순간 이동
		void DashNextTo(Player player, GameObject target)
		{
			if (target == null)
				return;

			Vector2Int best = player.CellPos;
			int bestDist = int.MaxValue;
			for (int dy = -1; dy <= 1; dy++)
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;
					Vector2Int cell = target.CellPos + new Vector2Int(dx, dy);
					if (Map.CanGo(cell) == false)
						continue;
					int dist = (cell - player.CellPos).cellDist;
					if (dist < bestDist)
					{
						best = cell;
						bestDist = dist;
					}
				}

			if (bestDist == int.MaxValue)
				return;
			if (best.x != player.CellPos.x || best.y != player.CellPos.y)
				Map.ApplyMove(player, best);
			player.Dir = GameObject.GetDirFromVec(target.CellPos - player.CellPos);
			Broadcast(new S_Move() { ObjectId = player.Id, PosInfo = player.PosInfo, Forced = true });
		}

		Vector2Int WalkFrom(Vector2Int start, Vector2Int step, int cells, bool blockPlayers)
		{
			Vector2Int cell = start;
			for (int i = 0; i < cells; i++)
			{
				Vector2Int next = cell + step;
				if (Map.CanMove(cell, next) == false || Map.CanGo(next, true, blockPlayers) == false)
					break;
				cell = next;
			}
			return cell;
		}

		// 덫 놓기: 발밑 칸에 덫(유지 configs 20052). 밟은 망령에게 스킬의 상태이상(결박)
		void PlaceTrap(Player player, Table.SpiritSkillsRow skill)
		{
			Field trap = ObjectManager.Instance.Add<Field>();
			trap.Init(player, skill, player.CellPos, CombatConfig.Get(CombatConfig.TrapLifetime));
			EnterGame(trap);
		}
	}
}
