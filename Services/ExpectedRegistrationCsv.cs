using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Services
{
    public sealed class ExpectedRegistrationCsvValidationReport
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public ExpectedRegistrationCsvSchema? Schema { get; internal set; }
        public int DataRowsCount { get; internal set; }
    }

    /// <summary>
    /// - Trims header spaces (including NBSP)
    /// - Detects delimiter (',' or ';' or '\t')
    /// - Validates required columns exist
    /// </summary>
    public static class ExpectedRegistrationCsv
    {
        
        private static FileStream OpenReadShared(string filePath)
        {
            // Allow reading even if another process (e.g., Excel) has the file open.
            // FileShare.Delete helps when the file is replaced during export.
            return new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }

// Canonical localized headers used by existing reports.
        // The validator now also accepts common English university-style headers.
        public const string ColStudentNo = "\u0631\u0642\u0645 \u0627\u0644\u0637\u0627\u0644\u0628";
        public const string ColStudentName = "\u0627\u0633\u0645 \u0627\u0644\u0637\u0627\u0644\u0628";
        public const string ColDepartment = "\u0627\u0644\u0642\u0633\u0645";
        public const string ColSpecialty = "\u0627\u0644\u062A\u062E\u0635\u0635";
        public const string ColTerm = "\u0627\u0644\u0641\u0635\u0644 \u0627\u0644\u062A\u062F\u0631\u064A\u0628\u064A";
        public const string ColCourseCode = "\u0627\u0644\u0645\u0642\u0631\u0631";      // Legacy localized course-code header.
        public const string ColCourseName = "\u0627\u0633\u0645 \u0627\u0644\u0645\u0642\u0631\u0631";
        public const string ColScheduleKind = "\u0646\u0648\u0639 \u0627\u0644\u062C\u062F\u0648\u0644\u0629";

        // Optional columns (used later for filtering / extra info)
        public const string ColTraineeStatus = "\u062D\u0627\u0644\u0629 \u0627\u0644\u0645\u062A\u062F\u0631\u0628";
        public const string ColRegistrationStatus = "\u062D\u0627\u0644\u0629 \u0627\u0644\u062A\u0633\u062C\u064A\u0644";
        public const string ColContactHours = "\u0633.\u0627\u062A\u0635\u0627\u0644";
        public const string ColCreditHours = "\u0633.\u0645\u0639\u062A\u0645\u062F\u0629";

        private static readonly string[] StudentNoAliases =
        {
            ColStudentNo,
            "Student No",
            "Student Number",
            "University ID",
            "Student ID"
        };

        private static readonly string[] StudentNameAliases =
        {
            ColStudentName,
            "Student Name",
            "Full Name"
        };

        private static readonly string[] DepartmentAliases =
        {
            ColDepartment,
            "Department",
            "Academic Department"
        };

        private static readonly string[] SpecialtyAliases =
        {
            ColSpecialty,
            "Specialty",
            "Specialisation",
            "Specialization",
            "Programme",
            "Program",
            "Academic Programme",
            "Academic Program"
        };

        private static readonly string[] TermAliases =
        {
            ColTerm,
            "Academic Term",
            "Term",
            "Semester",
            "Study Term"
        };

        private static readonly string[] CourseCodeAliases =
        {
            ColCourseCode,
            "Course",
            "Course Code",
            "Module Code"
        };

        private static readonly string[] CourseNameAliases =
        {
            ColCourseName,
            "Course Name",
            "Course Title",
            "Module Name",
            "Module Title"
        };

        private static readonly string[] ScheduleKindAliases =
        {
            ColScheduleKind,
            "Schedule Type",
            "Class Type",
            "Session Type",
            "Delivery Type",
            "Component Type"
        };

        private static readonly string[] StudentStatusAliases =
        {
            ColTraineeStatus,
            "Student Status"
        };

        private static readonly string[] RegistrationStatusAliases =
        {
            ColRegistrationStatus,
            "Registration Status",
            "Enrollment Status"
        };

        private static readonly string[] ContactHoursAliases =
        {
            ColContactHours,
            "Contact Hours"
        };

        private static readonly string[] CreditHoursAliases =
        {
            ColCreditHours,
            "Credit Hours"
        };

        public static ExpectedRegistrationCsvValidationReport Validate(string filePath)
        {
            var report = new ExpectedRegistrationCsvValidationReport();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                report.Errors.Add("The file was not found.");
                return report;
            }

            try
            {
                // Read first line raw to detect delimiter.
                var encoding = DetectEncoding(filePath);
                string? firstLine;
                using (var fs1 = OpenReadShared(filePath))
                using (var sr = new StreamReader(fs1, encoding, detectEncodingFromByteOrderMarks: true))
                {
                    firstLine = sr.ReadLine();
                }

                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    report.Errors.Add("The file is empty or does not contain a header row.");
                    return report;
                }

                var delimiter = DetectDelimiter(firstLine);

                // Parse header (handles quoted fields).
                var rawHeaders = SplitCsvLine(firstLine, delimiter);
                if (rawHeaders.Count == 0)
                {
                    report.Errors.Add("Unable to read the CSV header row.");
                    return report;
                }

                var normalized = rawHeaders.Select(NormalizeHeader).ToList();

                // Build index map. If duplicates after normalization, keep first and warn.
                var indexByHeader = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < normalized.Count; i++)
                {
                    var key = normalized[i];
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (!indexByHeader.ContainsKey(key))
                        indexByHeader[key] = i;
                    else
                        report.Warnings.Add($"Duplicate column after normalization: '{key}' (only the first occurrence will be used).");
                }

                int Require(params string[] aliases)
                {
                    foreach (var alias in aliases)
                    {
                        var k = NormalizeHeader(alias);
                        if (indexByHeader.TryGetValue(k, out var idx)) return idx;
                    }

                    report.Errors.Add($"A required column is missing. Accepted names: {string.Join(" / ", aliases)}");
                    return -1;
                }

                var studentNoIdx = Require(StudentNoAliases);
                var studentNameIdx = Require(StudentNameAliases);
                var deptIdx = Require(DepartmentAliases);
                var specIdx = Require(SpecialtyAliases);
                var termIdx = Require(TermAliases);
                var courseCodeIdx = Require(CourseCodeAliases);
                var courseNameIdx = Require(CourseNameAliases);
                var kindIdx = Require(ScheduleKindAliases);

                int? Optional(params string[] aliases)
                {
                    foreach (var alias in aliases)
                    {
                        var k = NormalizeHeader(alias);
                        if (indexByHeader.TryGetValue(k, out var idx))
                            return idx;
                    }

                    return null;
                }

                if (report.Errors.Count == 0)
                {
                    report.Schema = new ExpectedRegistrationCsvSchema(
                        filePath,
                        delimiter,
                        rawHeaders,
                        normalized,
                        indexByHeader,
                        studentNoIdx,
                        studentNameIdx,
                        deptIdx,
                        specIdx,
                        termIdx,
                        courseCodeIdx,
                        courseNameIdx,
                        kindIdx,
                        Optional(StudentStatusAliases),
                        Optional(RegistrationStatusAliases),
                        Optional(ContactHoursAliases),
                        Optional(CreditHoursAliases));

                    // Count remaining data rows (best-effort). We do not parse rows here.
                    // Assumption: report does not contain multiline quoted fields.
                    int rows = 0;
                    using var fs2 = OpenReadShared(filePath);
                using var sr2 = new StreamReader(fs2, encoding, detectEncodingFromByteOrderMarks: true);
                    // Skip header
                    sr2.ReadLine();
                    while (!sr2.EndOfStream)
                    {
                        var line = sr2.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        rows++;
                    }
                    report.DataRowsCount = rows;
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add("Unable to read or parse the CSV file: " + ex.Message);
            }

            return report;
        }

        public static string NormalizeHeader(string? header)
        {
            if (header is null) return string.Empty;

            // Normalize spaces, remove NBSP, trim.
            var s = header
                .Replace('\u00A0', ' ')
                .Replace('\u200F', ' ') // RLM
                .Replace('\u200E', ' ') // LRM
                .Trim();

            // Collapse multiple spaces
            while (s.Contains("  ", StringComparison.Ordinal))
                s = s.Replace("  ", " ");

            // Arabic normalization:
            s = s
                .Replace('\u0625', '\u0627')
                .Replace('\u0623', '\u0627')
                .Replace('\u0622', '\u0627');

            // English normalization:
            // unify spacing and common spelling variations so English university reports
            // such as "Specialisation" / "Specialization" and "Programme" / "Program" match.
            s = s.Replace("programme", "program", StringComparison.OrdinalIgnoreCase)
                 .Replace("specialisation", "specialization", StringComparison.OrdinalIgnoreCase)
                 .Replace("enrolment", "enrollment", StringComparison.OrdinalIgnoreCase)
                 .Replace("organisation", "organization", StringComparison.OrdinalIgnoreCase);

            return s;
        }

        private static Encoding DetectEncoding(string filePath)
        {
            // Many exports in KSA are UTF-8 without BOM, but some systems still export Windows-1256.
            // We detect BOM when available, otherwise use a small heuristic.
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            try
            {
                using var fs = OpenReadShared(filePath);

                // UTF-8 BOM
                if (fs.Length >= 3)
                {
                    Span<byte> bom = stackalloc byte[3];
                    fs.Read(bom);
                    fs.Position = 0;
                    if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                }

                // Sample bytes for heuristic
                const int max = 64 * 1024;
                int toRead = (int)Math.Min(fs.Length, max);
                byte[] bytes = new byte[toRead];
                int read = fs.Read(bytes, 0, toRead);
                if (read <= 0)
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

                // Try UTF-8 first
                string utf8 = Encoding.UTF8.GetString(bytes, 0, read);
                int utf8Bad = utf8.Count(ch => ch == '\uFFFD');

                // Try Windows-1256
                Encoding win1256;
                try { win1256 = Encoding.GetEncoding(1256); }
                catch { win1256 = Encoding.UTF8; }
                string cp = win1256.GetString(bytes, 0, read);
                int cpBad = cp.Count(ch => ch == '\uFFFD');

                // Choose the one with fewer replacements
                if (cpBad < utf8Bad)
                    return win1256;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        private static char DetectDelimiter(string headerLine)
        {
            // Common delimiters: comma, semicolon, tab.
            int commas = headerLine.Count(c => c == ',');
            int semis = headerLine.Count(c => c == ';');
            int tabs = headerLine.Count(c => c == '\t');

            if (tabs > commas && tabs > semis) return '\t';
            if (semis > commas) return ';';
            return ',';
        }

        /// <summary>
        /// Minimal CSV split (supports quoted values and escaped quotes "").
        /// </summary>
        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            if (line is null) return result;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];

                if (ch == '"')
                {
                    // Escaped quote inside quoted field: ""
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && ch == delimiter)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            result.Add(sb.ToString());
            return result;
        }
    }
}
