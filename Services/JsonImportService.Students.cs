using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    /// <summary>
    /// Supports either:
    ///   (A) Array file: [ {student...}, ... ] or [ {studentId, courseId, ...}, ... ]
    ///   (B) Bundle object: { "students": [...], "plans": [...] } (also accepts: studentPlans, requests)
    /// </summary>
    public static partial class JsonImportService
    {
        public sealed record StudentsImportReport(
            int StudentsImported,
            int PlansImported,
            int StudentsSkipped,
            int PlansSkipped,
            IReadOnlyList<string> Warnings);

        public static StudentsImportReport ImportStudentsAndPlans(DataStore store, string jsonOrPath,
            bool clearExistingStudents = true,
            bool clearExistingPlans = true)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(jsonOrPath)) throw new ArgumentNullException(nameof(jsonOrPath));

            var warnings = new List<string>();
            string json = NormalizeToJson(jsonOrPath);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var (students, plans, localWarnings) = ParseStudentsAndPlans(root);
            warnings.AddRange(localWarnings);

            int skippedStudents = 0;
            int skippedPlans = 0;

            if (clearExistingStudents && students.Count > 0) store.Students.Clear();
            if (clearExistingPlans && plans.Count > 0) store.StudentPlans.Clear();

            // ---- Students ----
            var seenStudentIds = new HashSet<int>(store.Students.Select(s => s.Id));
            foreach (var s in students)
            {
                if (s.Id <= 0)
                {
                    skippedStudents++;
                    continue;
                }

                if (!seenStudentIds.Add(s.Id))
                {
                    skippedStudents++;
                    continue;
                }

                // Normalize
                s.StudentNo ??= string.Empty;
                s.FullName ??= string.Empty;
                store.Students.Add(s);
            }

            // ---- Plans ----
            var validStudentIds = new HashSet<int>(store.Students.Select(s => s.Id));
            var validCourseIds = new HashSet<int>(store.Courses.Select(c => c.Id));

            foreach (var p in plans)
            {
                if (p.StudentId <= 0 || p.CourseId <= 0)
                {
                    skippedPlans++;
                    continue;
                }

                if (validStudentIds.Count > 0 && !validStudentIds.Contains(p.StudentId))
                {
                    skippedPlans++;
                    continue;
                }

                if (validCourseIds.Count > 0 && !validCourseIds.Contains(p.CourseId))
                {
                    skippedPlans++;
                    continue;
                }

                // Normalize
                p.TermKey = AcademicTermKeyService.NormalizeForStorage(p.TermKey);
                if (p.CreatedUtc == default) p.CreatedUtc = DateTime.UtcNow;

                store.StudentPlans.Add(p);
            }

            // Warnings summary
            if (students.Count > 0 && store.Students.Count == 0)
                warnings.Add("No students were imported. Check the id field in the students file.");

            if (plans.Count > 0 && store.StudentPlans.Count == 0)
                warnings.Add("No study plans were imported. StudentId/CourseId may not match the available data.");

            // Extra warning: courses missing
            if (plans.Count > 0 && store.Courses.Count == 0)
                warnings.Add("Notice: there are currently no courses in the data store, so plans were accepted without CourseId validation.");

            return new StudentsImportReport(
                StudentsImported: store.Students.Count,
                PlansImported: store.StudentPlans.Count,
                StudentsSkipped: skippedStudents,
                PlansSkipped: skippedPlans,
                Warnings: warnings);
        }

        public static StudentsImportReport ImportStudentsOnly(DataStore store, string jsonOrPath, bool clearExisting = true)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));
            var json = NormalizeToJson(jsonOrPath);
            using var doc = JsonDocument.Parse(json);

            var (students, _, warnings) = ParseStudentsAndPlans(doc.RootElement, parsePlans: false);

            if (clearExisting) store.Students.Clear();

            var seen = new HashSet<int>();
            int skipped = 0;

            foreach (var s in students)
            {
                if (s.Id <= 0 || !seen.Add(s.Id)) { skipped++; continue; }
                s.StudentNo ??= string.Empty;
                s.FullName ??= string.Empty;
                store.Students.Add(s);
            }

            return new StudentsImportReport(store.Students.Count, 0, skipped, 0, warnings);
        }

        public static StudentsImportReport ImportPlansOnly(DataStore store, string jsonOrPath, bool clearExisting = true)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));
            var json = NormalizeToJson(jsonOrPath);
            using var doc = JsonDocument.Parse(json);

            var (_, plans, warnings) = ParseStudentsAndPlans(doc.RootElement, parseStudents: false);

            if (clearExisting) store.StudentPlans.Clear();

            int skipped = 0;

            var validStudentIds = new HashSet<int>(store.Students.Select(s => s.Id));
            var validCourseIds = new HashSet<int>(store.Courses.Select(c => c.Id));

            foreach (var p in plans)
            {
                if (p.StudentId <= 0 || p.CourseId <= 0) { skipped++; continue; }

                if (validStudentIds.Count > 0 && !validStudentIds.Contains(p.StudentId)) { skipped++; continue; }
                if (validCourseIds.Count > 0 && !validCourseIds.Contains(p.CourseId)) { skipped++; continue; }

                p.TermKey = AcademicTermKeyService.NormalizeForStorage(p.TermKey);
                if (p.CreatedUtc == default) p.CreatedUtc = DateTime.UtcNow;
                store.StudentPlans.Add(p);
            }

            if (plans.Count > 0 && store.StudentPlans.Count == 0)
                warnings = warnings.Concat(new[] { "No study plans were imported. StudentId/CourseId may not match the available data." }).ToList();

            return new StudentsImportReport(0, store.StudentPlans.Count, 0, skipped, warnings);
        }

        // -------------------------
        // Parsing helpers
        // -------------------------

        private static (List<Student> students, List<StudentPlan> plans, List<string> warnings) ParseStudentsAndPlans(
            JsonElement root,
            bool parseStudents = true,
            bool parsePlans = true)
        {
            var warnings = new List<string>();
            var students = new List<Student>();
            var plans = new List<StudentPlan>();

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() }
            };

            if (root.ValueKind == JsonValueKind.Array)
            {
                // Decide whether it's students or plans based on first element.
                if (root.GetArrayLength() == 0)
                    return (students, plans, warnings);

                var first = root[0];
                bool looksLikePlan = first.ValueKind == JsonValueKind.Object &&
                                     (HasProperty(first, "studentId") || HasProperty(first, "courseId"));

                if (looksLikePlan)
                {
                    if (parsePlans)
                        plans.AddRange(DeserializeList<StudentPlan>(root, opts, warnings));
                }
                else
                {
                    if (parseStudents)
                        students.AddRange(DeserializeList<Student>(root, opts, warnings));
                }

                return (students, plans, warnings);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Unsupported JSON format. The file must be an Array or an Object.");
                return (students, plans, warnings);
            }

            if (parseStudents)
            {
                if (TryGetProperty(root, "students", out var stEl) && stEl.ValueKind == JsonValueKind.Array)
                {
                    students.AddRange(DeserializeList<Student>(stEl, opts, warnings));
                }
                else if (TryGetProperty(root, "Students", out stEl) && stEl.ValueKind == JsonValueKind.Array)
                {
                    students.AddRange(DeserializeList<Student>(stEl, opts, warnings));
                }
            }

            if (parsePlans)
            {
                if (TryGetProperty(root, "plans", out var plEl) && plEl.ValueKind == JsonValueKind.Array)
                {
                    plans.AddRange(DeserializeList<StudentPlan>(plEl, opts, warnings));
                }
                else if (TryGetProperty(root, "studentPlans", out plEl) && plEl.ValueKind == JsonValueKind.Array)
                {
                    plans.AddRange(DeserializeList<StudentPlan>(plEl, opts, warnings));
                }
                else if (TryGetProperty(root, "requests", out plEl) && plEl.ValueKind == JsonValueKind.Array)
                {
                    plans.AddRange(DeserializeList<StudentPlan>(plEl, opts, warnings));
                }
            }

            // If neither found, try to infer: object might be a student object, or plan object.
            if (students.Count == 0 && plans.Count == 0)
            {
                bool looksLikeStudent = HasProperty(root, "fullName") || HasProperty(root, "studentNo") || HasProperty(root, "departmentId");
                bool looksLikePlan = HasProperty(root, "studentId") || HasProperty(root, "courseId");

                if (looksLikeStudent && parseStudents)
                {
                    var one = DeserializeOne<Student>(root, opts, warnings);
                    if (one != null) students.Add(one);
                }
                else if (looksLikePlan && parsePlans)
                {
                    var one = DeserializeOne<StudentPlan>(root, opts, warnings);
                    if (one != null) plans.Add(one);
                }
                else
                {
                    warnings.Add("The file does not contain 'students' or 'plans' keys.");
                }
            }

            return (students, plans, warnings);
        }

        private static List<T> DeserializeList<T>(JsonElement el, JsonSerializerOptions opts, List<string> warnings)
        {
            try
            {
                return JsonSerializer.Deserialize<List<T>>(el.GetRawText(), opts) ?? new List<T>();
            }
            catch (Exception ex)
            {
                warnings.Add($"Unable to read the {typeof(T).Name} list: {ex.Message}");
                return new List<T>();
            }
        }

        private static T? DeserializeOne<T>(JsonElement el, JsonSerializerOptions opts, List<string> warnings)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(el.GetRawText(), opts);
            }
            catch (Exception ex)
            {
                warnings.Add($"Unable to read a {typeof(T).Name} item: {ex.Message}");
                return default;
            }
        }


        private static bool HasProperty(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object) return false;
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
        {
            value = default;
            if (obj.ValueKind != JsonValueKind.Object) return false;

            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
