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
		// 스킬이 옮긴 위치(forced)는 바로 맞춘다.
		if (Managers.Object.MyPlayer != null && Managers.Object.MyPlayer.Id == movePacket.ObjectId)
		{
			int tolerance = Mathf.RoundToInt(Table.TableManager.Configs.Get(20049).value[0]);
			Managers.Object.MyPlayer.OnServerPosition(movePacket.PosInfo, tolerance, movePacket.Forced);
			return;
		}

		BaseController bc = go.GetComponent<BaseController>();
		if (bc == null)
			return;

		bc.PosInfo = movePacket.PosInfo;
		if (movePacket.Forced)
		{
			bc.SyncPos(); // 밀려남·돌진은 미끄러지지 않고 그 칸으로
			// 망령이 강제로 옮겨졌다면 밀려난 것(방패 밀치기). 플레이어의 강제 이동은 자기 스킬(돌진·물러서기)이다
			if (bc is MonsterController mc)
				mc.PlayReaction("KNOCKBACK_");
		}
	}

	public static void S_SkillHandler(PacketSession session, IMessage packet)
	{
		S_Skill skillPacket = packet as S_Skill;

		GameObject go = Managers.Object.FindById(skillPacket.ObjectId);
		if (go == null)
			return;

		if (Managers.Object.MyPlayer != null && Managers.Object.MyPlayer.Id == skillPacket.ObjectId)
			MyCombat.OnMySkill(skillPacket.Info.SkillId, skillPacket.CastTime);

		CreatureController cc = go.GetComponent<CreatureController>();
		if (cc != null)
			cc.UseSkill(skillPacket.Info.SkillId, skillPacket.Info.TargetId, skillPacket.CastTime);
	}

	public static void S_CancelSkillHandler(PacketSession session, IMessage packet)
	{
		S_CancelSkill cancelPacket = packet as S_CancelSkill;

		if (Managers.Object.MyPlayer != null && Managers.Object.MyPlayer.Id == cancelPacket.ObjectId)
			MyCombat.OnMyCancel();

		GameObject go = Managers.Object.FindById(cancelPacket.ObjectId);
		CreatureController cc = go != null ? go.GetComponent<CreatureController>() : null;
		if (cc != null)
			cc.CancelSkill();
	}

	// 적 특수기 예고 칸. 표시만 하고 판정은 서버가 한다.
	public static void S_SkillWarningHandler(PacketSession session, IMessage packet)
	{
		SkillWarningView.Handle(packet as S_SkillWarning);
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
			if (changePacket.Amount > 0)
				CombatHud.ShowNumber(cc, changePacket.Amount, changePacket.Critical, changePacket.Heal);
			if (changePacket.Amount > 0 && changePacket.Heal == false)
			{
				OnHit(cc, changePacket.AttackerId, changePacket.Critical);
				// 허수아비 피해 측정
				TrainingDummyMeter meter = cc.GetComponent<TrainingDummyMeter>();
				if (meter != null)
					meter.Add(changePacket.Amount);
			}
		}
	}

	// 타격감('캐릭터 조작 및 전투' 2-10): 맞은 쪽 흰색 플래시·피격 동작, 때린 쪽·맞은 쪽 히트스톱(일반 20009 / 치명타 20010),
	// 내가 때리거나 맞은 치명타면 화면 흔들림
	static void OnHit(CreatureController target, int attackerId, bool critical)
	{
		float stop = CreatureController.HitStopTime(critical);
		target.Flash();
		target.PlayReaction("HIT_");
		target.HitStop(stop);

		GameObject attackerGo = attackerId != 0 ? Managers.Object.FindById(attackerId) : null;
		CreatureController attacker = attackerGo != null ? attackerGo.GetComponent<CreatureController>() : null;
		if (attacker != null)
			attacker.HitStop(stop);

		MyPlayerController me = Managers.Object.MyPlayer;
		if (critical && me != null && (target.Id == me.Id || attackerId == me.Id))
			NetCameraFollow.Shake(NetCameraFollow.CriticalShakeTime, NetCameraFollow.CriticalShakePixels);
	}

	// 내 신력(본인에게만 온다)
	public static void S_ChangeSpiritHandler(PacketSession session, IMessage packet)
	{
		S_ChangeSpirit spiritPacket = packet as S_ChangeSpirit;
		MyCombat.Spirit = spiritPacket.Spirit;
		MyCombat.MaxSpirit = spiritPacket.MaxSpirit;
	}

	public static void S_ChangeStatusHandler(PacketSession session, IMessage packet)
	{
		S_ChangeStatus statusPacket = packet as S_ChangeStatus;

		GameObject go = Managers.Object.FindById(statusPacket.ObjectId);
		CreatureController cc = go != null ? go.GetComponent<CreatureController>() : null;
		if (cc != null)
			cc.SetStatus(statusPacket.StatusId, statusPacket.Duration, statusPacket.Stack);
	}

	// 상태이상으로 바뀐 이동 속도 등
	public static void S_ChangeStatHandler(PacketSession session, IMessage packet)
	{
		S_ChangeStat statPacket = packet as S_ChangeStat;

		GameObject go = Managers.Object.FindById(statPacket.ObjectId);
		BaseController bc = go != null ? go.GetComponent<BaseController>() : null;
		// 이동 속도(신속·둔화)와 테이블 다시 읽기(F12) 뒤의 체력·최대 체력 등
		if (bc != null && statPacket.StatInfo != null)
			bc.Stat = statPacket.StatInfo;
	}

	public static void S_DieHandler(PacketSession session, IMessage packet)
	{
		S_Die diePacket = packet as S_Die;

		GameObject go = Managers.Object.FindById(diePacket.ObjectId);
		if (go == null)
			return;

		// 저승 던전의 플레이어는 혼절(그 자리에 쓰러져 있다가 일어난다)
		if (diePacket.FaintTime > 0f && go.GetComponent<PlayerController>() is PlayerController pc)
		{
			if (Managers.Object.MyPlayer != null && pc.Id == Managers.Object.MyPlayer.Id)
			{
				TargetSelect.End();
				MyCombat.OnMyCancel();
			}
			pc.OnFainted(diePacket.FaintTime);
			return;
		}

		CreatureController cc = go.GetComponent<CreatureController>();
		if (cc != null)
		{
			cc.Hp = 0;
			cc.OnDead();
		}
	}

	public static void S_ReviveHandler(PacketSession session, IMessage packet)
	{
		S_Revive revivePacket = packet as S_Revive;

		GameObject go = Managers.Object.FindById(revivePacket.ObjectId);
		PlayerController pc = go != null ? go.GetComponent<PlayerController>() : null;
		if (pc != null)
			pc.OnRevived(revivePacket.Hp, revivePacket.PosInfo);
	}

	// F 홀드 시작·끝(혼 부르기·보스방 불씨)
	public static void S_ChannelHandler(PacketSession session, IMessage packet)
	{
		S_Channel channelPacket = packet as S_Channel;

		GameObject go = Managers.Object.FindById(channelPacket.ObjectId);
		PlayerController pc = go != null ? go.GetComponent<PlayerController>() : null;
		if (pc != null)
			pc.SetChannel(channelPacket.Kind, channelPacket.Duration);
	}

	public static void S_DungeonStateHandler(PacketSession session, IMessage packet)
	{
		S_DungeonState statePacket = packet as S_DungeonState;
		MyCombat.Lanterns = statePacket.Lanterns;
		MyCombat.MaxLanterns = statePacket.MaxLanterns;
		MyCombat.ArenaBonfireUses = statePacket.ArenaBonfireUses;
		MyCombat.MaxArenaBonfireUses = statePacket.MaxArenaBonfireUses;
	}
}


