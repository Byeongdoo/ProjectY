using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server;
using Server.Game;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Text;

class PacketHandler
{
	public static void C_MoveHandler(PacketSession session, IMessage packet)
	{
		C_Move movePacket = packet as C_Move;
		ClientSession clientSession = session as ClientSession;

		//Console.WriteLine($"C_Move ({movePacket.PosInfo.PosX}, {movePacket.PosInfo.PosY})");

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleMove, player, movePacket);
	}

	// 씬 로딩을 마친 클라를 배정된 룸(PendingRoom)에 넣는다
	public static void C_EnterGameHandler(PacketSession session, IMessage packet)
	{
		C_EnterGame enterPacket = packet as C_EnterGame;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null || player.Room != null)
			return;

		GameRoom room = player.PendingRoom;
		if (room == null)
			return;

		// 이름은 로컬 세이브의 캐릭터 이름이 생기면 그 값을 받는다. 지금은 비어 있으면 서버 기본 이름을 쓴다.
		string name = enterPacket.Name?.Trim();
		if (string.IsNullOrEmpty(name) == false)
			player.Info.Name = name.Length > NameMaxLength ? name.Substring(0, NameMaxLength) : name;

		room.Push(room.EnterGame, player);
	}

	const int NameMaxLength = 16;

	public static void C_EnterDungeonHandler(PacketSession session, IMessage packet)
	{
		C_EnterDungeon enterPacket = packet as C_EnterDungeon;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleEnterDungeon, player, enterPacket);
	}

	public static void C_SkillHandler(PacketSession session, IMessage packet)
	{
		C_Skill skillPacket = packet as C_Skill;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleSkill, player, skillPacket);
	}

	public static void C_CancelSkillHandler(PacketSession session, IMessage packet)
	{
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleCancelSkill, player);
	}

	public static void C_HoldInteractHandler(PacketSession session, IMessage packet)
	{
		C_HoldInteract holdPacket = packet as C_HoldInteract;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleHoldInteract, player, holdPacket);
	}

	public static void C_DebugCommandHandler(PacketSession session, IMessage packet)
	{
		C_DebugCommand debugPacket = packet as C_DebugCommand;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleDebugCommand, player, debugPacket);
	}
}
