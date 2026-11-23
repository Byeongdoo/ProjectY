using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;

namespace Server.Game
{
	// statusEffects.statusID. 데이터 키라 코드에서 이름으로 쓴다('스킬 시스템' 2-5).
	public static class StatusId
	{
		public const int Bind = 8001;      // 결박 — 이동 불가, 공격 가능
		public const int SpiritFire = 8002;// 영화 — 지속 피해 (미구현)
		public const int Vulnerable = 8003;// 취약 — 받는 피해 +value
		public const int Slow = 8004;      // 둔화 — 이동·공격 속도 −value
		public const int Stun = 8005;      // 기절 — 이동·공격·스킬·방향 전환 불가
		public const int Empower = 8006;   // 강화 — 공격력 +value
		public const int Haste = 8007;     // 신속 — 이동 속도 +value
		public const int Inspire = 8008;   // 신명 — 스킬 신력 소모 −value (미구현)
		public const int Brace = 8009;     // 버팀 — 받는 피해 −value, 이동 불가
		public const int Guard = 8010;     // 수호 — 피해 일부를 시전자가 대신 받음 (미구현)
		public const int Frenzy = 8011;    // 광기 — 공격 속도 +value
		public const int Taunt = 8012;     // 도발 — 시전자만 노림 (미구현)
		public const int Exhaust = 8013;   // 탈진 — 보스 전용 (미구현)
	}

	// 한 오브젝트에 걸린 상태이상. 효과 판정은 서버가 하고 변화는 S_ChangeStatus로 알린다.
	//   같은 상태이상을 다시 걸면 지속 시간만 갱신(중첩은 maxStack까지). 해로운 것은 해독으로 한 번에 푼다.
	//   여러 효과의 배율은 곱한다(피해 공식과 같은 곱연산, '캐릭터 조작 및 전투' 2-7).
	// TODO 보스 대상 결박·둔화·기절 지속 시간 반감(configs 20022)은 보스 구현 때 적용한다.
	public class StatusSet
	{
		class Active
		{
			public long EndTick;
			public int Stack;
			public float Value;
			public bool Harmful;
		}

		readonly GameObject _owner;
		readonly Dictionary<int, Active> _active = new Dictionary<int, Active>();

		public StatusSet(GameObject owner)
		{
			_owner = owner;
		}

		public bool Has(int statusId)
		{
			return _active.ContainsKey(statusId);
		}

		public bool CanMove { get { return !Has(StatusId.Bind) && !Has(StatusId.Stun) && !Has(StatusId.Brace); } }
		public bool CanAct { get { return !Has(StatusId.Stun); } }   // 공격·스킬
		public bool CanTurn { get { return !Has(StatusId.Stun); } }  // 제자리 방향 전환

		public float AttackRate { get { return Up(StatusId.Empower); } }
		public float MoveSpeedRate { get { return Down(StatusId.Slow) * Up(StatusId.Haste); } }
		public float AttackSpeedRate { get { return Down(StatusId.Slow) * Up(StatusId.Frenzy); } }
		public float DamageTakenRate { get { return Up(StatusId.Vulnerable) * Down(StatusId.Brace); } }

		float Up(int id) { return _active.TryGetValue(id, out Active a) ? 1f + a.Value : 1f; }
		float Down(int id) { return _active.TryGetValue(id, out Active a) ? Math.Max(0f, 1f - a.Value) : 1f; }

		// 상태이상을 건다. 테이블에 없는 ID면 무시한다.
		public void Apply(int statusId, float durationSec)
		{
			if (durationSec <= 0 || _owner.Room == null)
				return;
			if (Table.TableManager.StatusEffects.TryGet(statusId, out Table.StatusEffectsRow row) == false)
				return;

			long end = Environment.TickCount64 + (long)(durationSec * 1000);
			if (_active.TryGetValue(statusId, out Active a))
			{
				a.EndTick = end;
				a.Stack = Math.Min(a.Stack + 1, Math.Max(1, row.maxStack));
			}
			else
			{
				a = new Active() { EndTick = end, Stack = 1, Value = row.value, Harmful = row.harmful != 0 };
				_active.Add(statusId, a);
			}
			Broadcast(statusId, a);
		}

		public void Remove(int statusId)
		{
			if (_active.Remove(statusId))
				Broadcast(statusId, null);
		}

		// 해독: 해로운 상태이상(statusEffects.harmful)을 전부 푼다. 반환 = 푼 개수
		public int RemoveHarmful()
		{
			List<int> ids = new List<int>();
			foreach (KeyValuePair<int, Active> kv in _active)
				if (kv.Value.Harmful)
					ids.Add(kv.Key);
			foreach (int id in ids)
				Remove(id);
			return ids.Count;
		}

		// 혼절·복귀·룸 이동 때 모두 지운다. notify = 룸에 알릴지(룸을 떠날 때는 알릴 필요 없음)
		public void Clear(bool notify = true)
		{
			if (_active.Count == 0)
				return;
			List<int> ids = new List<int>(_active.Keys);
			_active.Clear();
			if (notify)
				foreach (int id in ids)
					Broadcast(id, null);
		}

		// 만료 처리. 룸 Update에서 매 틱 부른다.
		public void Update()
		{
			if (_active.Count == 0)
				return;
			long now = Environment.TickCount64;
			List<int> expired = null;
			foreach (KeyValuePair<int, Active> kv in _active)
				if (kv.Value.EndTick <= now)
					(expired ??= new List<int>()).Add(kv.Key);
			if (expired != null)
				foreach (int id in expired)
					Remove(id);
		}

		// 스폰 패킷(ObjectInfo.statuses)에 지금 걸린 상태이상을 채운다.
		public void FillInfo(ObjectInfo info)
		{
			info.Statuses.Clear();
			long now = Environment.TickCount64;
			foreach (KeyValuePair<int, Active> kv in _active)
				info.Statuses.Add(ToInfo(kv.Key, kv.Value, now));
		}

		static StatusInfo ToInfo(int statusId, Active a, long now)
		{
			if (a == null)
				return new StatusInfo() { StatusId = statusId, RemainMs = 0, Stack = 0 };
			return new StatusInfo() { StatusId = statusId, RemainMs = (int)Math.Max(1, a.EndTick - now), Stack = a.Stack };
		}

		void Broadcast(int statusId, Active a)
		{
			if (_owner.Room == null)
				return;
			S_ChangeStatus packet = new S_ChangeStatus();
			packet.ObjectId = _owner.Id;
			packet.Status = ToInfo(statusId, a, Environment.TickCount64);
			_owner.Room.Broadcast(packet);
		}
	}
}
