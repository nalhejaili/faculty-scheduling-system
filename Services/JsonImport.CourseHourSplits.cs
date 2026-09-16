using System;
using System.IO;
using System.Text.Json;
using MiniTrainerScheduler.Models;
using CHSplit = MiniTrainerScheduler.Models.CourseHourSplitOverride;

namespace MiniTrainerScheduler.Services
{
    public static class JsonImport
    {
        public static void ImportCourseHourSplitsFromJson(
            DataStore store,
            string jsonOrPath,
            bool clearExisting = true,
            bool preferCourses = true)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(jsonOrPath)) return;

            if (store.CourseHourSplits is null)
                throw new InvalidOperationException("DataStore.CourseHourSplits is null. Initialize it to a List in DataStore.");

            if (clearExisting) store.CourseHourSplits.Clear();

            string text = NormalizeToJson(jsonOrPath);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("courses", out var courses) && courses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in courses.EnumerateArray())
                        LoadOneCourseIfPossible(store, c);
                }
                else
                {
                    LoadOneCourseIfPossible(store, root);
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in root.EnumerateArray())
                    LoadOneCourseIfPossible(store, c);
            }
        }

        private static void LoadOneCourseIfPossible(DataStore store, JsonElement c)
        {
            if (!c.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                return;

            int id = idEl.GetInt32();
            double th = ReadNumber(c, "theoryHours");
            double lab = ReadNumber(c, "practicalHours");

            if (th <= 0 && lab <= 0) return;

            bool preferSame = true;
            if (c.TryGetProperty("preferSameInstructor", out var prefEl))
                preferSame = prefEl.ValueKind == JsonValueKind.True
                             || (prefEl.ValueKind == JsonValueKind.String
                                 && bool.TryParse(prefEl.GetString(), out var b) && b);

            store.CourseHourSplits.Add(new CHSplit
            {
                CourseId = id,
                TheoryHours = th,
                PracticalHours = lab,
                PreferSameInstructor = preferSame
            });
        }

        private static double ReadNumber(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return 0;
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String when double.TryParse(el.GetString(), out var d) => d,
                _ => 0
            };
        }

        private static string NormalizeToJson(string jsonOrPath)
        {
            var s = jsonOrPath.AsSpan().TrimStart();
            bool looksLikeJson = s.Length > 0 && (s[0] == '{' || s[0] == '[');
            if (!looksLikeJson && File.Exists(jsonOrPath))
                return File.ReadAllText(jsonOrPath);
            return jsonOrPath;
        }
    }
}
