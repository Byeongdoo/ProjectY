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

			// 마스터 테이블 로딩. 실패하면 어느 파일이 문제인지 출력하고 즉시 종료한다.
			try
			{
				Table.TableManager.LoadAll(new FileTableReader(ConfigManager.Config.dataPath));
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.Message);
				Environment.Exit(1);
			}

			GameRoom room = RoomManager.Instance.Add(1);
			TickRoom(room, 50);

			// 호스트 이름이 여러 IPv4(루프백·LAN)로 풀리는 환경에서 서버와 클라가 서로 다른 주소를 고르면
			// 접속이 거부된다. 모든 IPv4 인터페이스에서 받으면 클라가 어느 주소를 골라도 붙는다.
			IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 7777);

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
