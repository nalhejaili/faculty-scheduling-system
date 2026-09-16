using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class JsonImportService
    {
        public static Root ImportFile(DataStore store, string path)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("The JSON file was not found.", path);

            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var root = JsonSerializer.Deserialize<Root>(json, opts)
                       ?? throw new InvalidDataException("The JSON file is invalid.");

	            HydrateCourseCodesFromJson(root, json);

            store.ClearCoreLookups();

            if (root.Departments != null) store.Departments.AddRange(root.Departments);
            if (root.Faculties != null) store.Faculties.AddRange(root.Faculties);
	            if (root.Courses != null) store.Courses.AddRange(root.Courses);
            if (root.Slots != null) store.Slots.AddRange(root.Slots);

            ImportPracticalFlags(store, json, clearExisting: true);

            JsonImport.ImportCourseHourSplitsFromJson(store, json, clearExisting: true, preferCourses: true);

            if (root.Rooms != null && root.Rooms.Count > 0)
            {
                var mappedRooms = root.Rooms.Select(r => new Room
                {
                    Id = r.Id,
                    Name = r.Name ?? string.Empty,
                    DepartmentId = r.DepartmentId
                });
                store.Rooms.AddRange(mappedRooms);
            }

            store.FacultyDeptOverrides.Clear();
            store.CourseFacultyOverrides.Clear();
            store.CourseCodeFacultyOverrides.Clear();
            store.CourseSlotOverrides.Clear();
            store.FacultyLoadOverrides.Clear();

            if (root.FacultyDeptOverrides != null) store.FacultyDeptOverrides.AddRange(root.FacultyDeptOverrides);
            if (root.CourseFacultyOverrides != null) store.CourseFacultyOverrides.AddRange(root.CourseFacultyOverrides);
            if (root.CourseCodeFacultyOverrides != null) store.CourseCodeFacultyOverrides.AddRange(root.CourseCodeFacultyOverrides);
            if (root.CourseSlotOverrides != null) store.CourseSlotOverrides.AddRange(root.CourseSlotOverrides);
            if (root.FacultyLoadOverrides != null) store.FacultyLoadOverrides.AddRange(root.FacultyLoadOverrides);

            return root;
        }

	        /// <summary>
	        /// </summary>
	        private static void HydrateCourseCodesFromJson(Root root, string json)
	        {
	            try
	            {
	                if (root.Courses == null || root.Courses.Count == 0) return;
	                using var doc = JsonDocument.Parse(json);
	
	                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
	                    doc.RootElement.TryGetProperty("courses", out var coursesEl) &&
	                    coursesEl.ValueKind == JsonValueKind.Array)
	                {
	                    int i = 0;
	                    foreach (var el in coursesEl.EnumerateArray())
	                    {
	                        if (i >= root.Courses.Count) break;
	                        var c = root.Courses[i];
	                        if (c != null && string.IsNullOrWhiteSpace(c.CourseCode))
	                        {
	                            var code = TryGetString(el, "courseCode")
	                                       ?? TryGetString(el, "CourseCode")
	                                       ?? TryGetString(el, "code")
	                                       ?? TryGetString(el, "Code");
	                            if (!string.IsNullOrWhiteSpace(code)) c.CourseCode = code.Trim();
	                        }
	                        i++;
	                    }
	                }
	            }
	            catch
	            {
	                // best-effort
	            }
	
	            static string? TryGetString(JsonElement obj, string prop)
	            {
	                if (obj.ValueKind != JsonValueKind.Object) return null;
	                if (!obj.TryGetProperty(prop, out var v)) return null;
	                if (v.ValueKind == JsonValueKind.String) return v.GetString();
	                // allow numbers if someone puts codes like 10101
	                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
	                return null;
	            }
	        }
    }

    public sealed class Root
    {
        public List<Department> Departments { get; set; } = new();
        public List<Faculty> Faculties { get; set; } = new();
        public List<Course> Courses { get; set; } = new();
        public List<Slot> Slots { get; set; } = new();
        public List<Room> Rooms { get; set; } = new();

        public List<FacultyDeptOverride> FacultyDeptOverrides { get; set; } = new();
        public List<CourseFacultyOverride> CourseFacultyOverrides { get; set; } = new();
        public List<CourseCodeFacultyOverride> CourseCodeFacultyOverrides { get; set; } = new();
        public List<CourseSlotOverride> CourseSlotOverrides { get; set; } = new();
        public List<FacultyLoadOverride> FacultyLoadOverrides { get; set; } = new();
    }
}
