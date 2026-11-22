using System.Text;
using ClosedXML.Excel;

namespace TableConverter;

/// <summary>
/// MasterTable.xlsx → JSON 변환기 (1단계: 시트 읽기 + 타입 판정 + JSON 출력).
/// 규약은 docs/data-tables.md, 설계는 docs/table-converter.md 참고.
/// </summary>
internal static class Program
{
    // 1단계 대상 시트. --sheet 로 덮어쓸 수 있다.
    static readonly string[] DefaultSheets = { "spirits", "spiritSkills", "characterLevel" };

    static int Main(string[] args)
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("저장소 루트(CLAUDE.md 위치)를 찾지 못했습니다. 저장소 안에서 실행하세요.");
            return 2;
        }

        string xlsxPath = Path.Combine(repoRoot, "Table", "MasterTable.xlsx");
        string outDir = Path.Combine(repoRoot, "Client", "Assets", "Resources", "Data");
        string rulesPath = Path.Combine(repoRoot, "Tools", "TableConverter", "tables.json");
        // 생성 C#은 클라·서버에 같은 내용으로 둔다 (Unity는 Assets 밖 파일을 참조할 수 없어 복사본을 둔다).
        string[] csPaths =
        {
            Path.Combine(repoRoot, "Server", "Server", "Data", "Generated", "Tables.g.cs"),
            Path.Combine(repoRoot, "Client", "Assets", "Scripts", "Data", "Generated", "Tables.g.cs"),
        };
        var sheets = new List<string>();
        bool sheetsGiven = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--xlsx" when i + 1 < args.Length: xlsxPath = Path.GetFullPath(args[++i]); break;
                case "--out" when i + 1 < args.Length: outDir = Path.GetFullPath(args[++i]); break;
                case "--sheet" when i + 1 < args.Length: sheets.Add(args[++i]); sheetsGiven = true; break;
                default:
                    Console.Error.WriteLine($"알 수 없는 인자: {args[i]}");
                    Console.Error.WriteLine("사용법: TableConverter [--xlsx <경로>] [--out <폴더>] [--sheet <이름>]...");
                    return 2;
            }
        }
        if (sheets.Count == 0) sheets.AddRange(DefaultSheets);

        if (!File.Exists(xlsxPath))
        {
            Console.Error.WriteLine($"xlsx를 찾을 수 없습니다: {xlsxPath}");
            return 2;
        }

        var errors = new List<string>();
        var results = new List<(string File, byte[] Bytes)>();
        var entries = new List<CodeGenerator.Entry>();
        Dictionary<string, TableRule> rules = TablesConfig.Load(rulesPath, errors);

        // Excel/LibreOffice가 열어 둔 상태에서도 읽도록 공유 모드로 연다.
        using (var fs = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var wb = new XLWorkbook(fs))
        {
            foreach (string name in sheets)
            {
                if (!wb.TryGetWorksheet(name, out IXLWorksheet? ws))
                {
                    errors.Add($"{name} — 시트를 찾을 수 없음");
                    continue;
                }

                SheetTable? table = SheetReader.Read(ws, errors);
                if (table == null) continue;

                List<Column>? keyCols = TablesConfig.ResolveKey(table, rules, errors);
                if (keyCols == null) continue;
                TablesConfig.CheckDuplicateKeys(table, keyCols, errors);

                entries.Add(new CodeGenerator.Entry(table, keyCols));
                results.Add((FileNameOf(name), JsonExporter.Export(table)));
            }
        }

        // 오류가 하나라도 있으면 아무 파일도 쓰지 않는다.
        if (errors.Count > 0)
        {
            foreach (string e in errors) Console.Error.WriteLine("오류: " + e);
            Console.Error.WriteLine($"변환 실패 — 오류 {errors.Count}건. 출력 파일은 쓰지 않았습니다.");
            return 1;
        }

        Directory.CreateDirectory(outDir);
        foreach (var (file, bytes) in results)
        {
            string path = Path.Combine(outDir, file);
            // 내용이 같으면 쓰지 않는다 (mtime 유지 → Unity 불필요한 재임포트 방지).
            bool same = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
            if (!same) File.WriteAllBytes(path, bytes);
            Console.WriteLine($"{(same ? "변경 없음" : "기록")}: {file} ({bytes.Length:N0} bytes)");
        }

        // --sheet로 일부만 변환할 때 생성 C#을 만들면 다른 시트의 클래스가 사라지므로 건너뛴다.
        if (sheetsGiven)
        {
            Console.WriteLine("참고: --sheet 지정 시 C# 생성은 건너뜁니다 (전체 변환 때만 생성).");
            return 0;
        }

        byte[] cs = new UTF8Encoding(false).GetBytes(CodeGenerator.Generate(entries));
        foreach (string path in csPaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bool same = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(cs);
            if (!same) File.WriteAllBytes(path, cs);
            Console.WriteLine($"{(same ? "변경 없음" : "기록")}: {Path.GetRelativePath(repoRoot, path)} ({cs.Length:N0} bytes)");
        }
        return 0;
    }

    // spiritSkills → SpiritSkills.json
    static string FileNameOf(string sheet) => char.ToUpperInvariant(sheet[0]) + sheet[1..] + ".json";

    static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
