using Alpixa.Core.Localization;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace Alpixa.Infrastructure.Contacts;

public static class TabularReader
{
    public static IEnumerable<string[]> ReadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(path),
            ".csv" or ".txt" or ".tsv" => ReadCsv(File.OpenRead(path)),
            _ => throw new NotSupportedException(Msg.T("Tabular_01", ext))
        };
    }

    public static IEnumerable<string[]> ReadText(string text)
        => ReadCsv(new MemoryStream(Encoding.UTF8.GetBytes(text ?? "")));

    public static IEnumerable<string[]> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var firstLine = reader.ReadLine();
        if (firstLine is null) yield break;
        var delimiter = DetectDelimiter(firstLine);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false
        };

        using (var firstParser = new CsvParser(new StringReader(firstLine), config))
        {
            if (firstParser.Read() && firstParser.Record is { } rec) yield return rec;
        }

        using var parser = new CsvParser(reader, config);
        while (parser.Read())
        {
            var record = parser.Record;
            if (record is null || record.All(string.IsNullOrWhiteSpace)) continue;
            yield return record;
        }
    }

    private static IEnumerable<string[]> ReadXlsx(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        foreach (var row in sheet.RowsUsed())
        {
            var values = new string[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
            {
                var cell = row.Cell(c);
                values[c - 1] = cell.DataType == XLDataType.DateTime
                    ? cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : cell.GetFormattedString().Trim();
            }
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            yield return values;
        }
    }

    public static string DetectDelimiter(string line)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        var best = candidates.Select(c => (c, count: line.Count(ch => ch == c))).OrderByDescending(x => x.count).First();
        return best.count == 0 ? "," : best.c.ToString();
    }
}
