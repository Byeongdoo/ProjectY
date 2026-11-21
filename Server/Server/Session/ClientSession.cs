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

				StatInfo stat = null;
				DataManager.StatDict.TryGetValue(1, out stat);
				MyPlayer.Stat.MergeFrom(stat);

				MyPlayer.Session = this;
			}

			GameRoom room = RoomManager.Instance.Find(1);
			room.Push(room.EnterGame, MyPlayer);
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
				GameRoom room = player.Room ?? RoomManager.Instance.Find(1);
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
