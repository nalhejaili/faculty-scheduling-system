using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class JsonImportService
    {
        public static void ImportPracticalFlags(DataStore store, string jsonOrPath, bool clearExisting = true)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(jsonOrPath)) return;

            string text = NormalizeToJson(jsonOrPath);

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (clearExisting) store.PracticalCourseIds.Clear();
            var seen = new HashSet<int>(store.PracticalCourseIds);

            if (root.TryGetProperty("practicalCourseIds", out var pracIdsEl) &&
                pracIdsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in pracIdsEl.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number)
                    {
                        int id = el.GetInt32();
                        if (seen.Add(id)) store.PracticalCourseIds.Add(id);
                    }
                }
            }

            if (root.TryGetProperty("courses", out var coursesEl) &&
                coursesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in coursesEl.EnumerateArray())
                {
                    if (!c.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                        continue;

                    int id = idEl.GetInt32();

                    bool isPracticalFlag =
                        (c.TryGetProperty("isPractical", out var ipEl) && ipEl.ValueKind == JsonValueKind.True) ||
                        (c.TryGetProperty("practical", out var pEl) && pEl.ValueKind == JsonValueKind.True);

                    bool hasPracticalHours =
                        c.TryGetProperty("practicalHours", out var phEl) &&
                        phEl.ValueKind == JsonValueKind.Number &&
                        phEl.GetDouble() > 0.0;

                    if (isPracticalFlag || hasPracticalHours)
                    {
                        if (seen.Add(id)) store.PracticalCourseIds.Add(id);
                    }
                }
            }
        }

        public static void ImportCourseHourSplits(DataStore store, string jsonOrPath, bool clearExisting = true, bool preferCourses = true)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(jsonOrPath)) return;

            string text = NormalizeToJson(jsonOrPath);
            JsonImport.ImportCourseHourSplitsFromJson(store, text, clearExisting, preferCourses);
        }

        private static string NormalizeToJson(string jsonOrPath)
        {
            var trimmed = jsonOrPath.AsSpan().TrimStart();
            bool looksLikeJson = trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');

            if (!looksLikeJson && File.Exists(jsonOrPath))
                return File.ReadAllText(jsonOrPath);

            return jsonOrPath;
        }
    }
}
