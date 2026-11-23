using Google.Protobuf;
using Google.Protobuf.Protocol;
using ServerCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

class PacketHandler
{
	public static void S_EnterGameHandler(PacketSession session, IMessage packet)
	{
		S_EnterGame enterGamePacket = packet as S_EnterGame;
		Managers.Object.Add(enterGamePacket.Player, myPlayer: true);
	}

	public static void S_LeaveGameHandler(PacketSession session, IMessage packet)
	{
		S_LeaveGame leaveGameHandler = packet as S_LeaveGame;
		Managers.Object.Clear();
	}

	// 서버가 다른 룸(던전 등)으로 옮겼다. 씬을 바꾸고, 새 씬의 MultiScene이 다 불러온 뒤 C_EnterGame을 보낸다.
	public static void S_ChangeSceneHandler(PacketSession session, IMessage packet)
	{
		S_ChangeScene changePacket = packet as S_ChangeScene;
		SpiritSelectPanel.Close();
		Managers.Object.Clear();
		Managers.Scene.LoadScene(changePacket.SceneName);
	}

	// 나루터에서 고른 원혼을 같은 던전의 다른 플레이어가 이미 쓰고 있다. 원혼 선택 화면에서 다시 고른다.
	public static void S_EnterDungeonRejectHandler(PacketSession session, IMessage packet)
	{
		S_EnterDungeonReject rejectPacket = packet as S_EnterDungeonReject;
		SpiritSelectPanel.OnRejected(rejectPacket.SpiritId);
	}

	public static void S_SpawnHandler(PacketSession session, IMessage packet)
	{
		S_Spawn spawnPacket = packet as S_Spawn;
		foreach (ObjectInfo obj in spawnPacket.Objects)
		{
			Managers.Object.Add(obj, myPlayer: false);
		}
	}

	public static void S_DespawnHandler(PacketSession session, IMessage packet)
	{
		S_Despawn despawnPacket = packet as S_Despawn;
		foreach (int id in despawnPacket.ObjectIds)
		{
			Managers.Object.Remove(id);
		}
	}

	public static void S_MoveHandler(PacketSession session, IMessage packet)
	{
		S_Move movePacket = packet as S_Move;

		GameObject go = Managers.Object.FindById(movePacket.ObjectId);
		if (go == null)
			return;

		// 내 캐릭터는 클라가 예측해 움직인다. 서버 위치와 허용 오차(configs 20049)보다 멀어졌을 때만 맞춘다.
		if (Managers.Object.MyPlayer.Id == movePacket.ObjectId)
		{
			int tolerance = Mathf.RoundToInt(Table.TableManager.Configs.Get(20049).value[0]);
			Managers.Object.MyPlayer.OnServerPosition(movePacket.PosInfo, tolerance);
			return;
		}

		BaseController bc = go.GetComponent<BaseController>();
		if (bc == null)
			return;

		// 혼절에서 부활한 파티원은 부활 칸으로 바로 옮긴다(걸어가는 보간 없이)
		bool revived = bc.PosInfo.State == CreatureState.Dead && movePacket.PosInfo.State != CreatureState.Dead;
		bc.PosInfo = movePacket.PosInfo;
		if (revived)
			bc.SyncPos();
	}

	public static void S_SkillHandler(PacketSession session, IMessage packet)
	{
		S_Skill skillPacket = packet as S_Skill;

		GameObject go = Managers.Object.FindById(skillPacket.ObjectId);
		if (go == null)
			return;

		CreatureController cc = go.GetComponent<CreatureController>();
		if (cc != null)
		{
			cc.UseSkill(skillPacket.Info.SkillId);
		}
	}

	public static void S_ChangeHpHandler(PacketSession session, IMessage packet)
	{
		S_ChangeHp changePacket = packet as S_ChangeHp;

		GameObject go = Managers.Object.FindById(changePacket.ObjectId);
		if (go == null)
			return;

		CreatureController cc = go.GetComponent<CreatureController>();
		if (cc != null)
		{
			cc.Hp = changePacket.Hp;
		}
	}

	public static void S_DieHandler(PacketSession session, IMessage packet)
	{
		S_Die diePacket = packet as S_Die;

		GameObject go = Managers.Object.FindById(diePacket.ObjectId);
		if (go == null)
			return;

		CreatureController cc = go.GetComponent<CreatureController>();
		if (cc != null)
		{
			cc.Hp = 0;
			cc.OnDead();
		}
	}
}


