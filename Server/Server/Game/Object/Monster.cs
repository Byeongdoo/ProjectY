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
			_searchCellDist = info.scanRange;
			_chaseCellDist = info.leashRange;
		}

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

		public override void OnDamaged(GameObject attacker, int damage)
		{
			base.OnDamaged(attacker, damage);
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

			S_Die diePacket = new S_Die();
			diePacket.ObjectId = Id;
			diePacket.AttackerId = attacker.Id;
			Room.Broadcast(diePacket);

			State = CreatureState.Dead;
			Room.LeaveGame(Id);
			ObjectManager.Instance.Remove(Id);
		}

		// FSM (Finite State Machine)
		public override void Update()
		{
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

				Skill skillData = null;
				DataManager.SkillDict.TryGetValue(1, out skillData);

				// 데미지 판정
				_target.OnDamaged(this, skillData.damage + Stat.Attack);

				// 스킬 사용 Broadcast
				S_Skill skill = new S_Skill() { Info = new SkillInfo() };
				skill.ObjectId = Id;
				skill.Info.SkillId = skillData.id;
				Room.Broadcast(skill);

				// 스킬 쿨타임 적용
				int coolTick = (int)(1000 * skillData.cooldown);
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
