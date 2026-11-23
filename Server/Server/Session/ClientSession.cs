using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ServerCore;
using System.Net;
using Google.Protobuf.Protocol;
using Google.Protobuf;
using Server.Game;
using Server.Data;

namespace Server
{
	public class ClientSession : PacketSession
	{
		public Player MyPlayer { get; set; }
		public int SessionId { get; set; }

		// 메시지 이름 -> MsgId 변환 결과 캐시 (매 전송마다 Enum.Parse 하지 않기 위함)
		static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MsgId> _msgIdCache
			= new System.Collections.Concurrent.ConcurrentDictionary<string, MsgId>();

		public void Send(IMessage packet)
		{
			MsgId msgId = _msgIdCache.GetOrAdd(packet.Descriptor.Name,
				name => (MsgId)Enum.Parse(typeof(MsgId), name.Replace("_", string.Empty)));
			ushort size = (ushort)packet.CalculateSize();
			byte[] sendBuffer = new byte[size + 4];
			Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, sendBuffer, 0, sizeof(ushort));
			Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, 2, sizeof(ushort));
			Array.Copy(packet.ToByteArray(), 0, sendBuffer, 4, size);
			Send(new ArraySegment<byte>(sendBuffer));
		}

		public override void OnConnected(EndPoint endPoint)
		{
			Console.WriteLine($"OnConnected : {endPoint}");

			MyPlayer = ObjectManager.Instance.Add<Player>();
			{
				MyPlayer.Info.Name = $"Player_{MyPlayer.Info.ObjectId}";
				MyPlayer.Info.PosInfo.State = CreatureState.Idle;
				MyPlayer.Info.PosInfo.MoveDir = MoveDir.Down;
				MyPlayer.Info.PosInfo.PosX = 0;
				MyPlayer.Info.PosInfo.PosY = 0;

				// 레벨은 로컬 세이브가 생기면 거기서 받는다. 나머지 스탯은 룸에 들어갈 때 Player.ApplyStats가 테이블에서 정한다
				MyPlayer.Stat.Level = 1;

				MyPlayer.Session = this;
			}

			// 접속하면 삼천도 채널에 배정만 해 둔다. 클라가 삼천도 씬을 다 불러와 C_EnterGame을 보내면 들어간다.
			MyPlayer.PendingRoom = RoomManager.Instance.Lobby;
		}

		public override void OnRecvPacket(ArraySegment<byte> buffer)
		{
			PacketManager.Instance.OnRecvPacket(this, buffer);
		}

		public override void OnDisconnected(EndPoint endPoint)
		{
			Player player = MyPlayer;
			if (player != null)
			{
				// 룸을 옮기는 중이면 들어가려던 룸에서 뺀다(그 룸 잡 큐에 EnterGame이 먼저 들어가 있어도 순서대로 처리된다)
				GameRoom room = player.Room ?? player.PendingRoom;
				room?.Push(room.LeaveGame, player.Info.ObjectId);

				// 접속이 끊긴 플레이어가 ObjectManager 에 남아 있지 않도록 제거한다
				ObjectManager.Instance.Remove(player.Info.ObjectId);
				MyPlayer = null;
			}

			SessionManager.Instance.Remove(this);

			Console.WriteLine($"OnDisconnected : {endPoint}");
		}

		public override void OnSend(int numOfBytes)
		{
			//Console.WriteLine($"Transferred bytes: {numOfBytes}");
		}
	}
}
