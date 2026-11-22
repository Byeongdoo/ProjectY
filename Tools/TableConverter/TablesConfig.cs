using System.Text.Json;

namespace TableConverter;

/// <summary>시트별 추가 규칙 (tables.json). 2단계: key. 3단계에서 refs·stringKeys 추가 예정.</summary>
internal sealed class TableRule
{
    public List<string>? Key { get; set; }
}

internal static class TablesConfig
{
    public static Dictionary<string, TableRule> Load(string path, List<string> errors)
    {
        if (!File.Exists(path)) return new Dictionary<string, TableRule>();
        try
        {
            var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            return JsonSerializer.Deserialize<Dictionary<string, TableRule>>(File.ReadAllText(path), opt)
                   ?? new Dictionary<string, TableRule>();
        }
        catch (JsonException e)
        {
            errors.Add($"tables.json — 읽기 실패: {e.Message}");
            return new Dictionary<string, TableRule>();
        }
    }

    /// <summary>시트의 키 컬럼 목록. 규칙이 없으면 첫 컬럼. 잘못되면 오류를 기록하고 null.</summary>
    public static List<Column>? ResolveKey(SheetTable table, Dictionary<string, TableRule> rules, List<string> errors)
    {
        var result = new List<Column>();
        if (rules.TryGetValue(table.Name, out TableRule? rule) && rule.Key is { Count: > 0 })
        {
            foreach (string name in rule.Key)
            {
                Column? c = table.Columns.Find(x => x.Key == name);
                if (c == null) { errors.Add($"{table.Name} — tables.json의 key '{name}' 컬럼이 시트에 없음"); return null; }
                result.Add(c);
            }
        }
        else
        {
            result.Add(table.Columns[0]);
        }

        foreach (Column c in result)
        {
            if (c.IsList || c.Type == ColType.Float)
            {
                errors.Add($"{table.Name} — 키 컬럼 '{c.Key}'은 정수 또는 문자열이어야 함");
                return null;
            }
        }
        return result;
    }

    /// <summary>키 중복 검사. 오류 위치는 중복된 행의 키 셀 주소.</summary>
    public static void CheckDuplicateKeys(SheetTable table, List<Column> keyCols, List<string> errors)
    {
        int[] idx = keyCols.Select(k => table.Columns.IndexOf(k)).ToArray();
        var seen = new Dictionary<string, string>();   // 키 → 처음 나온 셀 주소
        foreach (Raw[] row in table.Rows)
        {
            string key = string.Join("/", idx.Select(i => row[i].Text));
            if (seen.TryGetValue(key, out string? first))
                errors.Add($"{table.Name}!{row[idx[0]].Address} — 키 {key} 중복 ({first}와 동일)");
            else
                seen[key] = row[idx[0]].Address;
        }
    }
}
