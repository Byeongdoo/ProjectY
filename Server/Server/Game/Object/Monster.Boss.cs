using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 보스 전용 규칙('보스 시스템'). 보스는 characterInfos.type = Boss인 Monster다.
	//   교전: 플레이어가 보스 아레나(levelObjects 13)에 들어오면 시작한다(맞아도 시작). 아레나 밖 탐지는 하지 않는다.
	//   평타: configs 20059초마다 계수 20060(탱커 압박 가정 0.8/초에 맞춤, 2026-11-23 사용자 결정).
	//   스킬: bossSkills. 쿨이 찬 스킬 중 ID 순, 판정 뒤 최소 3초(20061). 2페이즈부터는 1+2페이즈 스킬을 모두 쓴다(사용자 결정).
	//   페이즈: bossPhases.hpStart 이하로 체력이 내려가면 다음 페이즈. 전환 순간 2초 무적(20024), 시전 중이던 스킬은 취소.
	//   전멸 재시작: 교전이 풀리면 시작 위치로 돌아가 체력·페이즈·스킬 대기 시간을 초기화한다('저승 던전 구조' 2-7).
	public partial class Monster
	{
		const int ConfigBossSkillGap = CombatConfig.BossSkillGap;

		int _phase = 1;
		long _nextBossSkillTick;
		long _invulnerableUntilTick;
		List<(int phase, float hpStart)> _phases = new List<(int, float)>();

		// 보스 아레나(서버 좌표, 양 끝 포함). 보스가 아닌 적은 쓰지 않는다
		public Vector2Int ArenaMin { get; set; }
		public Vector2Int ArenaMax { get; set; }
		public bool HasArena { get; set; }

		public int Phase => _phase;

		// 마지막으로 알린 교전 상태. 바뀌면 S_BossState를 보낸다
		bool _engagedNotified;

		public S_BossState MakeBossState()
		{
			return new S_BossState() { ObjectId = Id, Engaged = IsEngaged, Phase = _phase };
		}

		void BroadcastBossState()
		{
			if (_isBoss == false || Room == null)
				return;
			_engagedNotified = IsEngaged;
			Room.Broadcast(MakeBossState());
		}

		// 매 틱: 교전이 시작되거나 끝났으면 알린다(보스 체력바 표시·숨김)
		void NotifyBossEngageChange()
		{
			if (_isBoss && _engagedNotified != IsEngaged)
				BroadcastBossState();
		}

		void LoadBossPhases()
		{
			_phases = Table.TableManager.BossPhases.Rows
				.Where(r => r.boss == CharacterId)
				.OrderBy(r => r.phase)
				.Select(r => (r.phase, r.hpStart))
				.ToList();
		}

		bool InArena(Vector2Int c)
		{
			return HasArena && c.x >= ArenaMin.x && c.x <= ArenaMax.x && c.y >= ArenaMin.y && c.y <= ArenaMax.y;
		}

		// 아레나에 들어온 살아 있는 플레이어
		Player FindPlayerInArena()
		{
			return Room.FindPlayer(p => p.IsAlive && InArena(p.CellPos)) as Player;
		}

		bool IsInvulnerable => _isBoss && Environment.TickCount64 < _invulnerableUntilTick;

		// 피해를 받은 뒤 페이즈 전환 확인
		void CheckPhase()
		{
			if (_isBoss == false || Stat.MaxHp <= 0)
				return;

			float ratio = (float)Hp / Stat.MaxHp;
			int next = _phase;
			foreach ((int phase, float hpStart) in _phases)
				if (phase > next && ratio <= hpStart)
					next = phase;
			if (next == _phase)
				return;

			_phase = next;
			CancelCast();
			long now = Environment.TickCount64;
			_invulnerableUntilTick = now + (long)(1000 * CombatConfig.Get(CombatConfig.BossPhaseInvulnerable));
			_nextBossSkillTick = Math.Max(_nextBossSkillTick, _invulnerableUntilTick);
			// 새 페이즈 스킬도 자기 쿨타임만큼 기다린 뒤 처음 쓴다
			foreach (SkillSpec s in _skills)
				if (s.Phase == _phase)
					_skillReadyTick[s.Id] = now + (long)(1000 * s.CoolTime);
			Console.WriteLine($"[보스 {CharacterId}] {_phase}페이즈 (체력 {ratio:P0})");
			BroadcastBossState();
		}

		// 전멸 재시작·복귀 완료 때
		void ResetBoss()
		{
			_phase = 1;
			_nextBossSkillTick = 0;
			_invulnerableUntilTick = 0;
			_skillReadyTick.Clear();
			BroadcastBossState();
		}

		long BasicIntervalMs()
		{
			float interval = _isBoss ? CombatConfig.Get(CombatConfig.BossBasicInterval) : CombatConfig.Get(CombatConfig.BasicInterval);
			return (long)(1000 * interval / Math.Max(0.05f, AttackSpeed));
		}

		float BasicCoef => _isBoss ? CombatConfig.Get(CombatConfig.BossBasicCoef) : _basicCoef;
	}
}
