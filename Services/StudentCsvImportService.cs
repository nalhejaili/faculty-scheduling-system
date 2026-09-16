using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TrainerScheduler.Data.Entities;

namespace TrainerScheduler.Services
{
    public sealed record StudentImportPlanItem(int CourseId, int Priority, bool IsRepeat, string? TermKey);

    public sealed class StudentImportItem
    {
        public string StudentNo { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;
        public int DepartmentId { get; init; }
        public int? Level { get; init; }
        public List<StudentImportPlanItem> Plans { get; } = new();
    }

    public sealed class StudentCsvImportBuildResult
    {
        public List<StudentImportItem> Items { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public int RowsRead { get; internal set; }
        public int RowsSkipped { get; internal set; }
        public int PlanRowsPrepared { get; internal set; }
    }

    public static class StudentCsvImportService
    {
        private static readonly string[] StudentNoAliases = { "Student Number", "Student No", "Student ID", "University ID", "\u0631\u0642\u0645 \u0627\u0644\u0637\u0627\u0644\u0628" };
        private static readonly string[] StudentNameAliases = { "Student Name", "Full Name", "\u0627\u0633\u0645 \u0627\u0644\u0637\u0627\u0644\u0628" };
        private static readonly string[] DepartmentAliases = { "Department", "Academic Department", "\u0627\u0644\u0642\u0633\u0645" };
        private static readonly string[] LevelAliases = { "Academic Level", "Level", "\u0627\u0644\u0645\u0633\u062A\u0648\u0649", "Level Number" };
        private static readonly string[] CourseCodeAliases = { "Course Code", "Course", "Module Code", "\u0627\u0644\u0645\u0642\u0631\u0631", "\u0631\u0645\u0632 \u0627\u0644\u0645\u0642\u0631\u0631", "Course ID" };
        private static readonly string[] CourseNameAliases = { "Course Name", "Course Title", "Module Name", "\u0627\u0633\u0645 \u0627\u0644\u0645\u0642\u0631\u0631" };
        private static readonly string[] PriorityAliases = { "Priority", "Preference", "\u0627\u0644\u0623\u0648\u0644\u0648\u064A\u0629" };
        private static readonly string[] RepeatAliases = { "Repeat", "Retake", "Is Repeat", "\u0625\u0639\u0627\u062F\u0629", "\u0645\u0639\u0627\u062F" };
        private static readonly string[] TermAliases = { "Term Key", "Term", "Semester", "Academic Term", "\u0627\u0644\u0641\u0635\u0644", "\u0627\u0644\u0641\u0635\u0644 \u0627\u0644\u062A\u062F\u0631\u064A\u0628\u064A" };

        public static StudentCsvImportBuildResult ParseAndResolve(
            string filePath,
            IEnumerable<DepartmentEntity> departments,
            IEnumerable<CourseEntity> courses)
        {
            var result = new StudentCsvImportBuildResult();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                result.Errors.Add("The selected CSV file was not found.");
                return result;
            }

            var departmentList = departments?.ToList() ?? new List<DepartmentEntity>();
            var courseList = courses?.ToList() ?? new List<CourseEntity>();

            if (departmentList.Count == 0)
            {
                result.Errors.Add("No departments are available in the application. Add departments before importing students.");
                return result;
            }

            if (courseList.Count == 0)
                result.Warnings.Add("No courses are currently available in the application. Student profiles can still be imported, but study-plan rows will be skipped.");

            try
            {
                var encoding = DetectEncoding(filePath);
                string? headerLine;
                using (var fs = OpenReadShared(filePath))
                using (var sr = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: true))
                {
                    headerLine = sr.ReadLine();
                }

                if (string.IsNullOrWhiteSpace(headerLine))
                {
                    result.Errors.Add("The selected file is empty or does not include a header row.");
                    return result;
                }

                var delimiter = DetectDelimiter(headerLine);
                var headers = SplitCsvLine(headerLine, delimiter);
                var normalizedHeaders = headers.Select(NormalizeHeader).ToList();
                var indexByHeader = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < normalizedHeaders.Count; i++)
                {
                    var key = normalizedHeaders[i];
                    if (string.IsNullOrWhiteSpace(key))
                        continue;
                    if (!indexByHeader.ContainsKey(key))
                        indexByHeader[key] = i;
                }

                int FindIndex(params string[] aliases)
                {
                    foreach (var alias in aliases)
                    {
                        var key = NormalizeHeader(alias);
                        if (indexByHeader.TryGetValue(key, out var idx))
                            return idx;
                    }
                    return -1;
                }

                var studentNoIdx = FindIndex(StudentNoAliases);
                var studentNameIdx = FindIndex(StudentNameAliases);
                var deptIdx = FindIndex(DepartmentAliases);
                var levelIdx = FindIndex(LevelAliases);
                var courseCodeIdx = FindIndex(CourseCodeAliases);
                var courseNameIdx = FindIndex(CourseNameAliases);
                var priorityIdx = FindIndex(PriorityAliases);
                var repeatIdx = FindIndex(RepeatAliases);
                var termIdx = FindIndex(TermAliases);

                if (studentNoIdx < 0) result.Errors.Add("A required column is missing. Accepted names: " + string.Join(" / ", StudentNoAliases));
                if (studentNameIdx < 0) result.Errors.Add("A required column is missing. Accepted names: " + string.Join(" / ", StudentNameAliases));
                if (deptIdx < 0) result.Errors.Add("A required column is missing. Accepted names: " + string.Join(" / ", DepartmentAliases));
                if (result.Errors.Count > 0)
                    return result;

                var itemsByStudentNo = new Dictionary<string, StudentImportItem>(StringComparer.OrdinalIgnoreCase);
                using var fs2 = OpenReadShared(filePath);
                using var sr2 = new StreamReader(fs2, encoding, detectEncodingFromByteOrderMarks: true);
                _ = sr2.ReadLine();
                var lineNo = 1;
                while (!sr2.EndOfStream)
                {
                    var line = sr2.ReadLine();
                    lineNo++;
                    if (line is null || string.IsNullOrWhiteSpace(line))
                        continue;

                    result.RowsRead++;
                    var cols = SplitCsvLine(line, delimiter);

                    string Get(int idx) => idx >= 0 && idx < cols.Count ? (cols[idx] ?? string.Empty).Trim() : string.Empty;

                    var studentNo = Get(studentNoIdx);
                    var studentName = Get(studentNameIdx);
                    var departmentText = Get(deptIdx);
                    var levelText = Get(levelIdx);
                    var courseRef = Get(courseCodeIdx);
                    var courseName = Get(courseNameIdx);
                    var priorityText = Get(priorityIdx);
                    var repeatText = Get(repeatIdx);
                    var termKey = Get(termIdx);

                    if (string.IsNullOrWhiteSpace(studentNo) || string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(departmentText))
                    {
                        result.RowsSkipped++;
                        result.Warnings.Add($"Line {lineNo}: student number, student name, and department are required.");
                        continue;
                    }

                    if (!TryResolveDepartmentId(departmentText, departmentList, out var departmentId))
                    {
                        result.RowsSkipped++;
                        result.Warnings.Add($"Line {lineNo}: department '{departmentText}' was not found in the application.");
                        continue;
                    }

                    int? level = null;
                    if (!string.IsNullOrWhiteSpace(levelText))
                    {
                        if (int.TryParse(levelText, out var parsedLevel) && parsedLevel > 0)
                            level = parsedLevel;
                        else
                            result.Warnings.Add($"Line {lineNo}: academic level '{levelText}' is invalid and was ignored.");
                    }

                    if (!itemsByStudentNo.TryGetValue(studentNo, out var item))
                    {
                        item = new StudentImportItem
                        {
                            StudentNo = studentNo,
                            StudentName = studentName,
                            DepartmentId = departmentId,
                            Level = level
                        };
                        itemsByStudentNo[studentNo] = item;
                    }
                    else
                    {
                        if (!string.Equals(item.StudentName, studentName, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Warnings.Add($"Line {lineNo}: student '{studentNo}' appears with different names. The latest value was used.");
                            item = new StudentImportItem
                            {
                                StudentNo = item.StudentNo,
                                StudentName = studentName,
                                DepartmentId = departmentId,
                                Level = level ?? item.Level
                            };
                            item.Plans.AddRange(itemsByStudentNo[studentNo].Plans);
                            itemsByStudentNo[studentNo] = item;
                        }
                        else if (item.DepartmentId != departmentId || (level.HasValue && item.Level != level))
                        {
                            result.Warnings.Add($"Line {lineNo}: student '{studentNo}' appears with different department/level values. The latest values were used.");
                            var updated = new StudentImportItem
                            {
                                StudentNo = item.StudentNo,
                                StudentName = item.StudentName,
                                DepartmentId = departmentId,
                                Level = level ?? item.Level
                            };
                            updated.Plans.AddRange(item.Plans);
                            item = updated;
                            itemsByStudentNo[studentNo] = item;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(courseRef) && string.IsNullOrWhiteSpace(courseName))
                        continue;

                    if (courseList.Count == 0)
                    {
                        result.RowsSkipped++;
                        continue;
                    }

                    if (!TryResolveCourseId(courseRef, courseName, departmentId, courseList, out var courseId, out var courseWarning))
                    {
                        result.RowsSkipped++;
                        result.Warnings.Add($"Line {lineNo}: {courseWarning}");
                        continue;
                    }

                    var resolvedCourse = courseList.FirstOrDefault(c => c.Id == courseId);
                    var importedStudent = new StudentEntity
                    {
                        StudentNo = studentNo,
                        FullName = studentName,
                        DepartmentId = departmentId,
                        Level = level
                    };

                    if (resolvedCourse is null)
                    {
                        result.RowsSkipped++;
                        result.Warnings.Add($"Line {lineNo}: the resolved course could not be loaded from the current course list.");
                        continue;
                    }

                    if (!StudentAcademicRegistrationService.IsCourseAllowedForStudent(importedStudent, resolvedCourse, departmentList))
                    {
                        result.RowsSkipped++;
                        result.Warnings.Add($"Line {lineNo}: course '{resolvedCourse.Name}' is not valid for the student's department.");
                        continue;
                    }

                    if (!StudentAcademicRegistrationService.IsCourseWithinLevelCeiling(level, resolvedCourse))
                    {
                        result.RowsSkipped++;
                        result.Warnings.Add($"Line {lineNo}: course '{resolvedCourse.Name}' is above the student's current academic level.");
                        continue;
                    }

                    var priority = 0;
                    if (!string.IsNullOrWhiteSpace(priorityText) && !int.TryParse(priorityText, out priority))
                    {
                        priority = 0;
                        result.Warnings.Add($"Line {lineNo}: priority '{priorityText}' is invalid and was reset to 0.");
                    }

                    var isRepeat = ParseBool(repeatText);
                    var existingPlanIndex = item.Plans.FindIndex(p => p.CourseId == courseId);
                    var plan = new StudentImportPlanItem(courseId, priority, isRepeat, AcademicTermKeyService.NormalizeForStorage(termKey));
                    if (existingPlanIndex >= 0)
                    {
                        item.Plans[existingPlanIndex] = plan;
                        result.Warnings.Add($"Line {lineNo}: duplicate course entry for student '{studentNo}' was merged.");
                    }
                    else
                    {
                        item.Plans.Add(plan);
                    }
                    result.PlanRowsPrepared++;
                }

                foreach (var item in itemsByStudentNo.Values.OrderBy(x => x.StudentNo, StringComparer.OrdinalIgnoreCase))
                    result.Items.Add(item);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Unable to read or parse the CSV file: " + ex.Message);
            }

            return result;
        }

        private static bool TryResolveDepartmentId(string text, List<DepartmentEntity> departments, out int departmentId)
        {
            departmentId = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (int.TryParse(text, out var numericId) && departments.Any(d => d.Id == numericId))
            {
                departmentId = numericId;
                return true;
            }

            var normalized = NormalizeText(text);
            var match = departments.FirstOrDefault(d => NormalizeText(d.Name) == normalized);
            if (match is not null)
            {
                departmentId = match.Id;
                return true;
            }

            return false;
        }

        private static bool TryResolveCourseId(string courseRef, string courseName, int departmentId, List<CourseEntity> courses, out int courseId, out string warning)
        {
            courseId = 0;
            warning = string.Empty;

            var preferred = courses.Where(c => c.DepartmentId == departmentId || c.IsGeneralCourse).ToList();

            if (!string.IsNullOrWhiteSpace(courseRef))
            {
                if (int.TryParse(courseRef, out var numericId))
                {
                    var byId = courses.FirstOrDefault(c => c.Id == numericId);
                    if (byId is not null)
                    {
                        courseId = byId.Id;
                        return true;
                    }
                }

                var normalizedRef = NormalizeText(courseRef);
                var byCode = preferred.FirstOrDefault(c => NormalizeText(c.CourseCode) == normalizedRef)
                             ?? courses.FirstOrDefault(c => NormalizeText(c.CourseCode) == normalizedRef);
                if (byCode is not null)
                {
                    courseId = byCode.Id;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(courseName))
            {
                var normalizedName = NormalizeText(courseName);
                var matches = preferred.Where(c => NormalizeText(c.Name) == normalizedName).ToList();
                if (matches.Count == 1)
                {
                    courseId = matches[0].Id;
                    return true;
                }

                if (matches.Count == 0)
                {
                    matches = courses.Where(c => NormalizeText(c.Name) == normalizedName).ToList();
                    if (matches.Count == 1)
                    {
                        courseId = matches[0].Id;
                        return true;
                    }
                }

                if (matches.Count > 1)
                {
                    warning = $"course name '{courseName}' matches more than one course in the application";
                    return false;
                }
            }

            warning = !string.IsNullOrWhiteSpace(courseRef)
                ? $"course '{courseRef}' was not found in the application"
                : $"course '{courseName}' was not found in the application";
            return false;
        }

        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = NormalizeText(value);
            return normalized is "1" or "true" or "yes" or "y" or "repeat" or "retake" or "\u0645\u0639\u0627\u062F" or "\u0627\u0639\u0627\u062F\u0629" or "\u0625\u0639\u0627\u062F\u0629";
        }

        private static FileStream OpenReadShared(string filePath)
            => new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        private static Encoding DetectEncoding(string filePath)
        {
            using var fs = OpenReadShared(filePath);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (sr.Peek() >= 0) _ = sr.Read();
            return sr.CurrentEncoding;
        }

        private static char DetectDelimiter(string line)
        {
            var comma = SplitCsvLine(line, ',').Count;
            var semi = SplitCsvLine(line, ';').Count;
            var tab = SplitCsvLine(line, '\t').Count;
            return new[] { (',', comma), (';', semi), ('\t', tab) }.OrderByDescending(x => x.Item2).First().Item1;
        }

        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            var res = new List<string>();
            var current = new StringBuilder();
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

        private static string NormalizeHeader(string value) => NormalizeText(value);

        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Replace('\u00A0', ' ').Trim();
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            return normalized.ToUpperInvariant();
        }
    }
}
