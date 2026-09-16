using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static class CsvMasterImportService
    {
        public static void ImportFolder(DataStore store, string folder)
        {
            store.Departments.Clear();
            store.Faculties.Clear();
            store.Courses.Clear();
            store.Rooms.Clear();
            store.Slots.Clear();
            store.Assignments.Clear();

            ImportDepartments(store, Path.Combine(folder, "departments.csv"));
            ImportFaculties(store, Path.Combine(folder, "faculties.csv"));
            ImportCourses(store, Path.Combine(folder, "courses.csv"));
            ImportRooms(store, Path.Combine(folder, "rooms.csv"));
            ImportSlots(store, Path.Combine(folder, "slots.csv"));

            var fdo = System.IO.Path.Combine(folder, "faculty_dept_overrides.csv");
            if (File.Exists(fdo)) ImportFacultyDeptOverrides(store, fdo);

            var cfo = System.IO.Path.Combine(folder, "course_faculty_overrides.csv");
            if (File.Exists(cfo)) ImportCourseFacultyOverrides(store, cfo);

        }
        static void ImportFacultyDeptOverrides(DataStore store, string path)
        {
            store.FacultyDeptOverrides.Clear();
            foreach (var r in Read(path))
            {
                var fid = ToInt(r, "FacultyId");
                var did = ToInt(r, "DepartmentId");
                store.FacultyDeptOverrides.Add(new FacultyDeptOverride(fid, did));
            }
        }

        static void ImportCourseFacultyOverrides(DataStore store, string path)
        {
            store.CourseFacultyOverrides.Clear();
            foreach (var r in Read(path))
            {
                var cid = ToInt(r, "CourseId");
                var fid = ToInt(r, "FacultyId");
                store.CourseFacultyOverrides.Add(new CourseFacultyOverride(cid, fid));
            }
        }

        static void ImportDepartments(DataStore store, string path)
        {
            foreach (var r in Read(path))
            {
                var id = ToInt(r, "Id");
                var name = Get(r, "Name");
                store.Departments.Add(new Department(id, name));
            }
        }

        static void ImportFaculties(DataStore store, string path)
        {
            foreach (var r in Read(path))
            {
                var id = ToInt(r, "Id");
                var name = Get(r, "Name");
                var dep = ToInt(r, "DepartmentId");
                store.Faculties.Add(new Faculty(id, name, dep));
            }
        }

        static void ImportCourses(DataStore store, string path)
        {
            foreach (var r in Read(path))
            {
                var id = ToInt(r, "Id");
                var name = Get(r, "Name");
                var dep = ToInt(r, "DepartmentId");
                var lvl = ToInt(r, "Level");
                var code = TryGetAny(r, "CourseCode", "Code");

                var isGeneral = TryBool(r, "IsGeneralCourse") ?? false;
                var hours = TryInt(r, "HoursPerWeek") ?? 0;

                var c = new Course(id, name, dep, lvl, IsGeneralCourse: isGeneral, HoursPerWeek: hours)
                {
                    CourseCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim()
                };
                store.Courses.Add(c);
            }
        }

        private static string? TryGetAny(Dictionary<string, string> row, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (row.TryGetValue(k, out var v))
                    return v;
            }
            return null;
        }

        private static bool? TryBool(Dictionary<string, string> row, string key)
        {
            if (!row.TryGetValue(key, out var v)) return null;
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim();
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        static void ImportRooms(DataStore store, string path)
        {
            foreach (var r in Read(path))
            {
                var id = ToInt(r, "Id");
                var name = Get(r, "Name");
                var dep = TryInt(r, "DepartmentId");
                store.Rooms.Add(new Room(id, name, dep));
            }
        }

        static void ImportSlots(DataStore store, string path)
        {
            foreach (var r in Read(path))
            {
                var id = ToInt(r, "Id");
                var day = ParseDay(Get(r, "Day")); // Sunday..Saturday
                var start = TimeOnly.ParseExact(Get(r, "Start"), "HH:mm", CultureInfo.InvariantCulture);
                var end = TimeOnly.ParseExact(Get(r, "End"), "HH:mm", CultureInfo.InvariantCulture);
                store.Slots.Add(new Slot(id, day, start, end));
            }
        }

        // ===== Helpers =====
        static List<Dictionary<string, string>> Read(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return new();

            var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
            var list = new List<Dictionary<string, string>>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < header.Length && i < parts.Length; i++)
                    d[header[i]] = parts[i].Trim();
                list.Add(d);
            }
            return list;
        }

        static string Get(Dictionary<string, string> r, string key)
            => r.TryGetValue(key, out var v) ? v : throw new InvalidDataException($"Missing column: {key}");

        static int ToInt(Dictionary<string, string> r, string key)
            => int.Parse(Get(r, key), CultureInfo.InvariantCulture);

        static int? TryInt(Dictionary<string, string> r, string key)
            => r.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

        static DayOfWeek ParseDay(string s)
        {
            if (Enum.TryParse<DayOfWeek>(s, ignoreCase: true, out var d)) return d;
            throw new InvalidDataException($"Invalid Day value: {s}");
        }
    }
}
