using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MiniTrainerScheduler.ViewModels;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// </summary>
    public static class QueuedPlanFileService
    {
        public sealed class Draft
        {
            public int Version { get; set; } = 2;
            public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
            public List<Item> Rows { get; set; } = new();
        }

        public sealed class Item
        {
            public int DepartmentId { get; set; }
            public string DepartmentName { get; set; } = "";
            public int Level { get; set; }

            public int CourseId { get; set; }
            public string CourseName { get; set; } = "";

            public int SectionIndex { get; set; } = 1;
            public int SectionsRequested { get; set; } = 1;

            public int StudentsThisTerm { get; set; }
            public int Repeaters { get; set; }
            public bool IsPractical { get; set; }

            public int? PreferredFacultyId { get; set; }
            public int? PreferredSlotId { get; set; }

            public int? PreferredRoomId { get; set; }

            public double TheoryHoursRequired { get; set; }
            public double PracticalHoursRequired { get; set; }

            public List<int> TheorySlotIds { get; set; } = new();
            public List<int> PracticalSlotIds { get; set; } = new();
        }

        static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static void Save(string path, IEnumerable<QueuedPlanRow> rows)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required", nameof(path));

            var draft = new Draft
            {
                SavedUtc = DateTime.UtcNow,
                Rows = rows?.Select(r => new Item
                {
                    DepartmentId = r.DepartmentId,
                    DepartmentName = r.DepartmentName ?? string.Empty,
                    Level = r.Level,
                    CourseId = r.CourseId,
                    CourseName = r.CourseName ?? string.Empty,
                    SectionIndex = r.SectionIndex,
                    SectionsRequested = r.SectionsRequested,
                    StudentsThisTerm = r.StudentsThisTerm,
                    Repeaters = r.Repeaters,
                    IsPractical = r.IsPractical,
                    PreferredFacultyId = r.PreferredFacultyId,
                    PreferredSlotId = r.PreferredSlotId,
                    PreferredRoomId = r.PreferredRoomId,
                    TheoryHoursRequired = r.TheoryHoursRequired,
                    PracticalHoursRequired = r.PracticalHoursRequired,
                    TheorySlotIds = (r.TheorySlotIds ?? new List<int>()).ToList(),
                    PracticalSlotIds = (r.PracticalSlotIds ?? new List<int>()).ToList(),
                }).ToList() ?? new List<Item>()
            };

            var json = JsonSerializer.Serialize(draft, _jsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public static List<QueuedPlanRow> Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required", nameof(path));
            if (!File.Exists(path)) return new List<QueuedPlanRow>();

            var json = File.ReadAllText(path, Encoding.UTF8);
            var draft = JsonSerializer.Deserialize<Draft>(json, _jsonOptions);

            var rows = new List<QueuedPlanRow>();
            if (draft?.Rows == null) return rows;

            foreach (var it in draft.Rows)
            {
                rows.Add(new QueuedPlanRow
                {
                    DepartmentId = it.DepartmentId,
                    DepartmentName = it.DepartmentName ?? string.Empty,
                    Level = it.Level,
                    CourseId = it.CourseId,
                    CourseName = it.CourseName ?? string.Empty,
                    SectionIndex = it.SectionIndex <= 0 ? 1 : it.SectionIndex,
                    SectionsRequested = it.SectionsRequested < 0 ? 0 : it.SectionsRequested,
                    StudentsThisTerm = it.StudentsThisTerm < 0 ? 0 : it.StudentsThisTerm,
                    Repeaters = it.Repeaters < 0 ? 0 : it.Repeaters,
                    IsPractical = it.IsPractical,
                    PreferredFacultyId = it.PreferredFacultyId,
                    PreferredSlotId = it.PreferredSlotId,
                    PreferredRoomId = it.PreferredRoomId,
                    TheoryHoursRequired = it.TheoryHoursRequired < 0 ? 0 : it.TheoryHoursRequired,
                    PracticalHoursRequired = it.PracticalHoursRequired < 0 ? 0 : it.PracticalHoursRequired,
                    TheorySlotIds = it.TheorySlotIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>(),
                    PracticalSlotIds = it.PracticalSlotIds?.Where(x => x > 0).Distinct().ToList() ?? new List<int>(),
                });
            }

            return rows;
        }
    }
}
