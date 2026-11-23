using UnityEngine;

public class DataManager
{
	public void Init()
	{
		// 마스터 테이블 (TableConverter 생성 코드). 실패하면 어느 파일 문제인지 예외 메시지에 담긴다.
		Table.TableManager.LoadAll(new ResourcesTableReader());
	}
}
