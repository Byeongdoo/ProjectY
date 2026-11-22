using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TableConverter;

internal static class JsonExporter
{
    /// <summary>{ "rows": [ {...}, ... ] } 형태. UTF-8(BOM 없음), LF, 들여쓰기 2칸, 끝에 개행 1개.</summary>
    public static byte[] Export(SheetTable table)
    {
        using var ms = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            IndentSize = 2,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글을 \uXXXX로 바꾸지 않는다
        };

        using (var w = new Utf8JsonWriter(ms, options))
        {
            w.WriteStartObject();
            w.WriteStartArray("rows");
            foreach (Raw[] row in table.Rows)
            {
                w.WriteStartObject();
                for (int i = 0; i < table.Columns.Count; i++)
                    WriteField(w, table.Columns[i], row[i]);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        ms.WriteByte((byte)'\n');
        return ms.ToArray();
    }

    static void WriteField(Utf8JsonWriter w, Column col, Raw raw)
    {
        w.WritePropertyName(col.Key);
        if (col.IsList)
        {
            w.WriteStartArray();
            foreach (string e in SheetReader.Elements(col, raw))
                WriteValue(w, col.Type, e);
            w.WriteEndArray();
            return;
        }
        // 빈 셀은 타입별 기본값(0 / 0 / "").
        WriteValue(w, col.Type, raw.IsEmpty ? null : raw.Text);
    }

    static void WriteValue(Utf8JsonWriter w, ColType type, string? text)
    {
        switch (type)
        {
            case ColType.Int:
                w.WriteNumberValue(text == null ? 0L : long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
                break;
            case ColType.Float:
                w.WriteNumberValue(text == null ? 0.0 : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
                break;
            default:
                w.WriteStringValue(text ?? "");
                break;
        }
    }
}
