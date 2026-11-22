using System.Text.Json;

namespace TableConverter;

/// <summary>시트별 추가 규칙 (tables.json).</summary>
internal sealed class TableRule
{
    /// <summary>키 컬럼(복합 가능). 생략하면 첫 데이터 컬럼.</summary>
    public List<string>? Key { get; set; }

    /// <summary>true면 변환하지 않는다 (레거시 시트 등).</summary>
    public bool Skip { get; set; }

    /// <summary>컬럼 → 대상 시트. 값(리스트면 각 요소)이 대상 시트의 키여야 한다. 0은 '없음'으로 허용.</summary>
    public Dictionary<string, string>? Refs { get; set; }

    /// <summary>값이 strings 시트의 stringID여야 하는 컬럼.</summary>
    public List<string>? StringKeys { get; set; }

    /// <summary>stringKeys와 같지만 누락을 오류가 아니라 경고로만 알린다 (문안이 아직 작성 중인 컬럼).</summary>
    public List<string>? SoftStringKeys { get; set; }
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

    /// <summary>refs·stringKeys 검증. 오류 위치는 문제가 된 셀 주소.</summary>
    public static void CheckReferences(List<CodeGenerator.Entry> entries, HashSet<string>? stringIds,
        Dictionary<string, TableRule> rules, List<string> errors)
    {
        // 대상 시트별 키 집합 (단일 키만 참조 대상이 될 수 있다)
        var keySets = new Dictionary<string, HashSet<string>>();
        foreach (var e in entries)
        {
            if (e.KeyCols.Count != 1) continue;
            int ki = e.Table.Columns.IndexOf(e.KeyCols[0]);
            keySets[e.Table.Name] = new HashSet<string>(e.Table.Rows.Select(r => r[ki].Text));
        }

        foreach (var e in entries)
        {
            if (!rules.TryGetValue(e.Table.Name, out TableRule? rule)) continue;
            SheetTable t = e.Table;

            if (rule.Refs != null)
            {
                foreach (var (colName, target) in rule.Refs)
                {
                    int ci = t.Columns.FindIndex(c => c.Key == colName);
                    if (ci < 0) { errors.Add($"{t.Name} — tables.json refs의 컬럼 '{colName}'이 시트에 없음"); continue; }
                    if (!keySets.TryGetValue(target, out HashSet<string>? set))
                    {
                        errors.Add($"{t.Name} — refs '{colName}' → '{target}': 대상 시트가 없거나 단일 키가 아님");
                        continue;
                    }
                    foreach (Raw[] row in t.Rows)
                        foreach (string v in SheetReader.Elements(t.Columns[ci], row[ci]))
                            if (v != "0" && !set.Contains(v))
                                errors.Add($"{t.Name}!{row[ci].Address} — {colName} {v}가 {target}에 없음");
                }
            }

            CheckStringKeys(t, rule.StringKeys, soft: false, stringIds, errors);
            CheckStringKeys(t, rule.SoftStringKeys, soft: true, stringIds, errors);
        }
    }

    static void CheckStringKeys(SheetTable t, List<string>? cols, bool soft, HashSet<string>? stringIds, List<string> errors)
    {
        if (cols == null) return;
        if (stringIds == null)
        {
            errors.Add($"{t.Name} — stringKeys를 검증하려면 strings 시트가 필요함");
            return;
        }
        foreach (string colName in cols)
        {
            int ci = t.Columns.FindIndex(c => c.Key == colName);
            if (ci < 0) { errors.Add($"{t.Name} — tables.json의 stringKeys 컬럼 '{colName}'이 시트에 없음"); continue; }

            var missing = new List<(string Addr, string Id)>();
            foreach (Raw[] row in t.Rows)
                foreach (string v in SheetReader.Elements(t.Columns[ci], row[ci]))
                    if (!stringIds.Contains(v))
                        missing.Add((row[ci].Address, v));

            if (soft)
            {
                if (missing.Count > 0)
                    Console.WriteLine($"경고: {t.Name}.{colName} — stringID {missing.Count}개가 strings에 없음 (예: {t.Name}!{missing[0].Addr} '{missing[0].Id}')");
            }
            else
            {
                foreach (var (addr, id) in missing)
                    errors.Add($"{t.Name}!{addr} — stringID '{id}'가 strings에 없음");
            }
        }
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
