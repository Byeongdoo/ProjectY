using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	// 저승 던전 한 판의 진행: 혼절·부활, 불씨, 전멸·혼백 등불, 비전투 회복.
	//   '캐릭터 조작 및 전투' 2-11, '저승 던전 구조' 2-6·2-7
	// 지금은 던전별 공용 룸이라 룸이 빌 때 한 판을 초기화한다(쓰러진 망령은 그대로).
	public partial class GameRoom
	{
		int _lanterns;
		int _arenaBonfireUses;
		Vector2Int? _checkpoint;   // 마지막으로 닿은 구간 불씨. 없으면 던전 입구(사용자 결정 2026-11-23)
		bool _wipePending;
		List<Vector2Int> _bonfires;
		List<Vector2Int> _arenaBonfires;
		readonly HashSet<int> _atBonfire = new HashSet<int>(); // 지금 구간 불씨 옆에 있는 플레이어(닿는 순간에만 발동)

		// 비전투 회복을 몇 ms마다 반영해 보낼지(패킷 양 조절용, 게임 수치 아님)
		const int RegenSendIntervalMs = 250;

		void ResetDungeonRun()
		{
			_lanterns = (int)CombatConfig.Get(CombatConfig.Lanterns);
			_arenaBonfireUses = (int)CombatConfig.Get(CombatConfig.ArenaBonfireUses);
			_checkpoint = null;
			_wipePending = false;
			_atBonfire.Clear();
			_bonfires ??= DungeonSpawner.Points(DungeonId, DungeonSpawner.ObjTypeBonfire);
			_arenaBonfires ??= DungeonSpawner.Points(DungeonId, DungeonSpawner.ObjTypeArenaBonfire);
		}

		S_DungeonState DungeonState()
		{
			return new S_DungeonState()
			{
				Lanterns = _lanterns,
				MaxLanterns = (int)CombatConfig.Get(CombatConfig.Lanterns),
				ArenaBonfireUses = _arenaBonfireUses,
				MaxArenaBonfireUses = (int)CombatConfig.Get(CombatConfig.ArenaBonfireUses),
			};
		}

		// ---- 위협 ----

		// 회복 위협은 교전 중인 망령 전부에게 쌓는다
		public void AddHealThreat(Player healer, int healed)
		{
			if (Kind != RoomKind.Dungeon)
				return;
			float threat = healed * CombatConfig.Get(CombatConfig.ThreatPerHeal);
			foreach (Monster m in _monsters.Values)
				if (m.IsAlive && m.IsEngaged)
					m.AddThreat(healer, threat);
		}

		// ---- 혼절 ----

		public void OnPlayerFainted(Player player, GameObject attacker)
		{
			if (player.Room != this || player.State == CreatureState.Dead)
				return;

			if (player.IsCasting)
				CancelCast(player);
			CancelChannel(player);
			player.Status.Clear();
			player.State = CreatureState.Dead;
			player.Stat.Hp = 0;
			float faintTime = CombatConfig.Get(CombatConfig.FaintTime);
			player.FaintEndTick = Environment.TickCount64 + (long)(faintTime * 1000);

			foreach (Monster m in _monsters.Values)
				m.RemoveThreat(player);

			// 쓰러진 몸은 칸을 막지 않는다(망령이 돌아가지 않게). 일어날 때 다시 올린다
			Map.ApplyLeave(player);

			Broadcast(new S_Die() { ObjectId = player.Id, AttackerId = attacker?.Id ?? 0, FaintTime = faintTime });
			CheckWipe();
		}

		// 일으킨다. at이 있으면 그 근처 빈 칸으로 옮긴다
		void Revive(Player player, float hpRate, bool fullSpirit, Vector2Int? at)
		{
			if (player.Room != this || player.State != CreatureState.Dead)
				return;

			player.State = CreatureState.Idle;
			player.FaintEndTick = 0;
			player.LastCombatTick = Environment.TickCount64;
			player.Stat.Hp = Math.Max(1, (int)Math.Round(player.Stat.MaxHp * hpRate));
			if (fullSpirit)
				player.AddSpirit(player.Stat.MaxSpirit);

			// 그 자리(혼 부르기·회혼술)에 망령이 올라섰으면 가까운 빈 칸으로
			Vector2Int cell = at ?? player.CellPos;
			if (at != null || Map.CanGo(cell) == false)
				cell = FreeCellNear(cell);
			Map.ApplyMove(player, cell);

			Broadcast(new S_Revive() { ObjectId = player.Id, Hp = player.Stat.Hp, PosInfo = player.PosInfo });
		}

		// 마지막 불씨(없으면 입구)
		Vector2Int RespawnPoint()
		{
			return _checkpoint ?? PickSpawnCell();
		}

		Vector2Int FreeCellNear(Vector2Int point)
		{
			List<Vector2Int> cells = DungeonSpawner.PickCells(Map, point, 1, _random);
			return cells.Count > 0 ? cells[0] : point;
		}

		// ---- 전멸 ----

		void CheckWipe()
		{
			if (Kind != RoomKind.Dungeon || _wipePending || _players.Count == 0)
				return;
			if (_players.Values.All(p => p.State == CreatureState.Dead))
			{
				_wipePending = true;
				Push(Wipe); // 피해 판정 도중이면 그 판정이 끝난 뒤에 처리한다
			}
		}

		// 전멸: 혼백 등불 1개를 쓰고 마지막 불씨에서 전원 전량 회복 후 재시작. 등불이 없으면 던전 실패 → 삼천도 귀환
		void Wipe()
		{
			_wipePending = false;
			if (_players.Count == 0 || _players.Values.Any(p => p.State != CreatureState.Dead))
				return;

			if (_lanterns <= 0)
			{
				Console.WriteLine($"[{SceneName}] 던전 실패 — 혼백 등불을 모두 썼다");
				foreach (Player p in _players.Values.ToList())
					ReturnToLobby(p);
				return;
			}

			_lanterns--;
			_arenaBonfireUses = (int)CombatConfig.Get(CombatConfig.ArenaBonfireUses); // 보스방 불씨 횟수도 초기화('저승 던전 구조' 2-7)
			foreach (Monster m in _monsters.Values)
				m.ClearThreat();

			Vector2Int point = RespawnPoint();
			foreach (Player p in _players.Values.ToList())
				Revive(p, 1f, true, point);
			Broadcast(DungeonState());
		}

		void ReturnToLobby(Player player)
		{
			GameRoom lobby = RoomManager.Instance.Lobby;
			LeaveGame(player.Id);
			player.Info.SpiritId = 0;
			player.PendingRoom = lobby;
			player.Session.Send(new S_ChangeScene() { SceneName = lobby.SceneName });
		}

		// ---- 매 틱 ----

		void UpdateDungeon(float deltaSec)
		{
			if (Kind != RoomKind.Dungeon)
				return;

			long now = Environment.TickCount64;
			foreach (Player p in _players.Values.ToList())
			{
				if (p.Room != this)
					continue;

				// 혼절 시간이 지나면 마지막 불씨에서 체력 50%로 부활(걸어서 합류해야 한다)
				if (p.State == CreatureState.Dead)
				{
					if (p.FaintEndTick != 0 && p.FaintEndTick <= now)
						Revive(p, CombatConfig.Get(CombatConfig.ReviveHpRate), false, RespawnPoint());
					continue;
				}

				UpdateBonfireTouch(p);
				UpdateOutOfCombatRegen(p, now, deltaSec);
			}
		}

		// 구간 불씨: 옆 칸에 닿는 순간 체크포인트를 갱신하고 파티 전원 체력·신력 전량 회복, 혼절자는 불씨 옆에서 일어난다
		void UpdateBonfireTouch(Player player)
		{
			Vector2Int? touched = null;
			foreach (Vector2Int b in _bonfires)
				if ((player.CellPos - b).cellDist <= 1)
					touched = b;

			if (touched == null)
			{
				_atBonfire.Remove(player.Id);
				return;
			}
			if (_atBonfire.Add(player.Id) == false)
				return;

			_checkpoint = touched;
			foreach (Player p in _players.Values.ToList())
			{
				if (p.State == CreatureState.Dead)
				{
					Revive(p, 1f, true, touched);
					continue;
				}
				RestoreFull(p, spirit: true);
			}
		}

		void RestoreFull(Player p, bool spirit)
		{
			int healed = p.Stat.MaxHp - p.Stat.Hp;
			if (healed > 0)
			{
				p.Stat.Hp = p.Stat.MaxHp;
				Broadcast(new S_ChangeHp() { ObjectId = p.Id, Hp = p.Stat.Hp, Amount = healed, Heal = true });
			}
			if (spirit)
				p.AddSpirit(p.Stat.MaxSpirit);
		}

		// 비전투 회복: 교전이 끝나고 configs 60026초 뒤부터 최대 체력 × 60027 / 초
		void UpdateOutOfCombatRegen(Player p, long now, float deltaSec)
		{
			if (_monsters.Values.Any(m => m.IsEngagedWith(p)))
				p.LastCombatTick = now;

			if (now - p.LastCombatTick < (long)(CombatConfig.Get(CombatConfig.OutOfCombatDelay) * 1000) || p.Stat.Hp >= p.Stat.MaxHp)
			{
				p.RegenCarry = 0f;
				return;
			}

			p.RegenCarry += p.Stat.MaxHp * CombatConfig.Get(CombatConfig.OutOfCombatRegen) * deltaSec;
			if (now < p.NextRegenSendTick || p.RegenCarry < 1f)
				return;

			p.NextRegenSendTick = now + RegenSendIntervalMs;
			int amount = Math.Min((int)p.RegenCarry, p.Stat.MaxHp - p.Stat.Hp);
			p.RegenCarry -= (int)p.RegenCarry;
			p.Stat.Hp += amount;
			// 숫자는 띄우지 않는다(amount 0)
			Broadcast(new S_ChangeHp() { ObjectId = p.Id, Hp = p.Stat.Hp });
		}

		// ---- F 홀드: 혼 부르기·보스방 불씨 ----

		public void HandleHoldInteract(Player player, C_HoldInteract packet)
		{
			if (player == null || player.Room != this || Kind != RoomKind.Dungeon)
				return;

			if (packet.Holding == false)
			{
				CancelChannel(player);
				return;
			}

			if (player.CanAct == false || player.IsCasting || player.IsChanneling)
				return;

			// 옆 칸의 혼절한 파티원이 먼저
			Player fainted = _players.Values
				.Where(o => o != player && o.State == CreatureState.Dead && (o.CellPos - player.CellPos).cellDist <= 1)
				.FirstOrDefault();
			if (fainted != null)
			{
				StartChannel(player, ChannelKind.ChannelCallSoul, fainted.Id, CombatConfig.Get(CombatConfig.CallSoulTime));
				return;
			}

			if (_arenaBonfireUses > 0 && NearArenaBonfire(player))
				StartChannel(player, ChannelKind.ChannelArenaBonfire, 0, CombatConfig.Get(CombatConfig.BonfireHoldTime));
		}

		bool NearArenaBonfire(Player player)
		{
			return _arenaBonfires.Any(b => (player.CellPos - b).cellDist <= 1);
		}

		void StartChannel(Player player, ChannelKind kind, int targetId, float duration)
		{
			player.ChannelKind = kind;
			player.ChannelTargetId = targetId;
			int token = ++player.ChannelToken;
			Broadcast(new S_Channel() { ObjectId = player.Id, Kind = kind, TargetId = targetId, Duration = duration });
			PushAfter((int)(duration * 1000), CompleteChannel, player, token);
		}

		// 이동·혼절·퇴장·F를 뗌·기절이면 끊긴다. 맞는 것으로는 끊기지 않는다(시전과 같은 규칙)
		public void CancelChannel(Player player)
		{
			if (player.IsChanneling == false)
				return;
			EndChannel(player);
		}

		void EndChannel(Player player)
		{
			player.ChannelKind = ChannelKind.ChannelNone;
			player.ChannelTargetId = 0;
			player.ChannelToken++;
			if (player.Room == this)
				Broadcast(new S_Channel() { ObjectId = player.Id, Kind = ChannelKind.ChannelNone, Duration = 0f });
		}

		void CompleteChannel(Player player, int token)
		{
			if (player.Room != this || player.ChannelToken != token || player.IsChanneling == false)
				return;

			ChannelKind kind = player.ChannelKind;
			int targetId = player.ChannelTargetId;
			EndChannel(player);
			if (player.CanAct == false)
				return;

			if (kind == ChannelKind.ChannelCallSoul)
			{
				// 혼 부르기: 그 자리에서 체력 50%로
				if (_players.TryGetValue(targetId, out Player target) && target.State == CreatureState.Dead
					&& (target.CellPos - player.CellPos).cellDist <= 1)
					Revive(target, CombatConfig.Get(CombatConfig.ReviveHpRate), false, null);
			}
			else if (kind == ChannelKind.ChannelArenaBonfire)
			{
				// 보스방 불씨: 파티 전원 체력 전량 회복(신력·부활 없음)
				if (_arenaBonfireUses <= 0 || NearArenaBonfire(player) == false)
					return;
				_arenaBonfireUses--;
				foreach (Player p in _players.Values)
					if (p.State != CreatureState.Dead)
						RestoreFull(p, spirit: false);
				Broadcast(DungeonState());
			}
		}
	}
}
