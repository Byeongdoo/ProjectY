using Google.Protobuf.Protocol;
using System;

namespace Server.Game
{
	// 개발·테스트용 허수아비(C_DebugCommand로 소환). PC 전투(평타·스킬·상태이상·밀어내기)를 망령 AI와 상관없이 확인한다.
	//   - 공격·이동·추격하지 않는다(망령 AI를 돌리지 않음). 상태이상은 걸리고 풀린다
	//   - 쓰러지지 않는다: 체력 0이 되면 바로 가득 채운다
	//   - 위협 표에 오르지 않는다: 플레이어가 교전 중이 되지 않아 비전투 회복도 그대로 확인할 수 있다
	//   - configs 99003초 동안 맞지 않으면 체력을 채우고 상태이상을 풀고 배치 칸으로 돌아간다(측정 반복용)
	// 수치: configs 99001(최대 체력)·99002(방어율). 실제 콘텐츠에 쓰지 않는다.
	public class TrainingDummy : Monster
	{
		const int ConfigMaxHp = 99001;
		const int ConfigDefence = 99002;
		const int ConfigResetDelay = 99003;

		Vector2Int _home;
		long _lastHitTick;

		public void InitDummy(Vector2Int cell)
		{
			Info.TrainingDummy = true;
			Info.Name = "TrainingDummy";
			Stat.Level = 1;
			Stat.Attack = 0;
			Stat.MaxHp = (int)CombatConfig.Get(ConfigMaxHp);
			Stat.Hp = Stat.MaxHp;
			Stat.Speed = 0f;
			_home = cell;
			SpawnCell = cell;
			CellPos = cell;
			State = CreatureState.Idle;
			Dir = MoveDir.Down;
		}

		public override float Defence => CombatConfig.Get(ConfigDefence);

		public override void Update()
		{
			Status.Update();
			if (Room == null)
				return;

			bool dirty = Stat.Hp < Stat.MaxHp || CellPos.x != _home.x || CellPos.y != _home.y;
			if (dirty == false || Environment.TickCount64 - _lastHitTick < (long)(CombatConfig.Get(ConfigResetDelay) * 1000))
				return;

			Status.RemoveAll();
			Stat.Hp = Stat.MaxHp;
			Room.Broadcast(new S_ChangeHp() { ObjectId = Id, Hp = Stat.Hp });
			if (CellPos.x != _home.x || CellPos.y != _home.y)
			{
				if (Room.Map.ApplyMove(this, _home) == false)
					_home = CellPos; // 배치 칸이 막혔으면 지금 칸을 새 배치 칸으로
				Room.Broadcast(new S_Move() { ObjectId = Id, PosInfo = PosInfo, Forced = true });
			}
		}

		// 위협·팩 연동 없이 맞은 시각만 기록한다
		protected override void OnDamageTaken(GameObject attacker, int damage)
		{
			_lastHitTick = Environment.TickCount64;
		}

		// 쓰러지지 않는다
		public override void OnDead(GameObject attacker)
		{
			if (Room == null)
				return;
			Stat.Hp = Stat.MaxHp;
			_lastHitTick = Environment.TickCount64;
			Room.Broadcast(new S_ChangeHp() { ObjectId = Id, Hp = Stat.Hp });
		}
	}
}
