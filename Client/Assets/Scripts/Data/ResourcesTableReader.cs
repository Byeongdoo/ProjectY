using System.IO;
using Table;
using UnityEngine;

// Resources/Data/<이름>.json 을 읽는 클라용 ITableReader.
// 생성 코드(Generated/Tables.g.cs)의 TableManager.LoadAll()에 넘겨 쓴다.
public class ResourcesTableReader : ITableReader
{
	public TFile Read<TFile>(string fileName) where TFile : class
	{
		TextAsset asset = Resources.Load<TextAsset>($"Data/{fileName}");
		if (asset == null)
			throw new FileNotFoundException($"Resources/Data/{fileName}.json 을 찾을 수 없음");

		return JsonUtility.FromJson<TFile>(asset.text);
	}
}
