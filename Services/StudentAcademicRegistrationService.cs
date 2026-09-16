using System;
using System.Collections.Generic;
using System.Linq;
using TrainerScheduler.Data.Entities;

namespace TrainerScheduler.Services
{
    public sealed record StudentAcademicPlanInput(int CourseId, int Priority, bool IsRepeat, string? TermKey);

    public sealed class StudentRegistrationValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public int RegisteredCourseCount { get; internal set; }
        public int TotalWeeklyHours { get; internal set; }
        public int DistinctTermCount { get; internal set; }
        public bool IsValid => Errors.Count == 0;
    }

    public static class StudentAcademicRegistrationService
    {
        private static readonly string[] GeneralStudiesAliases =
        {
            "GENERAL STUDIES",
            "GENERAL EDUCATION",
            "FOUNDATION STUDIES",
            "UNIVERSITY REQUIREMENTS",
            "\u0627\u0644\u062F\u0631\u0627\u0633\u0627\u062A \u0627\u0644\u0639\u0627\u0645\u0629",
            "\u062F\u0631\u0627\u0633\u0627\u062A \u0639\u0627\u0645\u0629",
            "\u0639\u0627\u0645",
            "\u0627\u0644\u0639\u0627\u0645\u0629",
            "\u0639\u0627\u0645\u0647"
        };

        public static bool IsGeneralStudiesDepartmentName(string? name)
        {
            var normalized = Normalize(name);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return GeneralStudiesAliases.Any(alias => normalized.Contains(Normalize(alias), StringComparison.Ordinal));
        }

        public static bool IsCourseWithinLevelCeiling(int? studentLevel, CourseEntity? course)
        {
            if (course is null)
                return false;

            if (studentLevel is null || studentLevel <= 0)
                return true;

            if (course.Level <= 0)
                return true;

            return course.Level <= studentLevel.Value;
        }

        public static bool IsCourseAllowedForStudent(StudentEntity? student, CourseEntity? course, IEnumerable<DepartmentEntity> departments)
        {
            if (student is null || course is null)
                return false;

            if (student.DepartmentId is null || student.DepartmentId <= 0)
                return false;

            if (course.DepartmentId == student.DepartmentId.Value)
                return true;

            if (course.IsGeneralCourse)
                return true;

            var dept = departments?.FirstOrDefault(d => d.Id == course.DepartmentId);
            if (dept is not null && IsGeneralStudiesDepartmentName(dept.Name))
                return true;

            return false;
        }

        public static StudentRegistrationValidationResult ValidatePlan(
            StudentEntity? student,
            IEnumerable<StudentAcademicPlanInput> planRows,
            IEnumerable<CourseEntity> courses,
            IEnumerable<DepartmentEntity> departments)
        {
            var result = new StudentRegistrationValidationResult();
            var rows = planRows?.Where(x => x is not null).ToList() ?? new List<StudentAcademicPlanInput>();
            var courseById = courses?.Where(c => c is not null).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Last())
                             ?? new Dictionary<int, CourseEntity>();
            var departmentById = departments?.Where(d => d is not null).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.Last())
                                ?? new Dictionary<int, DepartmentEntity>();

            if (student is null)
            {
                result.Errors.Add("Select a student before validating academic registration.");
                return result;
            }

            var fullName = (student.FullName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                result.Errors.Add("The student profile is missing the student name.");

            if (student.DepartmentId is null || student.DepartmentId <= 0 || !departmentById.ContainsKey(student.DepartmentId.Value))
                result.Errors.Add("The student profile is missing a valid department.");

            if (student.Level is not null && student.Level <= 0)
                result.Errors.Add("The academic level must be 1 or higher.");

            result.RegisteredCourseCount = rows.Count(r => r.CourseId > 0);
            result.DistinctTermCount = rows.Where(r => !string.IsNullOrWhiteSpace(r.TermKey))
                                           .Select(r => r.TermKey!.Trim())
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .Count();

            if (result.RegisteredCourseCount == 0)
                result.Warnings.Add("No courses are currently listed in the student's study plan.");

            var duplicateCourse = rows.Where(r => r.CourseId > 0)
                                      .GroupBy(r => r.CourseId)
                                      .FirstOrDefault(g => g.Count() > 1);
            if (duplicateCourse is not null)
            {
                var duplicateName = courseById.TryGetValue(duplicateCourse.Key, out var dupCourse) ? dupCourse.Name : $"#{duplicateCourse.Key}";
                result.Errors.Add($"The study plan contains the course '{duplicateName}' more than once.");
            }

            if (result.DistinctTermCount > 1)
                result.Warnings.Add("The study plan contains more than one term key. Review the imported rows before saving.");

            foreach (var row in rows)
            {
                if (row.Priority < 0)
                    result.Errors.Add("Priority cannot be negative.");

                if (row.CourseId <= 0)
                {
                    result.Errors.Add("A study-plan row is missing a valid course.");
                    continue;
                }

                if (!courseById.TryGetValue(row.CourseId, out var course))
                {
                    result.Errors.Add($"Course ID {row.CourseId} does not exist in the current course list.");
                    continue;
                }

                result.TotalWeeklyHours += Math.Max(0, course.HoursPerWeek);

                if (!IsCourseAllowedForStudent(student, course, departmentById.Values))
                {
                    var studentDeptName = student.DepartmentId is int sid && departmentById.TryGetValue(sid, out var sdept)
                        ? sdept.Name
                        : "the selected department";
                    result.Errors.Add($"Course '{course.Name}' is not offered to the student's department ({studentDeptName}).");
                }

                if (!IsCourseWithinLevelCeiling(student.Level, course))
                    result.Errors.Add($"Course '{course.Name}' is above the student's current academic level.");
            }

            return result;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Replace('\u00A0', ' ').Trim().ToUpperInvariant();
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            return normalized;
        }
    }
}
