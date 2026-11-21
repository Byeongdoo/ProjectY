using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Protocol;
using Google.Protobuf.WellKnownTypes;
using Server.Data;
using Server.Game;
using ServerCore;

namespace Server
{
	class Program
	{
		static Listener _listener = new Listener();

		// 룸 틱은 전용 스레드 하나에서 순차 실행한다.
		// (System.Timers.Timer는 이전 콜백이 끝나기 전에 다음 콜백이 겹쳐 실행될 수 있어서 Update/Flush가 동시에 돌 위험이 있다)
		static void TickRoom(GameRoom room, int tick = 100)
		{
			Thread thread = new Thread(() =>
			{
				while (true)
				{
					long start = Environment.TickCount64;

					try
					{
						room.Update();
					}
					catch (Exception e)
					{
						Console.WriteLine($"Room Update Failed {e}");
					}

					int elapsed = (int)(Environment.TickCount64 - start);
					if (elapsed < tick)
						Thread.Sleep(tick - elapsed);
				}
			});
			thread.IsBackground = true;
			thread.Name = $"Room_{room.RoomId}";
			thread.Start();
		}

		static void Main(string[] args)
		{
			ConfigManager.LoadConfig();
			DataManager.LoadData();

			GameRoom room = RoomManager.Instance.Add(1);
			TickRoom(room, 50);

			// DNS (Domain Name System)
			string host = Dns.GetHostName();
			IPHostEntry ipHost = Dns.GetHostEntry(host);
			// IPv6/가상 어댑터가 먼저 잡히는 경우를 피하기 위해 IPv4 주소를 우선 사용한다
			IPAddress ipAddr = Array.Find(ipHost.AddressList, a => a.AddressFamily == AddressFamily.InterNetwork) ?? ipHost.AddressList[0];
			IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

			_listener.Init(endPoint, () => { return SessionManager.Instance.Generate(); });
			Console.WriteLine("Listening...");

			//FlushRoom();
			//JobTimer.Instance.Push(FlushRoom);

			// TODO
			while (true)
			{
				//JobTimer.Instance.Flush();
				Thread.Sleep(100);
			}
		}
	}
}
