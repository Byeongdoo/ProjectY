using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 적 특수기·보스 스킬 실행. 일반 적은 monsterSkills('망령(적) 시스템' 2-5-1), 보스는 bossSkills('보스 시스템')를 같은 흐름으로 쓴다.
	//   시작: 대상을 정하고 시전자 위치·방향·대상 칸·위험 칸을 고정해 예고(S_SkillWarning). 예고 중 이동·평타·다른 스킬 금지.
	//   발동: 예고가 끝나는 순간 판정. 칸형은 위험 칸 안의 플레이어만, 표식형(받는 패턴)은 표식된 플레이어가 맞는다.
	//         재사용 대기는 발동 순간부터. 발동 뒤 회수 시간(일반 적)·탈진(보스 대형 패턴) 동안 행동하지 않는다.
	//   취소: 기절·사망·복귀·보스 페이즈 전환. 예고를 지우고 피해·상태 미적용, 재시도 대기(configs 20058) 뒤 다시 시도.
	//   일반 적: Skill1 우선, 팩 전체에서 동시에 1개(20053)·최소 간격 3초(20054), 첫 사용 교전 3초(20056) + 스폰 순서 × 0.5초(20057).
	//   보스: 쿨이 찬 스킬 중 먼저 준비된 것부터(같으면 ID 순), 판정이 끝난 뒤 다음 스킬까지 최소 3초(20061),
	//         각 스킬 첫 사용 = 교전 시작 + 자기 쿨타임. ID 순만 쓰면 쿨이 짧은 1페이즈 스킬이 늘 먼저라 2페이즈 스킬이 나오지 않는다.
	public partial class Monster
	{
		// monsterSkills.shape·bossSkills.shape
		enum SkillShape
		{
			Dash = 1,       // 고정 방향 전방 직선 돌진. 지나간 칸의 플레이어 각 1회
			SelfArea = 2,   // 시전자 중심 반경(시전자 칸 포함)
			Line = 3,       // 고정 방향 전방 직선, 가장 가까운 플레이어부터 최대 대상 수
			TargetArea = 4, // 시전 시작 때 고정한 대상 칸 중심 반경
			Projectile = 5, // 고정 방향 직선 투사체, 벽 또는 첫 플레이어 적중 시 종료
			Cone = 6,       // 고정 방향 부채꼴: 반경 안이면서 바라보는 방향 좌우 45도 안(보스 뿌리 파도)
			Mark = 7,       // 표식: 고른 플레이어가 위치와 상관없이 맞는다(탱커 강타·디버프·산개 — 피하는 패턴이 아니라 받는 패턴)
		}

		// bossSkills.pattern (#타입 정의 BossPattern)
		const int PatternSpread = 3; // 산개: 표식끼리 configs 20045칸 이내면 피해 × 20046

		// bossSkills.targetRule (#타입 정의 TargetRule)
		enum TargetRule { Threat = 1, Random = 2, Farthest = 3, All = 4, Self = 5 }

		const int ConfigFirstDelay = 20056;        // 교전 시작 후 첫 특수기 대기(초)
		const int ConfigFirstDelayPerIndex = 20057;// 팩 스폰 순서당 추가 대기(초)
		const int ConfigCancelRetry = 20058;       // 취소 후 재시도 대기(초)
		const int ConfigSpreadDistance = 20045;    // 산개 판정 거리(칸)
		const int ConfigSpreadMultiplier = 20046;  // 산개 겹침 피해 배율

		static float Config(int id) => CombatConfig.Get(id);

		static int s_nextWarningId = 0;
		static readonly Random s_random = new Random();

		// 두 시트의 한 행을 같은 모양으로 옮긴 것
		class SkillSpec
		{
			public int Id;
			public int Phase = 1;           // 보스만. 이 페이즈부터 쓴다
			public SkillShape Shape;
			public TargetRule Rule = TargetRule.Threat;
			public int Pattern;
			public int MinRange, Range, Radius;
			public float CastTime, CoolTime, Coef;
			public int MaxTarget;
			public int Status;
			public float StatusTime;
			public float ProjectileSpeed, DashTime, RecoveryTime, ExhaustTime;
		}

		List<SkillSpec> _skills = new List<SkillSpec>();
		readonly Dictionary<int, long> _skillReadyTick = new Dictionary<int, long>();
		long _engagedAtTick;
		long _recoverUntilTick;

		class Cast
		{
			public SkillSpec Skill;
			public MoveDir Dir;
			public List<Vector2Int> Cells;
			public Vector2Int Center;      // 범위형의 중심(자기 중심 = 시전자 칸, 대상 칸 중심 = 시작 때 대상 칸)
			public List<Player> Marked;    // 표식형의 대상
			public long FireTick;
			public int WarningId;
		}
		Cast _cast;

		class Flight
		{
			public SkillSpec Skill;
			public List<Vector2Int> Cells;
			public long StartTick;
			public int Passed; // 이미 지난 칸 수
		}
		Flight _flight;

		void LoadSkills()
		{
			if (_isBoss)
			{
				_skills = Table.TableManager.BossSkills.Rows
					.Where(r => r.boss == CharacterId && r.shape != 0)
					.OrderBy(r => r.bossSkillID)
					.Select(r => new SkillSpec()
					{
						Id = r.bossSkillID, Phase = r.phase, Shape = (SkillShape)r.shape, Rule = (TargetRule)r.targetRule, Pattern = r.pattern,
						MinRange = 0, Range = 1, Radius = r.radius,
						CastTime = r.castTime, CoolTime = r.coolTime, Coef = r.coefficient, MaxTarget = r.maxTarget,
						Status = r.status, StatusTime = r.statusTime, ExhaustTime = r.exhaustTime,
					})
					.ToList();
				return;
			}

			_skills = Table.TableManager.MonsterSkills.Rows
				.Where(r => r.character == CharacterId)
				.OrderBy(r => r.slot)
				.Select(r => new SkillSpec()
				{
					Id = r.monsterSkillID, Shape = (SkillShape)r.shape,
					MinRange = r.minRange, Range = r.range, Radius = r.radius,
					CastTime = r.castTime, CoolTime = r.coolTime, Coef = r.coefficient, MaxTarget = r.maxTarget,
					Status = r.status, StatusTime = r.statusTime,
					ProjectileSpeed = r.projectileSpeed, DashTime = r.dashTime, RecoveryTime = r.recoveryTime,
				})
				.ToList();
		}

		// 교전 시작 때. 보스는 각 스킬을 자기 쿨타임만큼 기다린 뒤 처음 쓴다
		void OnEngageStart(long now)
		{
			_engagedAtTick = now;
			if (_isBoss)
				foreach (SkillSpec s in _skills)
					_skillReadyTick[s.Id] = now + (long)(1000 * s.CoolTime);
		}

		// 반환: 이번 틱을 특수기가 맡았는지(예고 중·회수 중·이번에 시전 시작)
		bool UpdateSpecial(long now)
		{
			if (_cast != null)
			{
				if (now >= _cast.FireTick)
					Fire(now);
				return true;
			}
			if (now < _recoverUntilTick)
				return true;
			return TryStartSpecial(now);
		}

		bool TryStartSpecial(long now)
		{
			if (_skills.Count == 0 || _target == null)
				return false;

			if (_isBoss)
			{
				if (now < _nextBossSkillTick)
					return false;
			}
			else
			{
				long firstUse = _engagedAtTick
					+ (long)(1000 * Config(ConfigFirstDelay))
					+ (long)(1000 * Config(ConfigFirstDelayPerIndex) * PackIndex);
				if (now < firstUse)
					return false;
				if (Pack != null && Pack.CanStartSpecial(now) == false)
					return false;
			}

			// 일반 적: 슬롯 순(Skill1 우선). 보스: 준비된 지 오래된 것부터, 같으면 ID 순
			IEnumerable<SkillSpec> order = _skills;
			if (_isBoss)
				order = _skills.OrderBy(s => _skillReadyTick.TryGetValue(s.Id, out long t) ? t : 0).ThenBy(s => s.Id);

			foreach (SkillSpec skill in order)
			{
				if (_isBoss && skill.Phase > _phase)
					continue;
				if (_skillReadyTick.TryGetValue(skill.Id, out long ready) && ready > now)
					continue;
				if (TryPrepare(skill, out Cast cast) == false)
					continue;

				StartCast(cast, now);
				return true;
			}
			return false;
		}

		// 스킬 조건과 위험 칸·표식 대상. 대각선도 칸 수로 센다.
		bool TryPrepare(SkillSpec skill, out Cast cast)
		{
			cast = new Cast() { Skill = skill, Center = CellPos };
			Vector2Int to = _target.CellPos - CellPos;
			int dist = to.cellDist;
			bool aligned = to.x == 0 || to.y == 0 || Math.Abs(to.x) == Math.Abs(to.y);
			bool inBand = dist >= skill.MinRange && dist <= skill.Range;
			cast.Dir = GetDirFromVec(to);

			switch (skill.Shape)
			{
				case SkillShape.Dash:
					{
						// 위협 1위가 전방 직선 위에 있고 첫 칸으로 나갈 수 있을 때
						if (aligned == false || inBand == false)
							return false;
						Vector2Int first = CellPos + DirToVec(cast.Dir);
						if (Room.Map.CanGo(first, checkObjects: true) == false || Room.Map.IsCornerBlocked(CellPos, first))
							return false;
						cast.Cells = Ray(cast.Dir, skill.Range);
						break;
					}
				case SkillShape.SelfArea:
					// 일반 적: 위협 1위가 사거리 안일 때. 보스(대상 규칙 Self): 언제든
					if (_isBoss == false && dist > skill.Range)
						return false;
					cast.Cells = Square(CellPos, skill.Radius);
					break;
				case SkillShape.Line:
				case SkillShape.Projectile:
					if (aligned == false || inBand == false || Room.Map.HasLineOfSight(CellPos, _target.CellPos) == false)
						return false;
					cast.Cells = Ray(cast.Dir, skill.Range);
					break;
				case SkillShape.TargetArea:
					{
						Player center = _isBoss ? PickTargets(skill.Rule, 1).FirstOrDefault() : _target;
						if (center == null)
							return false;
						if (_isBoss == false && (dist > skill.Range || Room.Map.HasLineOfSight(CellPos, _target.CellPos) == false))
							return false;
						cast.Center = center.CellPos;
						cast.Cells = Square(cast.Center, skill.Radius);
						break;
					}
				case SkillShape.Cone:
					cast.Cells = Cone(cast.Dir, skill.Radius);
					break;
				case SkillShape.Mark:
					{
						// 탱커 강타(위협 1위)는 붙어 있을 때만. 그 밖의 표식은 대상 규칙으로 고른다
						if (skill.Rule == TargetRule.Threat && dist > 1)
							return false;
						cast.Marked = PickTargets(skill.Rule, Math.Max(1, skill.MaxTarget));
						if (cast.Marked.Count == 0)
							return false;
						cast.Cells = cast.Marked.Select(p => p.CellPos).ToList();
						break;
					}
				default:
					return false;
			}
			return cast.Cells != null && cast.Cells.Count > 0;
		}

		// 대상 규칙(bossSkills.targetRule)으로 살아 있는 플레이어를 고른다
		List<Player> PickTargets(TargetRule rule, int count)
		{
			List<Player> alive = Room.TargetablePlayers();
			// 보스는 아레나 안 플레이어 중에서 고른다(불씨에서 부활해 걸어오는 중인 플레이어는 빼고). 아무도 없으면 전원
			if (HasArena)
			{
				List<Player> inArena = alive.Where(p => InArena(p.CellPos)).ToList();
				if (inArena.Count > 0)
					alive = inArena;
			}
			switch (rule)
			{
				case TargetRule.Threat:
					return _target != null && _target.IsAlive ? new List<Player>() { _target } : new List<Player>();
				case TargetRule.Farthest:
					return alive.OrderByDescending(p => (p.CellPos - CellPos).cellDist).Take(count).ToList();
				case TargetRule.All:
					return alive;
				default:
					return alive.OrderBy(_ => s_random.Next()).Take(count).ToList();
			}
		}

		// 시전자에서 dir 방향으로 길이 len 칸(벽·모서리에서 끊는다)
		List<Vector2Int> Ray(MoveDir dir, int len)
		{
			List<Vector2Int> cells = new List<Vector2Int>();
			Vector2Int step = DirToVec(dir);
			Vector2Int prev = CellPos;
			for (int i = 0; i < len; i++)
			{
				Vector2Int next = prev + step;
				if (Room.Map.CanGo(next, checkObjects: false) == false || Room.Map.IsCornerBlocked(prev, next))
					break;
				cells.Add(next);
				prev = next;
			}
			return cells;
		}

		// center 중심 반경 radius의 정사각형(벽 칸 제외)
		List<Vector2Int> Square(Vector2Int center, int radius)
		{
			List<Vector2Int> cells = new List<Vector2Int>();
			for (int dy = -radius; dy <= radius; dy++)
				for (int dx = -radius; dx <= radius; dx++)
				{
					Vector2Int c = new Vector2Int(center.x + dx, center.y + dy);
					if (Room.Map.CanGo(c, checkObjects: false))
						cells.Add(c);
				}
			return cells;
		}

		// 부채꼴: 시전자에서 반경 radius 칸 이내이면서 dir 기준 좌우 45도 안(경계 포함). 벽 칸과 시야가 막힌 칸은 뺀다
		List<Vector2Int> Cone(MoveDir dir, int radius)
		{
			Vector2Int f = DirToVec(dir);
			double fx = f.x, fy = f.y, flen = Math.Sqrt(fx * fx + fy * fy);
			double cos45 = Math.Cos(Math.PI / 4) - 1e-6;
			List<Vector2Int> cells = new List<Vector2Int>();
			for (int dy = -radius; dy <= radius; dy++)
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;
					double len = Math.Sqrt(dx * dx + dy * dy);
					if ((dx * fx + dy * fy) / (len * flen) < cos45)
						continue;
					Vector2Int c = new Vector2Int(CellPos.x + dx, CellPos.y + dy);
					if (Room.Map.CanGo(c, checkObjects: false) && Room.Map.HasLineOfSight(CellPos, c))
						cells.Add(c);
				}
			return cells;
		}

		void StartCast(Cast cast, long now)
		{
			SkillSpec skill = cast.Skill;
			cast.FireTick = now + (long)(1000 * skill.CastTime);
			cast.WarningId = ++s_nextWarningId;
			_cast = cast;
			Pack?.OnSpecialStart(now);

			State = CreatureState.Idle;
			Dir = cast.Dir;
			BroadcastMove();

			S_SkillWarning warning = new S_SkillWarning()
			{
				ObjectId = Id,
				WarningId = cast.WarningId,
				SkillId = skill.Id,
				DurationMs = (int)(1000 * skill.CastTime),
			};
			foreach (Vector2Int c in cast.Cells)
				warning.Cells.Add(new CellPos() { X = c.x, Y = c.y });
			if (cast.Marked != null)
				foreach (Player p in cast.Marked)
					warning.TargetIds.Add(p.Id);
			Room.Broadcast(warning);
		}

		// 기절·사망·복귀·페이즈 전환으로 예고를 끊는다. 피해·상태 미적용.
		void CancelCast()
		{
			if (_cast == null)
				return;

			long now = Environment.TickCount64;
			_skillReadyTick[_cast.Skill.Id] = now + (long)(1000 * Config(ConfigCancelRetry));
			if (Room != null)
				Room.Broadcast(new S_SkillWarning() { ObjectId = Id, WarningId = _cast.WarningId, SkillId = _cast.Skill.Id, DurationMs = 0 });
			_cast = null;
			Pack?.OnSpecialEnd();
		}

		void Fire(long now)
		{
			Cast cast = _cast;
			_cast = null;
			SkillSpec skill = cast.Skill;
			_skillReadyTick[skill.Id] = now + (long)(1000 * skill.CoolTime);

			// 발동 동작(클라는 ID 끝자리로 SKILL_1/SKILL_2 클립을 고른다)
			Room.Broadcast(new S_Skill() { ObjectId = Id, Info = new SkillInfo() { SkillId = skill.Id, TargetId = _target != null ? _target.Id : 0 } });

			long busyMs = 0;
			switch (skill.Shape)
			{
				case SkillShape.SelfArea:
				case SkillShape.TargetArea:
				case SkillShape.Cone:
					HitPlayersIn(skill, cast.Cells, cast.Center);
					break;
				case SkillShape.Line:
					HitPlayersIn(skill, cast.Cells, CellPos);
					break;
				case SkillShape.Mark:
					HitMarked(skill, cast.Marked);
					break;
				case SkillShape.Dash:
					busyMs = (long)(1000 * skill.DashTime);
					Dash(skill, cast.Cells, busyMs);
					break;
				case SkillShape.Projectile:
					_flight = new Flight() { Skill = skill, Cells = cast.Cells, StartTick = now };
					break;
			}
			if (Room == null)
				return;

			// 투사체는 날아가는 동안 팩 제한을 잡고 있다(UpdateFlight가 끝낼 때 푼다)
			if (_flight == null)
				Pack?.OnSpecialEnd();

			_recoverUntilTick = now + busyMs + (long)(1000 * skill.RecoveryTime);

			if (_isBoss)
			{
				_nextBossSkillTick = now + (long)(1000 * Config(ConfigBossSkillGap));
				// 대형 패턴 직후 탈진: 행동 정지 + 받는 피해 증가(statusEffects 8013, '보스 시스템' 2-6)
				if (skill.ExhaustTime > 0)
					Status.Apply(StatusId.Exhaust, skill.ExhaustTime, this);
			}
		}

		// 칸 안의 플레이어를 center에서 가까운 순으로 최대 대상 수만큼 친다
		void HitPlayersIn(SkillSpec skill, List<Vector2Int> cells, Vector2Int center)
		{
			List<Player> hits = new List<Player>();
			foreach (Vector2Int c in cells)
				hits.AddRange(Room.TargetablePlayersAt(c));

			int max = skill.MaxTarget > 0 ? skill.MaxTarget : int.MaxValue;
			foreach (Player p in hits.OrderBy(p => (p.CellPos - center).cellDist).ThenBy(p => (p.CellPos - center).sqrMagnitude).Take(max))
				HitWithSkill(skill, p);
		}

		// 표식형: 표식된 플레이어가 맞는다. 산개는 다른 표식이 configs 20045칸 이내에 있으면 피해 × 20046
		void HitMarked(SkillSpec skill, List<Player> marked)
		{
			List<Player> alive = marked.Where(p => p.IsAlive && p.Room == Room).ToList();
			foreach (Player p in alive)
			{
				float mul = 1f;
				if (skill.Pattern == PatternSpread)
				{
					int spread = (int)Math.Round(Config(ConfigSpreadDistance));
					if (alive.Any(o => o != p && (o.CellPos - p.CellPos).cellDist <= spread))
						mul = Config(ConfigSpreadMultiplier);
				}
				HitWithSkill(skill, p, mul);
			}
		}

		// 돌진: 예고한 칸을 따라 나아가며 지나간 칸의 플레이어를 각 1회 친다.
		// 다른 적·벽 칸 앞에서 멈추고, 플레이어가 선 칸에서는 끝나지 않는다(그 직전의 빈 칸에 선다).
		void Dash(SkillSpec skill, List<Vector2Int> cells, long dashMs)
		{
			int max = skill.MaxTarget > 0 ? skill.MaxTarget : int.MaxValue;
			HashSet<int> hitIds = new HashSet<int>();
			Vector2Int prev = CellPos;
			Vector2Int stop = CellPos;
			foreach (Vector2Int c in cells)
			{
				GameObject blocker = Room.Map.Find(c);
				if ((blocker != null && blocker != this) || Room.Map.IsCornerBlocked(prev, c))
					break;

				List<Player> players = Room.TargetablePlayersAt(c);
				foreach (Player p in players)
				{
					if (hitIds.Count >= max || hitIds.Add(p.Id) == false)
						continue;
					HitWithSkill(skill, p);
				}
				if (players.Count == 0)
					stop = c;
				prev = c;
				if (Room == null)
					return;
			}

			if (stop.x != CellPos.x || stop.y != CellPos.y)
			{
				State = CreatureState.Moving;
				Room.Map.ApplyMove(this, stop);
				BroadcastMove();
			}
			_nextMoveTick = Environment.TickCount64 + dashMs;
		}

		// 투사체: 초당 projectileSpeed 칸으로 예고한 칸을 지나며 첫 플레이어에게 맞고 끝난다
		void UpdateFlight()
		{
			if (_flight == null || Room == null)
				return;

			float speed = Math.Max(0.1f, _flight.Skill.ProjectileSpeed);
			int reach = (int)((Environment.TickCount64 - _flight.StartTick) * speed / 1000f) + 1;
			reach = Math.Min(reach, _flight.Cells.Count);

			while (_flight != null && _flight.Passed < reach)
			{
				Vector2Int c = _flight.Cells[_flight.Passed];
				_flight.Passed++;
				List<Player> players = Room.TargetablePlayersAt(c);
				if (players.Count > 0)
				{
					SkillSpec skill = _flight.Skill;
					int max = skill.MaxTarget > 0 ? skill.MaxTarget : int.MaxValue;
					EndFlight();
					foreach (Player p in players.Take(max))
						HitWithSkill(skill, p);
					return;
				}
			}

			if (_flight != null && _flight.Passed >= _flight.Cells.Count)
				EndFlight();
		}

		void EndFlight()
		{
			if (_flight == null)
				return;
			_flight = null;
			Pack?.OnSpecialEnd();
		}

		// 피해 = 시전자 공격력 × 계수 × extra(피해 공식 적용), 그다음 상태이상.
		// 일반 적 특수기 결박은 같은 스킬 결박이 끝나거나 풀린 뒤 5초(configs 20055) 동안 다시 걸리지 않는다(피해는 적용).
		// 면역 시작은 결박이 실제로 사라진 순간이다(만료·해독 모두, Player.OnStatusChanged). 보스 결박에는 이 면역이 없다.
		void HitWithSkill(SkillSpec skill, Player player, float extra = 1f)
		{
			if (Room == null || player.IsAlive == false || player.Room != Room)
				return;

			if (skill.Coef > 0)
				Combat.Damage(this, player, skill.Coef, extra);

			if (skill.Status == 0 || skill.StatusTime <= 0 || player.IsAlive == false)
				return;

			long now = Environment.TickCount64;
			if (skill.Status == StatusId.Root && _isBoss == false)
			{
				if (player.IsBindImmune(skill.Id, now))
					return;
				player.MarkRootSkill(skill.Id);
			}
			player.Status.Apply(skill.Status, skill.StatusTime, this);
		}
	}
}
