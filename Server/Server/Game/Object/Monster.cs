using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Server.Game
{
	public class Monster : GameObject
	{
		public Monster()
		{
			ObjectType = GameObjectType.Monster;

			// TEMP
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
			_chaseCellDist = info.leashRange;
		}

		float _moveSpeed = 5f, _defence, _critical, _criticalAttack = 1.5f, _attackSpeed = 1f;
		bool _isBoss;
		public override bool IsBoss => _isBoss;
		public override float Defence => _defence;
		public override float CriticalRate => _critical;
		public override float CriticalMultiplier => _criticalAttack;
		protected override float BaseAttackSpeed => _attackSpeed;
		protected override float BaseMoveSpeed => _moveSpeed;

		// 팩 전체가 같은 대상을 노린다. 이미 싸우는 체는 그대로 둔다.
		void AlertPack(Player target)
		{
			if (Pack == null)
				return;
			foreach (Monster m in Pack)
			{
				if (m == this || m.Room != Room || m.State != CreatureState.Idle)
					continue;
				m._target = target;
				m.State = CreatureState.Moving;
			}
		}

		public override void OnDamaged(GameObject attacker, int damage, bool critical, bool redirected = false)
		{
			base.OnDamaged(attacker, damage, critical, redirected);
			if (Room == null || State == CreatureState.Dead)
				return;

			// 탐지 범위 밖에서 맞아도 반응한다
			if (_target == null && attacker is Player p)
			{
				_target = p;
				if (State == CreatureState.Idle)
					State = CreatureState.Moving;
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

			S_Die diePacket = new S_Die();
			diePacket.ObjectId = Id;
			diePacket.AttackerId = attacker.Id;
			Room.Broadcast(diePacket);

			Status.Clear();
			State = CreatureState.Dead;
			Room.LeaveGame(Id);
			ObjectManager.Instance.Remove(Id);
		}

		// FSM (Finite State Machine)
		public override void Update()
		{
			Status.Update();
			if (Room == null || State == CreatureState.Dead)
				return;

			// 기절: 아무것도 하지 않는다
			if (Status.Has(StatusId.Stun))
				return;

			// 도발: 건 플레이어만 노린다(위협 무시, '스킬 시스템' 2-5)
			StatusInstance taunt = Status.Get(StatusId.Taunt);
			if (taunt != null && taunt.Source is Player taunter && taunter.IsAlive && taunter.Room == Room && _target != taunter)
			{
				_target = taunter;
				if (State == CreatureState.Idle)
					State = CreatureState.Moving;
			}

			switch (State)
			{
				case CreatureState.Idle:
					UpdateIdle();
					break;
				case CreatureState.Moving:
					UpdateMoving();
					break;
				case CreatureState.Skill:
					UpdateSkill();
					break;
				case CreatureState.Dead:
					UpdateDead();
					break;
			}
		}

		Player _target;
		int _searchCellDist = 10;
		int _chaseCellDist = 20;

		long _nextSearchTick = 0;
		protected virtual void UpdateIdle()
		{
			if (_nextSearchTick > Environment.TickCount64)
				return;
			_nextSearchTick = Environment.TickCount64 + 1000;

			Player target = Room.FindPlayer(p =>
			{
				Vector2Int dir = p.CellPos - CellPos;
				return dir.cellDist <= _searchCellDist;
			});

			if (target == null)
				return;

			_target = target;
			State = CreatureState.Moving;
			AlertPack(target);
		}

		int _skillRange = 1;
		long _nextMoveTick = 0;
		const int ConfigDiagonalTimeScale = 20030; // 대각선 1칸 소요 시간 배율
		protected virtual void UpdateMoving()
		{
			if (_nextMoveTick > Environment.TickCount64)
				return;
			int moveTick = (int)(1000 / Speed);
			_nextMoveTick = Environment.TickCount64 + moveTick;

			if (_target == null || _target.Room != Room)
			{
				_target = null;
				State = CreatureState.Idle;
				BroadcastMove();
				return;
			}

			Vector2Int dir = _target.CellPos - CellPos;
			int dist = dir.cellDist;
			if (dist == 0 || dist > _chaseCellDist)
			{
				_target = null;
				State = CreatureState.Idle;
				BroadcastMove();
				return;
			}

			List<Vector2Int> path = Room.Map.FindPath(CellPos, _target.CellPos, checkObjects: false);
			if (path.Count < 2 || path.Count > _chaseCellDist)
			{
				_target = null;
				State = CreatureState.Idle;
				BroadcastMove();
				return;
			}

			// 스킬로 넘어갈지 체크
			if (dist <= _skillRange)
			{
				_coolTick = 0;
				State = CreatureState.Skill;
				return;
			}

			// 결박·버팀이면 그 자리에서 기다린다
			if (CanMove == false)
				return;

			// 이동. 대각선 한 칸은 configs 20030 배율만큼 다음 이동까지 더 기다린다(클라 보간과 같은 배율, 화면상 속도를 직선과 맞춤)
			Vector2Int step = path[1] - CellPos;
			Dir = GetDirFromVec(step);
			Room.Map.ApplyMove(this, path[1]);
			if (step.x != 0 && step.y != 0)
				_nextMoveTick = Environment.TickCount64 + (int)(moveTick * Table.TableManager.Configs.Get(ConfigDiagonalTimeScale).value[0]);
			BroadcastMove();
		}

		void BroadcastMove()
		{
			// 다른 플레이어한테도 알려준다
			S_Move movePacket = new S_Move();
			movePacket.ObjectId = Id;
			movePacket.PosInfo = PosInfo;
			Room.Broadcast(movePacket);
		}

		long _coolTick = 0;
		const float MonsterBasicCoef = 1f; // 평타 계수 기준값(피해 = 공격력 × 계수). 망령별 계수가 테이블에 생기면 교체
		protected virtual void UpdateSkill()
		{
			if (_coolTick == 0)
			{
				// 유효한 타겟인지
				if (_target == null || _target.Room != Room || _target.Hp == 0)
				{
					_target = null;
					State = CreatureState.Moving;
					BroadcastMove();
					return;
				}

				// 스킬이 아직 사용 가능한지
				Vector2Int dir = (_target.CellPos - CellPos);
				int dist = dir.cellDist;
				bool canUseSkill = (dist <= _skillRange);
				if (canUseSkill == false)
				{
					State = CreatureState.Moving;
					BroadcastMove();
					return;
				}

				// 타게팅 방향 주시
				MoveDir lookDir = GetDirFromVec(dir);
				if (Dir != lookDir)
				{
					Dir = lookDir;
					BroadcastMove();
				}

				// 평타: 공격력 × 계수(1.0), 간격 = 평타 간격(configs 20032) / 공격 속도.
				// TODO 망령별 공격 계수·패턴은 '망령(적) 시스템' 구현 때 테이블에서 읽는다
				Combat.Damage(this, _target, MonsterBasicCoef);

				S_Skill skill = new S_Skill() { Info = new SkillInfo() { SkillId = Combat.BasicSkillId, TargetId = _target.Id } };
				skill.ObjectId = Id;
				Room.Broadcast(skill);

				int coolTick = (int)(1000 * CombatConfig.Get(CombatConfig.BasicInterval) / Math.Max(0.05f, AttackSpeed));
				_coolTick = Environment.TickCount64 + coolTick;
			}

			if (_coolTick > Environment.TickCount64)
				return;

			_coolTick = 0;
		}

		protected virtual void UpdateDead()
		{

		}
	}
}
