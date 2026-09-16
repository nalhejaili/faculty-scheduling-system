using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MiniTrainerScheduler.Services
{
    public static class ExpectedRegistrationCsvReader
    {
        public sealed record DataRow(
            string StudentNo,
            string StudentName,
            string DepartmentName,
            string SpecialtyName,
            string TermKey,
            string CourseCode,
            string CourseName,
            string ScheduleKindText,
            int LineNo);

        public static IEnumerable<DataRow> ReadRows(string filePath, ExpectedRegistrationCsvSchema schema)
        {
            if (schema is null) throw new ArgumentNullException(nameof(schema));

            // Important: the user may keep the report open in Excel.
            // Use FileShare.ReadWrite to avoid "file in use" errors.
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            // Skip header
            sr.ReadLine();

            var maxIndex = new[]
            {
                schema.StudentNoIndex,
                schema.StudentNameIndex,
                schema.DepartmentIndex,
                schema.SpecialtyIndex,
                schema.TermIndex,
                schema.CourseCodeIndex,
                schema.CourseNameIndex,
                schema.ScheduleKindIndex,
            }.Max();

            var lineNo = 1;
            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                lineNo++;
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = SplitCsvLine(line, schema.Delimiter);
                if (cols.Count <= maxIndex) continue; // ignore malformed lines

                yield return new DataRow(
                    StudentNo: (cols[schema.StudentNoIndex] ?? string.Empty).Trim(),
                    StudentName: (cols[schema.StudentNameIndex] ?? string.Empty).Trim(),
                    DepartmentName: (cols[schema.DepartmentIndex] ?? string.Empty).Trim(),
                    SpecialtyName: (cols[schema.SpecialtyIndex] ?? string.Empty).Trim(),
                    TermKey: (cols[schema.TermIndex] ?? string.Empty).Trim(),
                    CourseCode: (cols[schema.CourseCodeIndex] ?? string.Empty).Trim(),
                    CourseName: (cols[schema.CourseNameIndex] ?? string.Empty).Trim(),
                    ScheduleKindText: (cols[schema.ScheduleKindIndex] ?? string.Empty).Trim(),
                    LineNo: lineNo
                );
            }
        }

        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            var res = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    res.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            res.Add(current.ToString());
            return res;
        }
    }
}
