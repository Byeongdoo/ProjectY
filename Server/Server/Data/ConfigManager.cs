using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Server.Data
{
	[Serializable]
	public class ServerConfig
	{
		public string dataPath;

		// 개발·테스트 명령(C_DebugCommand: 허수아비 소환·정리, 체력·신력·쿨타임 초기화)을 받을지. 배포 서버에서는 false
		public bool debugCommands;
	}

	public class ConfigManager
	{
		public static ServerConfig Config { get; private set; }

		public static void LoadConfig()
		{
			// 실행 위치(작업 디렉터리)와 상관없이 exe 옆에 복사된 config.json 을 읽는다
			string baseDir = AppContext.BaseDirectory;
			string text = File.ReadAllText(Path.Combine(baseDir, "config.json"));
			Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(text);

			// 상대 경로인 dataPath 는 exe 위치 기준으로 해석한다
			if (Path.IsPathRooted(Config.dataPath) == false)
				Config.dataPath = Path.GetFullPath(Path.Combine(baseDir, Config.dataPath));
		}
	}
}
