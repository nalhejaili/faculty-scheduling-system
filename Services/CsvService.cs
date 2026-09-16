using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static class CsvService
    {
        private static readonly SearchValues<char> CsvSpecials =
            SearchValues.Create(new[] { ',', '"', '\n', '\r' });

        public static void ExportAssignmentsCsv(string path, DataStore store)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Department,Course,Section,Slot,Faculty,Room,Status");

            string DeptName(int id) =>
                store.Departments.FirstOrDefault(d => d.Id == id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);

            string CourseName(int id) =>
                store.Courses.FirstOrDefault(c => c.Id == id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);

            string FacultyName(int id) =>
                store.Faculties.FirstOrDefault(f => f.Id == id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);

            string RoomName(int? id)
            {
                if (id is null) return string.Empty;
                var r = store.Rooms.FirstOrDefault(x => x.Id == id);
                return r?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);
            }

            string SlotLabel(int id) =>
                store.Slots.FirstOrDefault(s => s.Id == id)?.ToString() ?? id.ToString(CultureInfo.InvariantCulture);

            foreach (var a in store.Assignments)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Escape(DeptName(a.DepartmentId)),
                    Escape(CourseName(a.CourseId)),
                    a.SectionIndex.ToString(CultureInfo.InvariantCulture),
                    Escape(SlotLabel(a.SlotId)),
                    Escape(FacultyName(a.FacultyId)),
                    Escape(RoomName(a.RoomId)),
                    Escape(a.Status),
                }));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        static string Escape(string s)
        {
            if (s.AsSpan().ContainsAny(CsvSpecials))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
