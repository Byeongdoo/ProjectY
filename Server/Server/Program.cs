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

		static void Main(string[] args)
		{
			ConfigManager.LoadConfig();

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

			// 접속하면 삼천도 채널에 배정된다. 맵(Common/MapData/Samcheondo.txt)이 없으면 시작하지 않는다.
			try
			{
				RoomManager.Instance.CreateLobby();
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"삼천도 룸 생성 실패: {e.Message}");
				Environment.Exit(1);
			}

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
