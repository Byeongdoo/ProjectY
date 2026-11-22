using System.IO;
using Newtonsoft.Json;
using Table;

namespace Server.Data
{
	// 마스터 테이블 JSON(dataPath 폴더)을 읽는 서버용 ITableReader.
	// 생성 코드(Generated/Tables.g.cs)의 TableManager.LoadAll()에 넘겨 쓴다.
	public class FileTableReader : ITableReader
	{
		readonly string _dir;

		public FileTableReader(string dir)
		{
			_dir = dir;
		}

		public TFile Read<TFile>(string fileName) where TFile : class
		{
			string text = File.ReadAllText(Path.Combine(_dir, fileName + ".json"));
			return JsonConvert.DeserializeObject<TFile>(text);
		}
	}
}
