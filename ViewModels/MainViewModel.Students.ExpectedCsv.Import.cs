using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Data.Entities;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public ICommand ImportExpectedRegistrationCsvCommand { get; private set; } = default!;

        private async Task ImportExpectedRegistrationCsvAsync()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("This action is available to administrators only.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Import Expected Registration Report (CSV)"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var validation = ExpectedRegistrationCsv.Validate(dlg.FileName);
                if (!validation.IsValid || validation.Schema is null)
                {
                    var msg = new StringBuilder();
                    msg.AppendLine("The application could not understand the report schema.");
                    foreach (var e in validation.Errors.Take(15))
                        msg.AppendLine("- " + e);
                    if (validation.Errors.Count > 15)
                        msg.AppendLine($"... (Total errors: {validation.Errors.Count})");
                    MessageBox.Show(msg.ToString(), "Expected Registration Import", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var schema = validation.Schema;
                var rows = ExpectedRegistrationCsvReader.ReadRows(dlg.FileName, schema)
                    .Where(r => !string.IsNullOrWhiteSpace(r.StudentNo) && !string.IsNullOrWhiteSpace(r.CourseCode))
                    .ToList();

                if (rows.Count == 0)
                {
                    MessageBox.Show("The report does not contain any usable rows.", "Expected Registration Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Load mapping table
                var existingMaps = await _sqlite.GetCourseCodeMapsAsync();
                var mapDict = existingMaps
                    .Where(m => m.CourseId.HasValue && m.CourseId.Value > 0 && !string.IsNullOrWhiteSpace(m.CourseCode))
                    .ToDictionary(m => NormalizeCourseCode(m.CourseCode), m => m.CourseId!.Value, StringComparer.OrdinalIgnoreCase);

                // We will upsert mappings that we auto-detect + register missing ones for later UI mapping.
                var mapUpserts = new Dictionary<string, CourseCodeMapEntity>(StringComparer.OrdinalIgnoreCase);

                Store.StudentEnrollments.Clear();
                Store.StudentPlans.Clear();
                Store.Students.Clear();

                var studentCount = 0;
                var planCount = 0;
                var skippedPlans = 0;
                var missingMappings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                // Cache courses by id + by normalized name for simple auto-matching.
                var coursesById = Store.Courses.ToDictionary(c => c.Id);
                var coursesByNormCode = Store.Courses
                    .Where(c => !string.IsNullOrWhiteSpace(c.CourseCode))
                    .GroupBy(c => NormalizeCourseCode(c.CourseCode!))
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var coursesByNormName = Store.Courses
                    .GroupBy(c => NormalizeArabic(c.Name))
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Per-student de-duplication: do NOT add the same course twice (the report repeats theory+practical).
                var perStudentCourseSet = new Dictionary<int, HashSet<string>>();

                foreach (var r in rows)
                {
                    var studentNo = r.StudentNo.Trim();
                    var studentId = MakeStableStudentId(studentNo);

                    if (!perStudentCourseSet.ContainsKey(studentId))
                        perStudentCourseSet[studentId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (Store.Students.All(s => s.Id != studentId))
                    {
                        var deptId = TryResolveDepartmentId(r.DepartmentName);
                        Store.Students.Add(new Student
                        {
                            Id = studentId,
                            StudentNo = studentNo,
                            FullName = string.IsNullOrWhiteSpace(r.StudentName) ? $"Student {studentNo}" : r.StudentName.Trim(),
                            DepartmentId = deptId,
                            Level = null
                        });
                        studentCount++;
                    }

                    var currentStudent = Store.Students.First(s => s.Id == studentId);
                    var studentDeptId = currentStudent.DepartmentId;

                    var codeNorm = NormalizeCourseCode(r.CourseCode);
                    if (!perStudentCourseSet[studentId].Add(codeNorm))
                        continue; // duplicate for same student (e.g., theory+practical rows)

                    if (!TryResolveCourseId(codeNorm, r.CourseName, studentDeptId, mapDict, coursesByNormCode, coursesById, coursesByNormName, out var courseId, out var autoMappedCourseId))
                    {
                        skippedPlans++;
                        missingMappings[codeNorm] = string.IsNullOrWhiteSpace(r.CourseName) ? null : r.CourseName.Trim();
                        continue;
                    }

                    if (autoMappedCourseId.HasValue)
                    {
                        // Register auto-mapping so it appears in the mapping tab.
                        mapUpserts[codeNorm] = new CourseCodeMapEntity
                        {
                            CourseCode = codeNorm,
                            CourseId = autoMappedCourseId,
                            CourseName = string.IsNullOrWhiteSpace(r.CourseName) ? null : r.CourseName.Trim(),
                            Notes = "Auto-mapped",
                            UpdatedUtc = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        // Still update last seen name if needed.
                        if (!mapUpserts.ContainsKey(codeNorm))
                        {
                            mapUpserts[codeNorm] = new CourseCodeMapEntity
                            {
                                CourseCode = codeNorm,
                                CourseId = courseId,
                                CourseName = string.IsNullOrWhiteSpace(r.CourseName) ? null : r.CourseName.Trim(),
                                Notes = null,
                                UpdatedUtc = DateTime.UtcNow
                            };
                        }
                    }

                    Store.StudentPlans.Add(new StudentPlan
                    {
                        StudentId = studentId,
                        CourseId = courseId,
                        TermKey = AcademicTermKeyService.NormalizeForStorage(r.TermKey) ?? string.Empty,
                        Priority = 0,
                        IsRepeat = false
                    });
                    planCount++;
                }

                // Upsert missing mappings (CourseId = null) so the admin can complete them.
                foreach (var kv in missingMappings)
                {
                    if (!mapUpserts.ContainsKey(kv.Key))
                    {
                        mapUpserts[kv.Key] = new CourseCodeMapEntity
                        {
                            CourseCode = kv.Key,
                            CourseId = null,
                            CourseName = kv.Value,
                            Notes = "Needs mapping",
                            UpdatedUtc = DateTime.UtcNow
                        };
                    }
                }

                if (mapUpserts.Count > 0)
                    await _sqlite.SaveCourseCodeMapsAsync(mapUpserts.Values, deleteMissing: false);

                // Persist
                await _sqlite.SaveStudentsAsync(Store, scopeDepartmentId: null);
                await _sqlite.SaveStudentPlansAsync(Store, scopeDepartmentId: null);
                await _sqlite.SaveStudentEnrollmentsAsync(Store, scopeDepartmentId: null);

                var sb = new StringBuilder();
                sb.AppendLine("The expected registration report has been imported successfully.");
                sb.AppendLine($"- Students: {studentCount}");
                sb.AppendLine($"- Registration plans (after deduplication): {planCount}");
                if (skippedPlans > 0)
                {
                    sb.AppendLine($"- {skippedPlans} row(s)/course(s) were skipped because no course-code mapping was found.");
                    sb.AppendLine("Open (Data Management > Course Code Mapping), complete the mapping, then import again.");
                }

                MessageBox.Show(sb.ToString(), "Expected Registration Import", MessageBoxButton.OK, MessageBoxImage.Information);

                // Refresh footer counts.
                NotifyStudentsChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string NormalizeCourseCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var s = code.Trim();
            s = s.Replace("\u200f", "").Replace("\u200e", "");

            // Remove whitespace + normalize Arabic/Persian digits.
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch)) continue;

                if (ch is >= '\u0660' and <= '\u0669') // Arabic-Indic
                {
                    sb.Append((char)('0' + (ch - '\u0660')));
                    continue;
                }
                if (ch is >= '\u06F0' and <= '\u06F9') // Eastern Arabic-Indic (Persian)
                {
                    sb.Append((char)('0' + (ch - '\u06F0')));
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString().ToUpperInvariant();
        }

        private static string NormalizeArabic(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch)) continue;
                var c = ch;
                // unify common variants
                if (c is '\u0623' or '\u0625' or '\u0622') c = '\u0627';
                if (c == '\u0629') c = '\u0647';
                if (c == '\u0649') c = '\u064A';
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private int? TryResolveDepartmentId(string deptName)
        {
            if (string.IsNullOrWhiteSpace(deptName)) return null;
            var target = NormalizeArabic(deptName);
            var exact = Store.Departments.FirstOrDefault(d => NormalizeArabic(d.Name) == target);
            if (exact is not null) return exact.Id;

            // Fallback: contains match (helps when report adds prefix/suffix)
            var contains = Store.Departments.FirstOrDefault(d => target.Contains(NormalizeArabic(d.Name)) || NormalizeArabic(d.Name).Contains(target));
            return contains?.Id;
        }

        private static int MakeStableStudentId(string studentNo)
        {
            if (int.TryParse(studentNo, out var id) && id > 0) return id;
            // FNV-1a 32-bit
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in studentNo)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                var v = (int)(hash & 0x7fffffff);
                return v == 0 ? 1 : v;
            }
        }

        private static bool TryResolveCourseId(
            string courseCodeNorm,
            string courseName,
            int? studentDeptId,
            Dictionary<string, int> codeToCourseId,
            Dictionary<string, List<Course>> coursesByNormCode,
            Dictionary<int, Course> coursesById,
            Dictionary<string, List<Course>> coursesByNormName,
            out int courseId,
            out int? autoMappedCourseId)
        {
            courseId = 0;
            autoMappedCourseId = null;

            if (codeToCourseId.TryGetValue(courseCodeNorm, out var mappedId))
            {
                courseId = mappedId;
                return true;
            }

            if (coursesByNormCode.TryGetValue(courseCodeNorm, out var codeCandidates) && codeCandidates.Count > 0)
            {
                // If the code is unique across the whole dataset, take it.
                if (codeCandidates.Count == 1)
                {
                    courseId = codeCandidates[0].Id;
                    autoMappedCourseId = courseId;
                    codeToCourseId[courseCodeNorm] = courseId;
                    return true;
                }

                // If multiple (rare), prefer the student's department if known.
                if (studentDeptId.HasValue)
                {
                    var deptCandidates = codeCandidates.Where(c => c.DepartmentId == studentDeptId.Value).ToList();
                    if (deptCandidates.Count == 1)
                    {
                        courseId = deptCandidates[0].Id;
                        autoMappedCourseId = courseId;
                        codeToCourseId[courseCodeNorm] = courseId;
                        return true;
                    }

                    // If still multiple, try match by normalized name inside the department.
                    var normName2 = NormalizeArabic(courseName);
                    if (!string.IsNullOrWhiteSpace(normName2))
                    {
                        var nameMatch = deptCandidates.Where(c => NormalizeArabic(c.Name) == normName2).ToList();
                        if (nameMatch.Count == 1)
                        {
                            courseId = nameMatch[0].Id;
                            autoMappedCourseId = courseId;
                            codeToCourseId[courseCodeNorm] = courseId;
                            return true;
                        }
                    }
                }
            }

            // Auto: if code is numeric and already matches an internal course id.
            if (int.TryParse(courseCodeNorm, out var numericId) && numericId > 0 && coursesById.ContainsKey(numericId))
            {
                courseId = numericId;
                autoMappedCourseId = numericId;
                codeToCourseId[courseCodeNorm] = numericId;
                return true;
            }

            // Auto: try match by course name (exact after normalization). Must be unique.
            var normName = NormalizeArabic(courseName);
            if (!string.IsNullOrWhiteSpace(normName) && coursesByNormName.TryGetValue(normName, out var candidates) && candidates.Count == 1)
            {
                courseId = candidates[0].Id;
                autoMappedCourseId = courseId;
                codeToCourseId[courseCodeNorm] = courseId;
                return true;
            }

            return false;
        }
    }
}
