namespace RISL.Application.Import;

/// <summary>Одна логическая запись CSV вместе с номером строки, с которой она началась.</summary>
/// <remarks>
/// Номер физической строки нужен отчёту об импорте: «ошибка в строке 417» админ
/// проверит в исходном файле, а порядковый номер записи — нет.
/// </remarks>
public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>
/// Разбор CSV по правилам RFC 4180: кавычки экранируют разделитель и перевод строки,
/// удвоенная кавычка внутри поля означает саму кавычку.
/// </summary>
/// <remarks>
/// Своя реализация вместо библиотеки: нужен ровно этот набор правил плюс определение
/// разделителя, а описания слов вполне могут содержать запятые и кавычки.
/// </remarks>
public static class DelimitedTextParser
{
    private static readonly char[] Candidates = [';', ',', '\t'];

    /// <summary>
    /// Угадывает разделитель по первой строке: Excel в русской локали сохраняет с «;»,
    /// выгрузки из таблиц — с «,».
    /// </summary>
    public static char DetectDelimiter(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return ';';
        }

        var lineEnd = content.IndexOfAny(['\r', '\n']);
        var firstLine = lineEnd < 0 ? content : content[..lineEnd];

        var best = ';';
        var bestCount = 0;

        foreach (var candidate in Candidates)
        {
            var count = CountOutsideQuotes(firstLine, candidate);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>Разбирает содержимое в записи. Полностью пустые строки пропускаются.</summary>
    public static IReadOnlyList<CsvRow> Parse(string content, char delimiter)
    {
        var rows = new List<CsvRow>();
        if (string.IsNullOrEmpty(content))
        {
            return rows;
        }

        // BOM пережил бы все сравнения заголовков и сломал первую колонку.
        if (content[0] == '﻿')
        {
            content = content[1..];
        }

        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var index = 0;
        var physicalLine = 1;
        var recordLine = 1;

        while (index < content.Length)
        {
            var symbol = content[index];

            if (inQuotes)
            {
                if (symbol == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                if (symbol == '\n')
                {
                    physicalLine++;
                }

                field.Append(symbol);
                index++;
                continue;
            }

            if (symbol == '"' && field.Length == 0)
            {
                inQuotes = true;
                index++;
                continue;
            }

            if (symbol == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                index++;
                continue;
            }

            if (symbol is '\r' or '\n')
            {
                // \r\n считаем одним переводом строки.
                if (symbol == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }

                fields.Add(field.ToString());
                field.Clear();
                AppendRow(rows, fields, recordLine);
                fields.Clear();

                index++;
                physicalLine++;
                recordLine = physicalLine;
                continue;
            }

            field.Append(symbol);
            index++;
        }

        fields.Add(field.ToString());
        AppendRow(rows, fields, recordLine);

        return rows;
    }

    private static void AppendRow(List<CsvRow> rows, List<string> fields, int lineNumber)
    {
        if (fields.All(string.IsNullOrWhiteSpace))
        {
            return;
        }

        rows.Add(new CsvRow(lineNumber, [.. fields.Select(value => value.Trim())]));
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;

        foreach (var symbol in line)
        {
            if (symbol == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (symbol == delimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }
}
