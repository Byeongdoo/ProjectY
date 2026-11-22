using ServerCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using Google.Protobuf;
using Google.Protobuf.Protocol;

// 게임 서버 접속. 서버는 삼천도·저승 던전에서만 쓴다 — 이승 씬은 접속하지 않는다.
// 멀티 씬(MultiScene)이 열리면 Connect()와 RequestEnterGame()을 부른다.
public class NetworkManager
{
	ServerSession _session = new ServerSession();
	bool _connecting;
	bool _enterRequested;
	string _enterName = "";

	public bool Connected => _session.Connected;

	public void Send(IMessage packet)
	{
		_session.Send(packet);
	}

	// 이미 접속했거나 접속 중이면 아무것도 하지 않는다
	public void Connect()
	{
		if (_connecting || Connected)
			return;
		_connecting = true;

		// DNS (Domain Name System)
		string host = Dns.GetHostName();
		IPHostEntry ipHost = Dns.GetHostEntry(host);
		// IPv6/가상 어댑터가 먼저 잡히는 경우를 피하기 위해 IPv4 주소를 우선 사용한다
		IPAddress ipAddr = Array.Find(ipHost.AddressList, a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? ipHost.AddressList[0];
		IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

		Connector connector = new Connector();

		connector.Connect(endPoint,
			() => { return _session; },
			1);
	}

	// 씬을 다 불러왔으니 서버가 배정해 둔 룸에 들어가겠다고 알린다. 아직 접속 전이면 접속되는 대로 보낸다.
	public void RequestEnterGame(string name)
	{
		_enterRequested = true;
		_enterName = name ?? "";
	}

	public void Update()
	{
		if (_enterRequested && Connected)
		{
			_enterRequested = false;
			Send(new C_EnterGame() { Name = _enterName });
		}

		List<PacketMessage> list = PacketQueue.Instance.PopAll();
		foreach (PacketMessage packet in list)
		{
			Action<PacketSession, IMessage> handler = PacketManager.Instance.GetPacketHandler(packet.Id);
			if (handler != null)
				handler.Invoke(_session, packet.Message);
		}
	}

}
