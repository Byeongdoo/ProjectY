using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// MasterTable.xlsx → JSON/C# 변환기(Tools/TableConverter)를 에디터 메뉴에서 실행하는 얇은 래퍼.
// 변환 로직은 전부 변환기에 있고, 여기서는 dotnet을 호출해 결과를 Console에 보여주기만 한다.
public static class TableExporter
{
	const string MenuRoot = "Tools/ProjectY/Table/";

	[MenuItem(MenuRoot + "Export JSON (xlsx -> JSON/C#)")]
	static void Export()
	{
		if (Run("", out string output))
		{
			Debug.Log($"[TableExporter] 변환 완료\n{output}");
			// 새 JSON·생성된 .cs가 바로 임포트·컴파일되도록 한다.
			AssetDatabase.Refresh();
		}
	}

	[MenuItem(MenuRoot + "Check (xlsx와 출력이 일치하는지 검사)")]
	static void Check()
	{
		if (Run("--check", out string output))
			Debug.Log($"[TableExporter] 검사 통과\n{output}");
	}

	static bool Run(string arguments, out string output)
	{
		// Client/ 의 상위 폴더가 저장소 루트다.
		string repoRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
		string project = Path.Combine(repoRoot, "Tools", "TableConverter");

		var psi = new ProcessStartInfo
		{
			FileName = "dotnet",
			Arguments = $"run --project \"{project}\" -- {arguments}",
			WorkingDirectory = repoRoot,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};

		try
		{
			using (Process process = Process.Start(psi))
			{
				string stdout = process.StandardOutput.ReadToEnd();
				string stderr = process.StandardError.ReadToEnd();
				process.WaitForExit();

				output = stdout;
				if (process.ExitCode != 0)
				{
					Debug.LogError($"[TableExporter] 실패 (종료 코드 {process.ExitCode})\n{stderr}\n{stdout}");
					return false;
				}
				return true;
			}
		}
		catch (System.Exception e)
		{
			// 대표적인 원인: dotnet SDK가 설치되어 있지 않거나 PATH에 없음
			output = "";
			Debug.LogError($"[TableExporter] dotnet 실행 실패: {e.Message}\n.NET SDK가 설치되어 있고 PATH에 있는지 확인하세요.");
			return false;
		}
	}
}
