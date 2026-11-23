using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 망령(적). 서버 권위로 탐지·추격·평타·특수기·복귀를 한다('망령(적) 시스템' 2-5).
	//   대기 → (탐지/피격) → 추격 → 공격 → (복귀 거리 초과/교전 상대 없음) → 복귀 → 대기
	//   대상은 위협 1위('캐릭터 조작 및 전투' 2-9). 팩 하나가 반응하면 팩 전체가 함께 반응한다.
	// 개체별 특수기(2-5-1)는 Monster.Skill.cs.
	public partial class Monster : GameObject
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

		// 같은 팩(spawnGroups 1회분). 한 체가 탐지·피격되면 팩 전체가 반응한다('망령(적) 시스템' 2-5). 특수기 동시 시전 제한도 맡는다.
		public MonsterPack Pack { get; set; }

		// 팩 안 스폰 순서(0부터). 특수기 첫 사용을 분산한다.
		public int PackIndex { get; set; }

		// 배치된 칸. 복귀 거리는 이 칸에서 잰다.
		public Vector2Int SpawnCell { get; set; }

		const int ConfigDiagonalTimeScale = 20030; // 대각선 1칸 소요 시간 배율

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
			_moveSpeed = stat.moveSpeed;
			_defence = stat.defence;
			_critical = stat.critical;
			_criticalAttack = stat.criticalAttack;
			_attackSpeed = stat.attackSpeed;
			_isBoss = info.type == (int)CharacterType.Boss;
			_searchCellDist = info.scanRange;
			_leashCellDist = info.leashRange;

			if (Table.TableManager.MonsterTypes.TryGet(info.monsterType, out Table.MonsterTypesRow type))
			{
				_basicRange = Math.Max(1, type.basicRange);
				_keepMin = type.keepMin;
				_basicCoef = type.basicCoef;
			}

			LoadSkills();
		}

		float _moveSpeed = 5f, _defence, _critical, _criticalAttack = 1.5f, _attackSpeed = 1f;
		bool _isBoss;
		public override bool IsBoss => _isBoss;
		public override float Defence => _defence;
		public override float CriticalRate => _critical;
		public override float CriticalMultiplier => _criticalAttack;
		protected override float BaseAttackSpeed => _attackSpeed;
		protected override float BaseMoveSpeed => _moveSpeed;

		#region 위협

		// ---- 위협 ('캐릭터 조작 및 전투' 2-9) ----
		// 피해 1 = 위협 configs 20036, 회복 1 = 20037(교전 중인 망령 전부에게), × 원혼 위협 배율(무명장수 ×3).
		// 대상은 위협 1위. 새 후보가 현재 대상의 configs 20038배 이상일 때만 바꾼다(핑퐁 방지).
		// 표에 오른 플레이어 = 이 망령과 교전 중. 탐지·팩 연동은 위협 0으로 올린다.
		readonly Dictionary<Player, float> _threat = new Dictionary<Player, float>();
		Player _target;
		bool _returning;

		public bool IsEngaged => _threat.Count > 0;
		public bool IsEngagedWith(Player player) => _threat.ContainsKey(player);

		public void AddThreat(Player player, float amount)
		{
			// 복귀 중에는 교전하지 않는다
			if (_returning || player == null || player.IsAlive == false || player.Room != Room)
				return;
			if (IsEngaged == false)
				_engagedAtTick = Environment.TickCount64;
			_threat.TryGetValue(player, out float current);
			_threat[player] = current + amount * player.ThreatRate;
			if (_target == null)
				_target = player;
			if (State == CreatureState.Idle)
				State = CreatureState.Moving;
		}

		public void RemoveThreat(Player player)
		{
			_threat.Remove(player);
			if (_target == player)
				_target = null;
		}

		// 전멸 재시작 등. 교전을 끝낸다. 배치 칸에서 떨어져 있으면 다음 틱에 복귀한다
		public void ClearThreat()
		{
			CancelCast();
			_threat.Clear();
			_target = null;
			if (State != CreatureState.Dead && State != CreatureState.Idle)
			{
				State = CreatureState.Idle;
				BroadcastMove();
			}
		}

		void RefreshTarget()
		{
			List<Player> gone = null;
			foreach (Player p in _threat.Keys)
				if (p.IsAlive == false || p.Room != Room)
					(gone ??= new List<Player>()).Add(p);
			if (gone != null)
				foreach (Player p in gone)
					RemoveThreat(p);

			if (_threat.Count == 0)
			{
				_target = null;
				return;
			}

			Player top = null;
			float topThreat = float.MinValue;
			foreach (KeyValuePair<Player, float> kv in _threat)
				if (kv.Value > topThreat)
				{
					top = kv.Key;
					topThreat = kv.Value;
				}

			if (_target == null || _threat.ContainsKey(_target) == false)
				_target = top;
			else if (top != _target && topThreat >= _threat[_target] * CombatConfig.Get(CombatConfig.ThreatSwitchRate))
				_target = top;
		}

		// 팩 안의 한 체가 탐지·피격되면 팩 전체가 그 플레이어와 교전한다('망령(적) 시스템' 2-5)
		void AlertPack(Player target)
		{
			if (Pack == null)
				return;
			foreach (Monster m in Pack.Members)
			{
				if (m == this || m.Room != Room || m.State == CreatureState.Dead)
					continue;
				if (m.IsEngagedWith(target) == false)
					m.AddThreat(target, 0f);
			}
		}

		#endregion

		// 복귀 중에는 맞지 않는다
		public override void OnDamaged(GameObject attacker, int damage, bool critical, bool redirected = false)
		{
			if (_returning)
				return;
			base.OnDamaged(attacker, damage, critical, redirected);
		}

		protected override void OnDamageTaken(GameObject attacker, int damage)
		{
			// 탐지 범위 밖에서 맞아도 반응한다
			if (attacker is Player p)
			{
				p.LastCombatTick = Environment.TickCount64;
				bool first = IsEngagedWith(p) == false;
				AddThreat(p, damage * CombatConfig.Get(CombatConfig.ThreatPerDamage));
				if (first)
					AlertPack(p);
			}
		}

		// 망령은 쓰러지면 그 판에서 사라진다(레거시처럼 그 자리에서 되살아나지 않는다).
		public override void OnDead(GameObject attacker)
		{
			if (Room == null)
				return;

			// 막타 친 플레이어만 신력을 번다('캐릭터 조작 및 전투' 2-8)
			if (attacker is Player killer)
				killer.AddSpirit(CombatConfig.Get(CombatConfig.SpiritOnKill));

			// 처치 보상(장비·혼백)은 룸의 플레이어가 각자 굴린다
			Room.GrantKillRewards(this);

			CancelCast();
			EndFlight();

			S_Die diePacket = new S_Die();
			diePacket.ObjectId = Id;
			diePacket.AttackerId = attacker.Id;
			Room.Broadcast(diePacket);

			Status.Clear();
			State = CreatureState.Dead;
			_threat.Clear();
			_target = null;
			Room.LeaveGame(Id);
			ObjectManager.Instance.Remove(Id);
		}

		// FSM
		public override void Update()
		{
			Status.Update();
			if (Room == null || State == CreatureState.Dead)
				return;

			// 이미 날아간 투사체는 시전자 상태와 상관없이 끝까지 간다
			UpdateFlight();
			if (Room == null)
				return;

			// 기절: 아무것도 하지 않고 시전 중인 특수기를 취소한다
			if (Status.Has(StatusId.Stun))
			{
				CancelCast();
				return;
			}

			if (_returning)
			{
				UpdateReturn();
				return;
			}

			// 대상 = 위협 1위. 도발 중이면 건 플레이어만 노린다(위협 무시, '스킬 시스템' 2-5)
			RefreshTarget();
			StatusInstance taunt = Status.Get(StatusId.Taunt);
			if (taunt != null && taunt.Source is Player taunter && taunter.IsAlive && taunter.Room == Room)
			{
				if (IsEngagedWith(taunter) == false)
					AddThreat(taunter, 0f);
				_target = taunter;
			}

			if (IsEngaged)
				UpdateCombat();
			else
				UpdateIdle();
		}

		long _nextSearchTick = 0;
		void UpdateIdle()
		{
			// 교전이 끝났는데 배치 칸 밖이면 돌아간다(추격 포기·전멸 재시작 뒤)
			if (CellPos.x != SpawnCell.x || CellPos.y != SpawnCell.y)
			{
				StartReturn();
				return;
			}

			long now = Environment.TickCount64;
			if (_nextSearchTick > now)
				return;
			_nextSearchTick = now + 1000;

			Player target = Room.FindPlayer(p =>
			{
				Vector2Int dir = p.CellPos - CellPos;
				return p.IsAlive && dir.cellDist <= _searchCellDist;
			}) as Player;

			if (target == null)
				return;

			AddThreat(target, 0f);
			_target = target;
			AlertPack(target);
		}

		long _nextMoveTick = 0;
		long _nextAttackTick = 0;

		void UpdateCombat()
		{
			// 배치 칸에서 복귀 거리(leashRange)보다 멀어지면 교전을 끝내고 돌아간다
			if (_target == null || (CellPos - SpawnCell).cellDist > _leashCellDist)
			{
				StartReturn();
				return;
			}

			long now = Environment.TickCount64;
			// 한 칸 이동이 끝나기 전에는 다음 행동을 하지 않는다(클라 보간과 같은 시간)
			if (_nextMoveTick > now)
				return;

			// 특수기: 예고 중·회수 중이거나 이번 틱에 시전을 시작했으면 평타·이동을 하지 않는다(특수기는 평타를 대체)
			if (UpdateSpecial(now))
				return;

			Vector2Int toTarget = _target.CellPos - CellPos;

			// 유지 최소 거리보다 가까우면 물러선다. 물러날 칸이 없으면 제자리에서 공격한다.
			if (toTarget.cellDist < _keepMin && CanMove && TryRetreat(_target.CellPos))
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
				_nextAttackTick = now + (long)(1000 * CombatConfig.Get(CombatConfig.BasicInterval) / Math.Max(0.05f, AttackSpeed));
				BasicAttack();
				return;
			}

			// 대상에게 갈 길이 없으면 그 플레이어와의 교전을 끝낸다. 남은 상대가 없으면 다음 틱에 복귀한다
			if (StepToward(_target.CellPos) == StepResult.NoPath)
				RemoveThreat(_target);
		}

		// 평타: 사거리(monsterTypes.basicRange) 안의 위협 1위에게 즉발 피해. 간격 = configs 20032 ÷ 공격 속도, 계수 = monsterTypes.basicCoef.
		void BasicAttack()
		{
			S_Skill skill = new S_Skill() { Info = new SkillInfo() { SkillId = Combat.BasicSkillId, TargetId = _target.Id } };
			skill.ObjectId = Id;
			Room.Broadcast(skill);

			Combat.Damage(this, _target, _basicCoef);
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
			if (CanMove == false)
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
		// 이동 속도(Stat.Speed)에는 신속·둔화가 이미 반영돼 있다(GameObject.RefreshMoveSpeed).
		void MoveOneCell(Vector2Int next)
		{
			Vector2Int step = next - CellPos;
			int moveTick = (int)(1000 / Math.Max(0.1f, Speed));
			if (step.x != 0 && step.y != 0)
				moveTick = (int)(moveTick * Table.TableManager.Configs.Get(ConfigDiagonalTimeScale).value[0]);
			_nextMoveTick = Environment.TickCount64 + moveTick;

			State = CreatureState.Moving;
			Dir = GetDirFromVec(step);
			Room.Map.ApplyMove(this, next);
			BroadcastMove();
		}

		// 복귀: 위협·상태이상을 비우고 배치 칸으로 걸어서 돌아간다. 돌아가는 동안은 맞지 않고 탐지하지 않는다.
		void StartReturn()
		{
			CancelCast();
			_returning = true;
			Status.RemoveAll();
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
			int healed = Stat.MaxHp - Hp;
			Hp = Stat.MaxHp;
			if (healed > 0)
				Room.Broadcast(new S_ChangeHp() { ObjectId = Id, Hp = Hp, Amount = healed, Heal = true });

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
