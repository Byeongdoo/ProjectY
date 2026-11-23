using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 망령(적). 서버 권위로 탐지·추격·평타·복귀를 한다('망령(적) 시스템' 2-5).
	//   대기 → (탐지/피격) → 추격 → 공격 → (복귀 거리 초과/대상 없음) → 복귀 → 대기
	//   대상은 위협 1위('캐릭터 조작 및 전투' 2-9). 팩 하나가 반응하면 팩 전체가 함께 반응한다.
	// 개체별 특수기(2-5-1)는 monsterSkills 테이블이 생긴 뒤 붙인다.
	public class Monster : GameObject
	{
		public Monster()
		{
			ObjectType = GameObjectType.Monster;

			// TEMP (InitFromTable 이전 기본값)
			Stat.Level = 1;
			Stat.Hp = 100;
			Stat.MaxHp = 100;
			Stat.Speed = 5.0f;

			State = CreatureState.Idle;
		}

		// characterInfos.characterID. 클라는 이 값의 prefabName(Prefabs/Creature/Monster)으로 보여 준다. 0이면 레거시 임시 몬스터.
		public int CharacterId
		{
			get { return Info.CharacterId; }
			set { Info.CharacterId = value; }
		}

		// 같은 팩(spawnGroups 1회분). 한 체가 탐지·피격되면 팩 전체가 반응한다('망령(적) 시스템' 2-5).
		public List<Monster> Pack { get; set; }

		// 배치된 칸. 복귀 거리는 이 칸에서 잰다.
		public Vector2Int SpawnCell { get; set; }

		const int ConfigDiagonalTimeScale = 20030; // 대각선 1칸 소요 시간 배율
		const int ConfigBasicInterval = 20032;     // 평타 간격(초, 공격 속도 1.0 기준)
		const int ConfigThreatPerDamage = 20036;   // 피해 1당 위협
		const int ConfigThreatSwitchRate = 20038;  // 대상 변경 배율

		// 레거시 SkillData의 근접 평타 ID. 클라 MonsterController가 이 ID로 공격 동작을 보여 준다(원거리 귀신도 같은 동작).
		const int BasicMeleeSkillId = 1;

		int _searchCellDist = 10;
		int _leashCellDist = 20;

		// 분류별 평타·거리 규칙(monsterTypes). 근접 분류 = 사거리 1·유지 거리 0.
		// 귀신 = 사거리 9칸 즉발 단일 대상, 위협 1위가 유지 최소 거리(6칸)보다 가까우면 물러선다('망령(적) 시스템' 2-5).
		int _basicRange = 1;
		int _keepMin = 0;
		float _basicCoef = 1f;

		// 테이블 망령으로 초기화한다('망령(적) 시스템' 2-4).
		//   개체 스탯 = characterLevel(group = characterID), 던전 체력 = 개체 체력 × dungeons.mobHpRate
		//   탐지 = characterInfos.scanRange, 복귀 거리 = characterInfos.leashRange
		public void InitFromTable(int characterId, float mobHpRate)
		{
			Table.CharacterInfosRow info = Table.TableManager.CharacterInfos.Get(characterId);
			Table.CharacterLevelRow stat = Table.TableManager.CharacterLevel.Rows.First(r => r.group == characterId);

			CharacterId = characterId;
			Info.Name = info.name;
			Stat.Level = stat.level;
			Stat.Attack = stat.attack;
			Stat.MaxHp = (int)Math.Round(stat.maxHP * mobHpRate);
			Stat.Hp = Stat.MaxHp;
			Stat.Speed = stat.moveSpeed;
			Defence = stat.defence;
			Critical = stat.critical;
			CriticalAttack = stat.criticalAttack;
			AttackSpeed = stat.attackSpeed > 0 ? stat.attackSpeed : 1f;
			_searchCellDist = info.scanRange;
			_leashCellDist = info.leashRange;

			Table.MonsterTypesRow type = Table.TableManager.MonsterTypes.Get(info.monsterType);
			_basicRange = Math.Max(1, type.basicRange);
			_keepMin = type.keepMin;
			_basicCoef = type.basicCoef;
		}

		static float Config(int id)
		{
			return Table.TableManager.Configs.Get(id).value[0];
		}

		#region 위협

		// 플레이어 ID → 위협. 탐지·팩 연동으로 들어온 플레이어는 0으로 올린다.
		readonly Dictionary<int, float> _threat = new Dictionary<int, float>();
		Player _target;
		bool _returning;

		bool Engaged { get { return _threat.Count > 0; } }

		void AddThreat(Player player, float amount)
		{
			_threat.TryGetValue(player.Id, out float current);
			_threat[player.Id] = current + amount;
		}

		// 교전 시작(탐지·피격·팩 연동). 이미 교전 중이면 위협 표에 올리기만 한다.
		void Engage(Player player, bool alertPack)
		{
			if (_returning || State == CreatureState.Dead || Room == null || Room.IsTargetable(player) == false)
				return;

			bool wasEngaged = Engaged;
			AddThreat(player, 0);
			if (_target == null)
				_target = player;
			if (wasEngaged == false && State == CreatureState.Idle)
				State = CreatureState.Moving;

			if (alertPack && wasEngaged == false)
				AlertPack(player);
		}

		// 팩 전체가 같은 플레이어를 위협 표에 올린다.
		void AlertPack(Player player)
		{
			if (Pack == null)
				return;
			foreach (Monster m in Pack)
			{
				if (m == this || m.Room != Room)
					continue;
				m.Engage(player, alertPack: false);
			}
		}

		// 위협 1위를 대상으로 삼는다. 현재 대상보다 1.1배(configs 20038) 이상 높아야 바꾼다(핑퐁 방지).
		void SelectTarget()
		{
			// 나간 플레이어·혼절한 플레이어는 위협 표에서 뺀다
			List<int> removed = null;
			foreach (int playerId in _threat.Keys)
			{
				Player p = Room.FindPlayer(o => o.Id == playerId);
				if (Room.IsTargetable(p) == false)
					(removed ??= new List<int>()).Add(playerId);
			}
			if (removed != null)
				foreach (int id in removed)
					_threat.Remove(id);

			if (_target != null && _threat.ContainsKey(_target.Id) == false)
				_target = null;

			Player best = null;
			float bestThreat = float.MinValue;
			foreach (KeyValuePair<int, float> kv in _threat)
			{
				if (kv.Value > bestThreat)
				{
					best = Room.FindPlayer(o => o.Id == kv.Key);
					bestThreat = kv.Value;
				}
			}

			if (best == null || best == _target)
				return;
			if (_target == null || bestThreat >= _threat[_target.Id] * Config(ConfigThreatSwitchRate))
				_target = best;
		}

		#endregion

		public override void OnDamaged(GameObject attacker, int damage)
		{
			// 복귀 중에는 맞지 않는다
			if (_returning || Room == null || State == CreatureState.Dead)
				return;

			int before = Hp;
			base.OnDamaged(attacker, damage);
			if (Room == null || State == CreatureState.Dead)
				return;

			// 피해 1 = 위협 1(configs 20036) × 원혼 위협 배율(spirits.threatRate). 탐지 범위 밖에서 맞아도 반응한다.
			if (attacker is Player p)
			{
				Engage(p, alertPack: true);
				AddThreat(p, (before - Hp) * Config(ConfigThreatPerDamage) * p.ThreatRate);
			}
		}

		// 망령은 쓰러지면 그 판에서 사라진다(레거시처럼 그 자리에서 되살아나지 않는다).
		public override void OnDead(GameObject attacker)
		{
			if (Room == null)
				return;

			S_Die diePacket = new S_Die();
			diePacket.ObjectId = Id;
			diePacket.AttackerId = attacker.Id;
			Room.Broadcast(diePacket);

			State = CreatureState.Dead;
			_threat.Clear();
			_target = null;
			Room.LeaveGame(Id);
			ObjectManager.Instance.Remove(Id);
		}

		// FSM
		public override void Update()
		{
			if (Room == null || State == CreatureState.Dead)
				return;

			Statuses.Update();

			// 기절: 아무 행동도 하지 않는다(복귀 중에는 상태이상을 지웠으므로 해당 없음)
			if (_returning == false && Statuses.CanAct == false)
			{
				if (State != CreatureState.Idle)
				{
					State = CreatureState.Idle;
					BroadcastMove();
				}
				return;
			}

			if (_returning)
				UpdateReturn();
			else if (Engaged)
				UpdateCombat();
			else
				UpdateIdle();
		}

		long _nextSearchTick = 0;
		void UpdateIdle()
		{
			long now = Environment.TickCount64;
			if (_nextSearchTick > now)
				return;
			_nextSearchTick = now + 1000;

			Player target = Room.FindPlayer(o =>
			{
				Player p = o as Player;
				return Room.IsTargetable(p) && (p.CellPos - CellPos).cellDist <= _searchCellDist;
			});

			if (target != null)
				Engage(target, alertPack: true);
		}

		long _nextMoveTick = 0;
		long _nextAttackTick = 0;

		void UpdateCombat()
		{
			SelectTarget();
			if (_target == null || (CellPos - SpawnCell).cellDist > _leashCellDist)
			{
				StartReturn();
				return;
			}

			long now = Environment.TickCount64;
			// 한 칸 이동이 끝나기 전에는 다음 행동을 하지 않는다(클라 보간과 같은 시간)
			if (_nextMoveTick > now)
				return;

			Vector2Int toTarget = _target.CellPos - CellPos;

			// 유지 최소 거리보다 가까우면 물러선다. 물러날 칸이 없으면 제자리에서 공격한다.
			if (toTarget.cellDist < _keepMin && Statuses.CanMove && TryRetreat(_target.CellPos))
				return;

			// 근접은 벽 모서리 너머를 치지 못하고(이동과 같은 모서리 규칙), 원거리는 시야가 트여야 친다.
			// 시야가 막혔으면 공격하지 않고 다가간다.
			bool inRange = toTarget.cellDist <= _basicRange
				&& Room.Map.HasLineOfSight(CellPos, _target.CellPos);

			if (inRange)
			{
				// 제자리에 서서 대상을 바라본다
				MoveDir lookDir = GetDirFromVec(toTarget);
				if (State != CreatureState.Idle || Dir != lookDir)
				{
					State = CreatureState.Idle;
					Dir = lookDir;
					BroadcastMove();
				}

				if (_nextAttackTick > now)
					return;
				_nextAttackTick = now + (long)(1000 * Config(ConfigBasicInterval) / CurrentAttackSpeed);
				BasicAttack();
				return;
			}

			StepToward(_target.CellPos);
		}

		// 평타: 사거리(monsterTypes.basicRange) 안의 위협 1위에게 즉발 피해. 간격 = configs 20032 ÷ 공격 속도, 계수 = monsterTypes.basicCoef.
		void BasicAttack()
		{
			S_Skill skill = new S_Skill() { Info = new SkillInfo() };
			skill.ObjectId = Id;
			skill.Info.SkillId = BasicMeleeSkillId;
			Room.Broadcast(skill);

			Combat.Hit(this, _target, _basicCoef);
		}

		// from에서 멀어지는 이웃 칸으로 한 칸 물러선다. 복귀 거리 밖·시야가 끊기는 칸으로는 물러나지 않는다.
		// 가장 멀어지는 칸(칸 거리 → 실제 거리 순)을 고른다. 반환: 물러섰는지
		bool TryRetreat(Vector2Int from)
		{
			int currentDist = (from - CellPos).cellDist;
			Vector2Int best = CellPos;
			int bestDist = currentDist;
			int bestSqr = (from - CellPos).sqrMagnitude;

			for (int dy = -1; dy <= 1; dy++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;
					Vector2Int next = new Vector2Int(CellPos.x + dx, CellPos.y + dy);
					if (Room.Map.CanGo(next, true, blockPlayers: true) == false || Room.Map.IsCornerBlocked(CellPos, next))
						continue;
					if ((next - SpawnCell).cellDist > _leashCellDist)
						continue;
					// 시야가 끊기는 칸으로는 물러나지 않는다(물러났다가 다시 다가오는 반복 방지)
					if (Room.Map.HasLineOfSight(next, from) == false)
						continue;

					int dist = (from - next).cellDist;
					int sqr = (from - next).sqrMagnitude;
					if (dist > bestDist || (dist == bestDist && dist > currentDist && sqr > bestSqr))
					{
						best = next;
						bestDist = dist;
						bestSqr = sqr;
					}
				}
			}

			if (bestDist <= currentDist)
				return false;

			MoveOneCell(best);
			return true;
		}

		// dest 쪽으로 한 칸. 다른 적은 돌아가고(checkObjects), 길이 없으면 적을 무시한 길로 다가가 막히면 기다린다.
		enum StepResult { Moved, Blocked, NoPath }

		StepResult StepToward(Vector2Int dest)
		{
			// 결박·버팀: 움직이지 않고 그쪽을 바라보며 기다린다
			if (Statuses.CanMove == false)
			{
				MoveDir lookDir = GetDirFromVec(dest - CellPos);
				if (State != CreatureState.Idle || Dir != lookDir)
				{
					State = CreatureState.Idle;
					Dir = lookDir;
					BroadcastMove();
				}
				return StepResult.Blocked;
			}

			List<Vector2Int> path = Room.Map.FindPath(CellPos, dest, checkObjects: true);
			if (path.Count < 2)
				path = Room.Map.FindPath(CellPos, dest, checkObjects: false);
			if (path.Count < 2)
				return StepResult.NoPath;

			Vector2Int next = path[1];
			Vector2Int step = next - CellPos;
			bool blocked = Room.Map.CanGo(next, true, blockPlayers: true) == false || Room.Map.IsCornerBlocked(CellPos, next);
			if (blocked)
			{
				// 다음 칸이 막혔으면 그쪽을 바라보며 기다린다
				if (State != CreatureState.Idle)
				{
					State = CreatureState.Idle;
					Dir = GetDirFromVec(step);
					BroadcastMove();
				}
				return StepResult.Blocked;
			}

			MoveOneCell(next);
			return StepResult.Moved;
		}

		// 비어 있는 이웃 칸 next로 한 칸 옮긴다(호출 쪽에서 막힘을 확인한다).
		// 대각선 한 칸은 configs 20030 배율만큼 다음 이동까지 더 기다린다(클라 보간과 같은 배율).
		void MoveOneCell(Vector2Int next)
		{
			Vector2Int step = next - CellPos;
			int moveTick = (int)(1000 / CurrentMoveSpeed);
			if (step.x != 0 && step.y != 0)
				moveTick = (int)(moveTick * Config(ConfigDiagonalTimeScale));
			_nextMoveTick = Environment.TickCount64 + moveTick;

			State = CreatureState.Moving;
			Dir = GetDirFromVec(step);
			Room.Map.ApplyMove(this, next);
			BroadcastMove();
		}

		// 복귀: 위협을 비우고 배치 칸으로 걸어서 돌아간다. 돌아가는 동안은 맞지 않고 탐지하지 않는다.
		void StartReturn()
		{
			_returning = true;
			Statuses.Clear();
			_threat.Clear();
			_target = null;
		}

		void UpdateReturn()
		{
			long now = Environment.TickCount64;
			if (_nextMoveTick > now)
				return;

			if (CellPos.x == SpawnCell.x && CellPos.y == SpawnCell.y)
			{
				FinishReturn();
				return;
			}

			if (StepToward(SpawnCell) != StepResult.NoPath)
				return;

			// 돌아갈 길이 아예 없으면 배치 칸이 비었을 때 그 자리로 옮긴다
			if (Room.Map.CanGo(SpawnCell, true, blockPlayers: true))
			{
				Room.Map.ApplyMove(this, SpawnCell);
				FinishReturn();
			}
		}

		// 도착하면 체력을 가득 채우고 대기로 돌아간다(2026-11-23 사용자 결정).
		void FinishReturn()
		{
			_returning = false;
			Hp = Stat.MaxHp;
			Room.Broadcast(new S_ChangeHp() { ObjectId = Id, Hp = Hp });

			State = CreatureState.Idle;
			BroadcastMove();
			_nextSearchTick = Environment.TickCount64 + 1000;
		}

		void BroadcastMove()
		{
			S_Move movePacket = new S_Move();
			movePacket.ObjectId = Id;
			movePacket.PosInfo = PosInfo;
			Room.Broadcast(movePacket);
		}
	}
}
