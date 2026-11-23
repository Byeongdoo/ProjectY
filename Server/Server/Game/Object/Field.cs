using Google.Protobuf.Protocol;
using System;

namespace Server.Game
{
	// 칸에 깔리는 설치물(착호갑사 덫 놓기). 칸을 막지 않는다.
	// 망령이 그 칸에 들어오면 스킬의 상태이상(결박 등)을 걸고 사라진다. 유지 시간(configs 20052)이 지나도 사라진다('스킬 시스템' 2-8).
	public class Field : GameObject
	{
		public GameObject Owner { get; set; }
		public Table.SpiritSkillsRow Skill { get; private set; }
		long _expireTick;

		public Field()
		{
			ObjectType = GameObjectType.Field;
		}

		public void Init(GameObject owner, Table.SpiritSkillsRow skill, Vector2Int cell, float lifetimeSec)
		{
			Owner = owner;
			Skill = skill;
			Info.SkillId = skill.skillID;
			Info.Name = skill.name;
			CellPos = cell;
			State = CreatureState.Idle;
			_expireTick = Environment.TickCount64 + (long)(lifetimeSec * 1000);
		}

		public override void Update()
		{
			if (Room == null)
				return;

			if (_expireTick <= Environment.TickCount64)
			{
				Room.Push(Room.LeaveGame, Id);
				return;
			}

			if (Room.Map.Find(CellPos) is Monster monster && monster.IsAlive)
			{
				foreach (int statusId in Skill.statusIDs)
					monster.Status.Apply(statusId, Skill.statusTime, Owner);
				Room.Push(Room.LeaveGame, Id);
			}
		}
	}
}
