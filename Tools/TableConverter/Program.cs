using System.Text;
using ClosedXML.Excel;

namespace TableConverter;

/// <summary>
/// MasterTable.xlsx → JSON + C# 변환기.
/// 규약은 docs/data-tables.md, 설계는 docs/table-converter.md 참고.
/// </summary>
internal static class Program
{
    const string StringsSheet = "strings";

    static int Main(string[] args)
    {
        // 출력이 파이프로 넘어가는 경우(Unity 에디터 메뉴, CI)에는 한글이 깨지지 않게 UTF-8로 쓴다.
        if (Console.IsOutputRedirected || Console.IsErrorRedirected) Console.OutputEncoding = new UTF8Encoding(false);

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
        var onlySheets = new List<string>();
        bool customOut = false;
        bool check = false;   // true면 파일을 쓰지 않고, 현재 출력과 다르면 종료 코드 1
        int stale = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--xlsx" when i + 1 < args.Length: xlsxPath = Path.GetFullPath(args[++i]); break;
                case "--out" when i + 1 < args.Length: outDir = Path.GetFullPath(args[++i]); customOut = true; break;
                case "--sheet" when i + 1 < args.Length: onlySheets.Add(args[++i]); break;
                case "--check": check = true; break;
                default:
                    Console.Error.WriteLine($"알 수 없는 인자: {args[i]}");
                    Console.Error.WriteLine("사용법: TableConverter [--check] [--xlsx <경로>] [--out <폴더>] [--sheet <이름>]...");
                    return 2;
            }
        }

        if (!File.Exists(xlsxPath))
        {
            Console.Error.WriteLine($"xlsx를 찾을 수 없습니다: {xlsxPath}");
            return 2;
        }

        var errors = new List<string>();
        Dictionary<string, TableRule> rules = TablesConfig.Load(rulesPath, errors);
        var entries = new List<CodeGenerator.Entry>();
        HashSet<string>? stringIds = null;

        // Excel/LibreOffice가 열어 둔 상태에서도 읽도록 공유 모드로 연다.
        using (var fs = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var wb = new XLWorkbook(fs))
        {
            // 변환 대상: '#' 시트·skip 규칙 시트를 뺀 모든 시트 (strings 포함).
            // 검증(참조)을 위해 --sheet가 있어도 전부 읽고, 출력만 골라서 한다.
            foreach (IXLWorksheet ws in wb.Worksheets)
            {
                string name = ws.Name;
                if (name.StartsWith('#')) continue;
                if (rules.TryGetValue(name, out TableRule? rule) && rule.Skip) continue;

                SheetTable? table = SheetReader.Read(ws, errors);
                if (table == null) continue;

                List<Column>? keyCols = TablesConfig.ResolveKey(table, rules, errors);
                if (keyCols == null) continue;
                TablesConfig.CheckDuplicateKeys(table, keyCols, errors);
                entries.Add(new CodeGenerator.Entry(table, keyCols));
            }

        }

        // stringKeys 검증용 stringID 집합 (strings 시트의 키)
        CodeGenerator.Entry? strings = entries.Find(e => e.Table.Name == StringsSheet);
        if (strings != null)
        {
            int ki = strings.Table.Columns.IndexOf(strings.KeyCols[0]);
            stringIds = new HashSet<string>(strings.Table.Rows.Select(r => r[ki].Text));
        }

        foreach (string sheet in onlySheets)
            if (!entries.Any(e => e.Table.Name == sheet))
                errors.Add($"{sheet} — 변환 대상 시트가 아님 (없거나 skip/#/strings)");

        TablesConfig.CheckReferences(entries, stringIds, rules, errors);

        // 오류가 하나라도 있으면 아무 파일도 쓰지 않는다.
        if (errors.Count > 0)
        {
            foreach (string e in errors) Console.Error.WriteLine("오류: " + e);
            Console.Error.WriteLine($"변환 실패 — 오류 {errors.Count}건. 출력 파일은 쓰지 않았습니다.");
            return 1;
        }

        // 파일 하나를 쓰거나(기본) 비교만 한다(--check). 내용이 같으면 쓰지 않는다
        // (mtime 유지 → Unity가 불필요하게 다시 임포트하지 않음).
        string Emit(string path, byte[] bytes)
        {
            bool same = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
            if (same) return "변경 없음";
            if (check)
            {
                stale++;
                return File.Exists(path) ? "오래됨" : "없음";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return "기록";
        }

        foreach (CodeGenerator.Entry e in entries)
        {
            if (onlySheets.Count > 0 && !onlySheets.Contains(e.Table.Name)) continue;
            string file = CodeGenerator.Pascal(e.Table.Name) + ".json";
            byte[] bytes = JsonExporter.Export(e.Table);
            Console.WriteLine($"{Emit(Path.Combine(outDir, file), bytes)}: {file} ({bytes.Length:N0} bytes)");
        }

        // --sheet로 일부만 변환할 때 생성 C#을 만들면 다른 시트의 클래스가 사라지므로 건너뛴다.
        if (onlySheets.Count > 0 || customOut)
        {
            Console.WriteLine("참고: --sheet 또는 --out 지정 시 C# 생성은 건너뜁니다 (기본 출력의 전체 변환 때만 생성).");
        }
        else
        {
            byte[] cs = new UTF8Encoding(false).GetBytes(CodeGenerator.Generate(entries));
            foreach (string path in csPaths)
                Console.WriteLine($"{Emit(path, cs)}: {Path.GetRelativePath(repoRoot, path)} ({cs.Length:N0} bytes)");
        }

        if (check && stale > 0)
        {
            Console.Error.WriteLine($"검사 실패 — 최신이 아닌 파일 {stale}개. `dotnet run --project Tools/TableConverter`로 다시 변환하세요.");
            return 1;
        }
        if (check) Console.WriteLine("검사 통과 — 모든 출력이 xlsx와 일치합니다.");
        return 0;
    }

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
