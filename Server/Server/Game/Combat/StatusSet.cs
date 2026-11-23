using Google.Protobuf.Protocol;
using System;
using System.Collections.Generic;

namespace Server.Game
{
	public class StatusInstance
	{
		public int StatusId;
		public long ExpireTick;
		public int Stack = 1;
		public GameObject Source;   // 건 쪽(수호를 대신 받는 시전자, 도발 대상, 영화 피해 기준)
		public long NextTick;       // 지속 피해 다음 틱
	}

	// 한 오브젝트에 붙은 상태이상. 같은 상태이상을 다시 걸면 지속시간을 갱신하고 최대 중첩까지 쌓는다('스킬 시스템' 2-5).
	// 시작·갱신·해제는 룸 전체에 S_ChangeStatus로 알린다.
	public class StatusSet
	{
		readonly GameObject _owner;
		readonly Dictionary<int, StatusInstance> _list = new Dictionary<int, StatusInstance>();

		public StatusSet(GameObject owner)
		{
			_owner = owner;
		}

		public bool Has(int statusId) => _list.ContainsKey(statusId);

		public StatusInstance Get(int statusId)
		{
			_list.TryGetValue(statusId, out StatusInstance s);
			return s;
		}

		// 효과 값 × 중첩. 없으면 0
		public float Value(int statusId)
		{
			if (_list.TryGetValue(statusId, out StatusInstance s) == false)
				return 0f;
			return Table.TableManager.StatusEffects.Get(statusId).value * s.Stack;
		}

		public static bool IsHarmful(int statusId)
		{
			return Table.TableManager.StatusEffects.Get(statusId).kind == (int)StatusKind.Harmful;
		}

		public void Apply(int statusId, float durationSec, GameObject source)
		{
			if (_owner.Room == null || durationSec <= 0f)
				return;
			if (Table.TableManager.StatusEffects.TryGet(statusId, out Table.StatusEffectsRow row) == false)
				return;

			// 보스는 결박·둔화·기절이 절반만 걸린다(configs 20022)
			if (_owner.IsBoss && (statusId == StatusId.Root || statusId == StatusId.Slow || statusId == StatusId.Stun))
				durationSec *= CombatConfig.Get(CombatConfig.BossControlRate);

			long now = Environment.TickCount64;
			if (_list.TryGetValue(statusId, out StatusInstance s))
			{
				s.Stack = Math.Min(s.Stack + 1, Math.Max(1, row.maxStack));
				s.Source = source;
			}
			else
			{
				s = new StatusInstance() { StatusId = statusId, Source = source, NextTick = now + 1000 };
				_list.Add(statusId, s);
			}
			s.ExpireTick = now + (long)(durationSec * 1000);

			Broadcast(s, durationSec);
			_owner.OnStatusChanged(statusId);
		}

		public void Remove(int statusId)
		{
			if (_list.Remove(statusId, out StatusInstance s) == false)
				return;
			Broadcast(s, 0f);
			_owner.OnStatusChanged(statusId);
		}

		public void RemoveHarmful()
		{
			List<int> harmful = new List<int>();
			foreach (int id in _list.Keys)
				if (IsHarmful(id))
					harmful.Add(id);
			foreach (int id in harmful)
				Remove(id);
		}

		// 룸을 떠나거나 쓰러질 때. 알리지 않는다(오브젝트가 사라지거나 다시 들어온다)
		public void Clear()
		{
			_list.Clear();
		}

		// 만료 처리와 지속 피해(영화: 초당 시전자 공격력 × 값 × 중첩)
		public void Update()
		{
			if (_list.Count == 0)
				return;

			long now = Environment.TickCount64;
			List<int> expired = null;
			foreach (StatusInstance s in _list.Values)
			{
				if (s.ExpireTick <= now)
				{
					(expired ??= new List<int>()).Add(s.StatusId);
					continue;
				}

				if (s.StatusId == StatusId.Burn && s.NextTick <= now)
				{
					s.NextTick = now + 1000;
					if (s.Source != null)
						Combat.Damage(s.Source, _owner, Value(StatusId.Burn));
				}
			}

			if (expired != null)
				foreach (int id in expired)
					Remove(id);
		}

		void Broadcast(StatusInstance s, float durationSec)
		{
			if (_owner.Room == null)
				return;
			_owner.Room.Broadcast(new S_ChangeStatus()
			{
				ObjectId = _owner.Id,
				StatusId = s.StatusId,
				Duration = durationSec,
				Stack = s.Stack,
			});
		}
	}
}
