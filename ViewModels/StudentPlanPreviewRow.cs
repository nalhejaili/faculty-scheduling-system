using System;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// Read-only projection for showing a student's planned courses (next term) in the Admin window.
    /// </summary>
    public sealed class StudentPlanPreviewRow
    {
        public long Id { get; set; }
        public int CourseId { get; set; }
        public string CourseName { get; set; } = string.Empty;

        public int Priority { get; set; }
        public bool IsRepeat { get; set; }
        public string? TermKey { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}
