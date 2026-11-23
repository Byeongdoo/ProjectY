using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 적 특수기 실행('망령(적) 시스템' 2-5-1, monsterSkills 시트).
	//   선택: 조건을 만족한 스킬 중 Skill1 우선, 없으면 Skill2, 둘 다 안 되면 기본 AI(평타·이동).
	//   시작: 대상 = 위협 1위. 시전자 위치·방향·대상 칸·위험 칸을 고정하고 예고(S_SkillWarning). 예고 중 이동·평타·다른 스킬 금지.
	//   발동: 예고가 끝나는 순간 위험 칸 안의 플레이어만 맞는다. 재사용 대기는 발동 순간부터, 발동 후 회수 시간 동안 행동하지 않는다.
	//   취소: 기절·사망·복귀. 예고를 지우고 피해·상태 미적용, 그 스킬은 재시도 대기(configs 20058) 뒤 다시 시도. 일반 피격은 시전을 끊지 않는다.
	//   팩 제한: 팩 전체에서 동시에 1개(configs 20053), 다음 시작은 이전 시작 후 3초(20054)이면서 이전 판정·투사체 종료 이후.
	//   첫 사용: 교전 시작 후 3초(20056) + 팩 안 스폰 순서 × 0.5초(20057).
	public partial class Monster
	{
		// monsterSkills.shape (시트 '#형태' 메모와 같은 값)
		enum SkillShape
		{
			Dash = 1,       // 고정 방향 전방 직선 돌진. 지나간 칸의 플레이어 각 1회
			SelfArea = 2,   // 시전자 중심 반경(시전자 칸 포함)
			Line = 3,       // 고정 방향 전방 직선, 가장 가까운 플레이어부터 최대 대상 수
			TargetArea = 4, // 시전 시작 때 고정한 대상 칸 중심 반경
			Projectile = 5, // 고정 방향 직선 투사체, 벽 또는 첫 플레이어 적중 시 종료
		}

		const int ConfigBindImmunity = 20055;      // 같은 스킬 결박 재적용 면역(초)
		const int ConfigFirstDelay = 20056;        // 교전 시작 후 첫 특수기 대기(초)
		const int ConfigFirstDelayPerIndex = 20057;// 팩 스폰 순서당 추가 대기(초)
		const int ConfigCancelRetry = 20058;       // 취소 후 재시도 대기(초)

		static float Config(int id) => CombatConfig.Get(id);

		static int s_nextWarningId = 0;

		List<Table.MonsterSkillsRow> _skills = new List<Table.MonsterSkillsRow>();
		readonly Dictionary<int, long> _skillReadyTick = new Dictionary<int, long>();
		long _engagedAtTick;
		long _recoverUntilTick;

		class Cast
		{
			public Table.MonsterSkillsRow Skill;
			public MoveDir Dir;
			public List<Vector2Int> Cells;
			public Vector2Int Center; // 범위형의 중심(자기 중심 = 시전자 칸, 대상 칸 중심 = 시작 때 대상 칸)
			public long FireTick;
			public int WarningId;
		}
		Cast _cast;

		class Flight
		{
			public Table.MonsterSkillsRow Skill;
			public List<Vector2Int> Cells;
			public long StartTick;
			public int Passed; // 이미 지난 칸 수
		}
		Flight _flight;

		void LoadSkills()
		{
			_skills = Table.TableManager.MonsterSkills.Rows
				.Where(r => r.character == CharacterId)
				.OrderBy(r => r.slot)
				.ToList();
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

			long firstUse = _engagedAtTick
				+ (long)(1000 * Config(ConfigFirstDelay))
				+ (long)(1000 * Config(ConfigFirstDelayPerIndex) * PackIndex);
			if (now < firstUse)
				return false;
			if (Pack != null && Pack.CanStartSpecial(now) == false)
				return false;

			foreach (Table.MonsterSkillsRow skill in _skills)
			{
				if (_skillReadyTick.TryGetValue(skill.monsterSkillID, out long ready) && ready > now)
					continue;
				if (TryGetCastArea(skill, out MoveDir dir, out List<Vector2Int> cells, out Vector2Int center) == false)
					continue;

				StartCast(skill, dir, cells, center, now);
				return true;
			}
			return false;
		}

		// 스킬 조건(위협 1위 기준)과 위험 칸. 대각선도 칸 수로 센다.
		bool TryGetCastArea(Table.MonsterSkillsRow skill, out MoveDir dir, out List<Vector2Int> cells, out Vector2Int center)
		{
			center = CellPos;
			Vector2Int to = _target.CellPos - CellPos;
			int dist = to.cellDist;
			bool aligned = to.x == 0 || to.y == 0 || Math.Abs(to.x) == Math.Abs(to.y);
			bool inBand = dist >= skill.minRange && dist <= skill.range;
			dir = GetDirFromVec(to);
			cells = null;

			switch ((SkillShape)skill.shape)
			{
				case SkillShape.Dash:
					{
						// 위협 1위가 전방 직선 위에 있고 첫 칸으로 나갈 수 있을 때
						if (aligned == false || inBand == false)
							return false;
						Vector2Int first = CellPos + DirToVec(dir);
						if (Room.Map.CanGo(first, checkObjects: true) == false || Room.Map.IsCornerBlocked(CellPos, first))
							return false;
						cells = Ray(dir, skill.range);
						break;
					}
				case SkillShape.SelfArea:
					if (dist > skill.range)
						return false;
					cells = Square(CellPos, skill.radius);
					break;
				case SkillShape.Line:
				case SkillShape.Projectile:
					if (aligned == false || inBand == false || Room.Map.HasLineOfSight(CellPos, _target.CellPos) == false)
						return false;
					cells = Ray(dir, skill.range);
					break;
				case SkillShape.TargetArea:
					if (dist > skill.range || Room.Map.HasLineOfSight(CellPos, _target.CellPos) == false)
						return false;
					center = _target.CellPos;
					cells = Square(center, skill.radius);
					break;
				default:
					return false;
			}
			return cells.Count > 0;
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

		void StartCast(Table.MonsterSkillsRow skill, MoveDir dir, List<Vector2Int> cells, Vector2Int center, long now)
		{
			_cast = new Cast()
			{
				Skill = skill,
				Dir = dir,
				Cells = cells,
				Center = center,
				FireTick = now + (long)(1000 * skill.castTime),
				WarningId = ++s_nextWarningId,
			};
			Pack?.OnSpecialStart(now);

			State = CreatureState.Idle;
			Dir = dir;
			BroadcastMove();

			S_SkillWarning warning = new S_SkillWarning()
			{
				ObjectId = Id,
				WarningId = _cast.WarningId,
				SkillId = skill.monsterSkillID,
				DurationMs = (int)(1000 * skill.castTime),
			};
			foreach (Vector2Int c in cells)
				warning.Cells.Add(new CellPos() { X = c.x, Y = c.y });
			Room.Broadcast(warning);
		}

		// 기절·사망·복귀로 예고를 끊는다. 피해·상태 미적용.
		void CancelCast()
		{
			if (_cast == null)
				return;

			long now = Environment.TickCount64;
			_skillReadyTick[_cast.Skill.monsterSkillID] = now + (long)(1000 * Config(ConfigCancelRetry));
			if (Room != null)
				Room.Broadcast(new S_SkillWarning() { ObjectId = Id, WarningId = _cast.WarningId, SkillId = _cast.Skill.monsterSkillID, DurationMs = 0 });
			_cast = null;
			Pack?.OnSpecialEnd();
		}

		void Fire(long now)
		{
			Cast cast = _cast;
			_cast = null;
			Table.MonsterSkillsRow skill = cast.Skill;
			_skillReadyTick[skill.monsterSkillID] = now + (long)(1000 * skill.coolTime);

			// 발동 동작(클라는 슬롯으로 SKILL_1/SKILL_2 클립을 고른다)
			Room.Broadcast(new S_Skill() { ObjectId = Id, Info = new SkillInfo() { SkillId = skill.monsterSkillID, TargetId = _target != null ? _target.Id : 0 } });

			long busyMs = 0;
			switch ((SkillShape)skill.shape)
			{
				case SkillShape.SelfArea:
				case SkillShape.TargetArea:
					HitPlayersIn(skill, cast.Cells, cast.Center);
					break;
				case SkillShape.Line:
					HitPlayersIn(skill, cast.Cells, CellPos);
					break;
				case SkillShape.Dash:
					busyMs = (long)(1000 * skill.dashTime);
					Dash(skill, cast.Cells, busyMs);
					break;
				case SkillShape.Projectile:
					_flight = new Flight() { Skill = skill, Cells = cast.Cells, StartTick = now };
					break;
			}

			// 투사체는 날아가는 동안 팩 제한을 잡고 있다(UpdateFlight가 끝낼 때 푼다)
			if (_flight == null)
				Pack?.OnSpecialEnd();

			_recoverUntilTick = now + busyMs + (long)(1000 * skill.recoveryTime);
		}

		// 칸 안의 플레이어를 center에서 가까운 순으로 최대 대상 수만큼 친다
		void HitPlayersIn(Table.MonsterSkillsRow skill, List<Vector2Int> cells, Vector2Int center)
		{
			List<Player> hits = new List<Player>();
			foreach (Vector2Int c in cells)
				hits.AddRange(Room.TargetablePlayersAt(c));

			int max = skill.maxTarget > 0 ? skill.maxTarget : int.MaxValue;
			foreach (Player p in hits.OrderBy(p => (p.CellPos - center).cellDist).ThenBy(p => (p.CellPos - center).sqrMagnitude).Take(max))
				HitWithSkill(skill, p);
		}

		// 돌진: 예고한 칸을 따라 나아가며 지나간 칸의 플레이어를 각 1회 친다.
		// 다른 적·벽 칸 앞에서 멈추고, 플레이어가 선 칸에서는 끝나지 않는다(그 직전의 빈 칸에 선다).
		void Dash(Table.MonsterSkillsRow skill, List<Vector2Int> cells, long dashMs)
		{
			int max = skill.maxTarget > 0 ? skill.maxTarget : int.MaxValue;
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

			float speed = Math.Max(0.1f, _flight.Skill.projectileSpeed);
			int reach = (int)((Environment.TickCount64 - _flight.StartTick) * speed / 1000f) + 1;
			reach = Math.Min(reach, _flight.Cells.Count);

			while (_flight != null && _flight.Passed < reach)
			{
				Vector2Int c = _flight.Cells[_flight.Passed];
				_flight.Passed++;
				List<Player> players = Room.TargetablePlayersAt(c);
				if (players.Count > 0)
				{
					Table.MonsterSkillsRow skill = _flight.Skill;
					int max = skill.maxTarget > 0 ? skill.maxTarget : int.MaxValue;
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

		// 피해 = 시전자 공격력 × 계수(피해 공식 적용), 그다음 상태이상.
		// 결박은 같은 스킬 결박이 끝난 뒤 5초(configs 20052) 동안 다시 걸리지 않는다(피해는 적용).
		// TODO 해독으로 결박이 일찍 풀리면 면역도 그때부터 5초여야 한다. 지금은 원래 끝날 시각 기준이다.
		void HitWithSkill(Table.MonsterSkillsRow skill, Player player)
		{
			if (Room == null || player.IsAlive == false || player.Room != Room)
				return;

			if (skill.coefficient > 0)
				Combat.Damage(this, player, skill.coefficient);

			if (skill.status == 0 || skill.statusTime <= 0 || player.IsAlive == false)
				return;

			long now = Environment.TickCount64;
			if (skill.status == StatusId.Root)
			{
				if (player.IsBindImmune(skill.monsterSkillID, now))
					return;
				player.SetBindImmune(skill.monsterSkillID, now + (long)(1000 * (skill.statusTime + Config(ConfigBindImmunity))));
			}
			player.Status.Apply(skill.status, skill.statusTime, this);
		}
	}
}
