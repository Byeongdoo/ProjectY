using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class GameRoom : JobSerializer
	{
		public int RoomId { get; set; }

		Dictionary<int, Player> _players = new Dictionary<int, Player>();
		Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
		Dictionary<int, Projectile> _projectiles = new Dictionary<int, Projectile>();

		public Map Map { get; private set; } = new Map();

		public void Init(int mapId)
		{
			Map.LoadMap(mapId);

			// TEMP
			Monster monster = ObjectManager.Instance.Add<Monster>();
			monster.CellPos = new Vector2Int(5, 5);
			EnterGame(monster);
		}

		// 누군가 주기적으로 호출해줘야 한다
		public void Update()
		{
			foreach (Monster monster in _monsters.Values)
			{
				monster.Update();
			}

			foreach (Projectile projectile in _projectiles.Values)
			{
				projectile.Update();
			}

			Flush();
		}

		public void EnterGame(GameObject gameObject)
		{
			if (gameObject == null)
				return;

			GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

			if (type == GameObjectType.Player)
			{
				Player player = gameObject as Player;
				_players.Add(gameObject.Id, player);
				player.Room = this;

				Map.ApplyMove(player, new Vector2Int(player.CellPos.x, player.CellPos.y));

				// 본인한테 정보 전송
				{
					S_EnterGame enterPacket = new S_EnterGame();
					enterPacket.Player = player.Info;
					player.Session.Send(enterPacket);

					S_Spawn spawnPacket = new S_Spawn();
					foreach (Player p in _players.Values)
					{
						if (player != p)
							spawnPacket.Objects.Add(p.Info);
					}

					foreach (Monster m in _monsters.Values)
						spawnPacket.Objects.Add(m.Info);

					foreach (Projectile p in _projectiles.Values)
						spawnPacket.Objects.Add(p.Info);

					player.Session.Send(spawnPacket);
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = gameObject as Monster;
				_monsters.Add(gameObject.Id, monster);
				monster.Room = this;

				Map.ApplyMove(monster, new Vector2Int(monster.CellPos.x, monster.CellPos.y));
			}
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = gameObject as Projectile;
				_projectiles.Add(gameObject.Id, projectile);
				projectile.Room = this;
			}
			
			// 타인한테 정보 전송
			{
				S_Spawn spawnPacket = new S_Spawn();
				spawnPacket.Objects.Add(gameObject.Info);
				foreach (Player p in _players.Values)
				{
					if (p.Id != gameObject.Id)
						p.Session.Send(spawnPacket);
				}
			}
		}

		public void LeaveGame(int objectId)
		{
			GameObjectType type = ObjectManager.GetObjectTypeById(objectId);

			if (type == GameObjectType.Player)
			{
				Player player = null;
				if (_players.Remove(objectId, out player) == false)
					return;

				Map.ApplyLeave(player);
				player.Room = null;

				// 본인한테 정보 전송
				{
					S_LeaveGame leavePacket = new S_LeaveGame();
					player.Session.Send(leavePacket);
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = null;
				if (_monsters.Remove(objectId, out monster) == false)
					return;

				Map.ApplyLeave(monster);
				monster.Room = null;
			}
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = null;
				if (_projectiles.Remove(objectId, out projectile) == false)
					return;

				projectile.Room = null;
			}

			// 타인한테 정보 전송
			{
				S_Despawn despawnPacket = new S_Despawn();
				despawnPacket.ObjectIds.Add(objectId);
				foreach (Player p in _players.Values)
				{
					if (p.Id != objectId)
						p.Session.Send(despawnPacket);
				}
			}
		}

		public void HandleMove(Player player, C_Move movePacket)
		{
			if (player == null)
				return;

			PositionInfo movePosInfo = movePacket.PosInfo;
			if (movePosInfo == null)
				return;

			ObjectInfo info = player.Info;

			// 클라이언트가 보낸 상태는 신뢰하지 않는다 (사망/스킬 상태 위조 방지)
			if (movePosInfo.State != CreatureState.Idle && movePosInfo.State != CreatureState.Moving)
				return;

			// 클라이언트는 스킬 종료 시 Idle 상태의 C_Move 로 서버 상태를 되돌리므로 Skill 상태에서의 요청은 허용한다
			if (info.PosInfo.State == CreatureState.Dead)
				return;

			// 다른 좌표로 이동할 경우, 8방향 한 칸 이동으로 갈 수 있는지 체크
			if (movePosInfo.PosX != info.PosInfo.PosX || movePosInfo.PosY != info.PosInfo.PosY)
			{
				if (Map.CanMove(player.CellPos, new Vector2Int(movePosInfo.PosX, movePosInfo.PosY)) == false)
					return;
			}

			info.PosInfo.State = movePosInfo.State;
			info.PosInfo.MoveDir = movePosInfo.MoveDir;
			Map.ApplyMove(player, new Vector2Int(movePosInfo.PosX, movePosInfo.PosY));

			// 다른 플레이어한테도 알려준다 (검증된 서버 쪽 좌표를 보낸다)
			S_Move resMovePacket = new S_Move();
			resMovePacket.ObjectId = player.Info.ObjectId;
			resMovePacket.PosInfo = player.PosInfo;

			Broadcast(resMovePacket);
		}

		public void HandleSkill(Player player, C_Skill skillPacket)
		{
			if (player == null)
				return;

			ObjectInfo info = player.Info;
			if (info.PosInfo.State != CreatureState.Idle)
				return;

			if (skillPacket.Info == null)
				return;

			// 존재하는 스킬인지 먼저 확인한다 (없는 스킬로 상태만 바뀌는 것을 방지)
			Data.Skill skillData = null;
			if (DataManager.SkillDict.TryGetValue(skillPacket.Info.SkillId, out skillData) == false)
				return;

			// 쿨타임 체크
			long now = Environment.TickCount64;
			if (player.NextSkillTick > now)
				return;
			player.NextSkillTick = now + (long)(1000 * skillData.cooldown);

			info.PosInfo.State = CreatureState.Skill;
			S_Skill skill = new S_Skill() { Info = new SkillInfo() };
			skill.ObjectId = info.ObjectId;
			skill.Info.SkillId = skillPacket.Info.SkillId;
			Broadcast(skill);

			// 클라이언트의 Idle 복귀 패킷이 오지 않더라도 쿨타임 후에는 Skill 상태에 갇히지 않게 한다
			PushAfter((int)(1000 * skillData.cooldown), ResetSkillState, player);

			switch (skillData.skillType)
			{
				case SkillType.SkillAuto:
					{
						Vector2Int skillPos = player.GetFrontCellPos(info.PosInfo.MoveDir);
						GameObject target = Map.Find(skillPos);
						if (target != null && target != player)
						{
							target.OnDamaged(player, skillData.damage + player.Stat.Attack);
						}
					}
					break;
				case SkillType.SkillProjectile:
					{
						Arrow arrow = ObjectManager.Instance.Add<Arrow>();
						if (arrow == null)
							return;

						arrow.Owner = player;
						arrow.Data = skillData;
						arrow.PosInfo.State = CreatureState.Moving;
						arrow.PosInfo.MoveDir = player.PosInfo.MoveDir;
						arrow.PosInfo.PosX = player.PosInfo.PosX;
						arrow.PosInfo.PosY = player.PosInfo.PosY;
						arrow.Speed = skillData.projectile.speed;
						Push(EnterGame, arrow);
					}
					break;
			}
		}

		void ResetSkillState(Player player)
		{
			if (player.Room != this)
				return;

			if (player.State == CreatureState.Skill)
				player.State = CreatureState.Idle;
		}

		public Player FindPlayer(Func<GameObject, bool> condition)
		{
			foreach (Player player in _players.Values)
			{
				if (condition.Invoke(player))
					return player;
			}

			return null;
		}

		public void Broadcast(IMessage packet)
		{
			foreach (Player p in _players.Values)
			{
				p.Session.Send(packet);
			}
		}
	}
}
