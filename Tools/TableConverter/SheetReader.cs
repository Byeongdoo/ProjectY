using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace TableConverter;

internal enum ColType { Int, Float, String }

internal sealed class Column
{
    public int Index;          // 시트의 열 번호(1부터)
    public string Key = "";    // JSON 필드명 (<List> 제거)
    public bool IsList;
    public bool FloatHint;     // 1행 라벨에 |float
    public string Label = "";  // 1행 한국어 라벨 (|float 제거) — 생성 코드의 주석에 쓴다
    public bool Wide;          // 정수 값이 int 범위를 넘으면 true → long
    public ColType Type;
}

/// <summary>한 셀의 원시 값. 숫자 셀은 문자열로 보관해 비교·파싱 규칙을 통일한다.</summary>
internal readonly record struct Raw(string Text, bool IsEmpty, string Address);

internal sealed class SheetTable
{
    public string Name = "";
    public List<Column> Columns = new();
    public List<Raw[]> Rows = new();   // Rows[r][c] — Columns 순서
}

internal static class SheetReader
{
    static readonly Regex FloatHintRe = new(@"\|\s*float\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    const string ListSuffix = "<List>";

    public static SheetTable? Read(IXLWorksheet ws, List<string> errors)
    {
        int errorsBefore = errors.Count;
        var table = new SheetTable { Name = ws.Name };

        var lastRowObj = ws.LastRowUsed();
        var lastColObj = ws.LastColumnUsed();
        if (lastRowObj == null || lastColObj == null)
        {
            errors.Add($"{ws.Name} — 비어 있는 시트");
            return null;
        }
        int lastRow = lastRowObj.RowNumber();
        int lastCol = lastColObj.ColumnNumber();

        // --- 헤더: 2행 = 영문 키, 1행 = 라벨(|float 힌트). A열(기능)은 건너뛴다.
        var seenKeys = new HashSet<string>();
        for (int c = 2; c <= lastCol; c++)
        {
            string key = ws.Cell(2, c).GetString().Trim();
            if (key.Length == 0 || key.StartsWith('#')) continue;   // 메모 컬럼

            var col = new Column { Index = c };
            if (key.EndsWith(ListSuffix, StringComparison.Ordinal))
            {
                col.IsList = true;
                key = key[..^ListSuffix.Length].TrimEnd();
            }
            col.Key = key;
            string label = ws.Cell(1, c).GetString().Trim();
            col.FloatHint = FloatHintRe.IsMatch(label);
            col.Label = FloatHintRe.Replace(label, "").Trim();

            if (!seenKeys.Add(key))
            {
                errors.Add($"{ws.Name}!{ws.Cell(2, c).Address.ToStringRelative()} — 키 '{key}' 중복");
                continue;
            }
            table.Columns.Add(col);
        }
        if (table.Columns.Count == 0)
        {
            errors.Add($"{ws.Name} — 2행에서 유효한 키 컬럼을 찾지 못함");
            return null;
        }

        // --- 데이터: 3행~. A열이 //이면 주석 행, 전부 비면 빈 행.
        for (int r = 3; r <= lastRow; r++)
        {
            string marker = ws.Cell(r, 1).GetString().Trim();
            if (marker.StartsWith("//", StringComparison.Ordinal)) continue;

            var row = new Raw[table.Columns.Count];
            bool any = false;
            for (int i = 0; i < table.Columns.Count; i++)
            {
                IXLCell cell = ws.Cell(r, table.Columns[i].Index);
                row[i] = ReadCell(cell);
                if (!row[i].IsEmpty) any = true;
            }
            if (any) table.Rows.Add(row);
        }

        // --- 타입 판정
        for (int i = 0; i < table.Columns.Count; i++)
            InferType(table, i, errors);

        return errors.Count == errorsBefore ? table : null;
    }

    static Raw ReadCell(IXLCell cell)
    {
        string addr = cell.Address.ToStringRelative();
        if (cell.IsEmpty()) return new Raw("", true, addr);

        string text;
        if (cell.DataType == XLDataType.Number)
            text = cell.GetDouble().ToString("R", CultureInfo.InvariantCulture);
        else if (cell.DataType == XLDataType.Boolean)
            text = cell.GetBoolean() ? "1" : "0";
        else
            text = cell.GetString().Trim();

        return new Raw(text, text.Length == 0, addr);
    }

    /// <summary>셀(리스트면 요소들)의 문자열 조각.</summary>
    public static IEnumerable<string> Elements(Column col, Raw raw)
    {
        if (raw.IsEmpty) yield break;
        if (!col.IsList) { yield return raw.Text; yield break; }
        foreach (string part in raw.Text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            yield return part;
    }

    static void InferType(SheetTable table, int ci, List<string> errors)
    {
        Column col = table.Columns[ci];
        bool allInt = true, anyValue = false, wide = false;
        string? firstFraction = null;

        foreach (Raw[] row in table.Rows)
        {
            foreach (string e in Elements(col, row[ci]))
            {
                anyValue = true;
                bool isInt = long.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv);
                if (isInt && (lv > int.MaxValue || lv < int.MinValue)) wide = true;
                bool isNum = double.TryParse(e, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

                if (col.FloatHint)
                {
                    if (!isNum)
                        errors.Add($"{table.Name}!{row[ci].Address} — |float 컬럼 '{col.Key}'에 숫자가 아닌 값: \"{e}\"");
                }
                else if (!isInt)
                {
                    allInt = false;
                    if (isNum && firstFraction == null)
                        firstFraction = $"{table.Name}!{row[ci].Address} — 컬럼 '{col.Key}'에 소수 값 \"{e}\"가 있음 (1행 라벨에 |float 필요?)";
                }
            }
        }

        if (col.FloatHint) { col.Type = ColType.Float; return; }

        if (!anyValue)
        {
            // 데이터가 전혀 없는 컬럼은 판정 근거가 없다. 문자열로 두고 경고만 한다.
            col.Type = ColType.String;
            Console.WriteLine($"경고: {table.Name}.{col.Key} — 데이터가 없어 string으로 처리");
            return;
        }

        if (allInt) { col.Type = ColType.Int; col.Wide = wide; return; }

        // 소수가 섞여 있는데 숫자만 있는 컬럼이면 힌트 누락 가능성이 높으므로 중단한다.
        if (firstFraction != null && IsAllNumeric(table, ci))
        {
            errors.Add(firstFraction);
            return;
        }
        col.Type = ColType.String;
    }

    static bool IsAllNumeric(SheetTable table, int ci)
    {
        Column col = table.Columns[ci];
        foreach (Raw[] row in table.Rows)
            foreach (string e in Elements(col, row[ci]))
                if (!double.TryParse(e, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
        return true;
    }
}
