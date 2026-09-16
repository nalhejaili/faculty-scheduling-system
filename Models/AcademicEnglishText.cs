using System;

namespace MiniTrainerScheduler.Models
{
    public static class AcademicEnglishText
    {
        public static string DayName(DayOfWeek d) => d switch
        {
            DayOfWeek.Sunday => "Sunday",
            DayOfWeek.Monday => "Monday",
            DayOfWeek.Tuesday => "Tuesday",
            DayOfWeek.Wednesday => "Wednesday",
            DayOfWeek.Thursday => "Thursday",
            DayOfWeek.Friday => "Friday",
            DayOfWeek.Saturday => "Saturday",
            _ => d.ToString()
        };

        public static string Slot(Slot slot)
            => $"{DayName(slot.Day)} {slot.Start:HH:mm}-{slot.End:HH:mm}";

        public static string KindLabel(string? kind) => (kind ?? string.Empty).ToUpperInvariant() switch
        {
            "LAB" => "Laboratory",
            "PRACTICAL" => "Laboratory",
            "THEORY" => "Lecture",
            _ => kind ?? string.Empty
        };

        public static string KindSuffix(string? kind) => (kind ?? string.Empty).ToUpperInvariant() switch
        {
            "LAB" => " (Laboratory)",
            "PRACTICAL" => " (Laboratory)",
            "THEORY" => " (Lecture)",
            _ => string.Empty
        };

        public static string TranslateSlotLabel(string? slotLabel)
        {
            if (string.IsNullOrWhiteSpace(slotLabel)) return slotLabel ?? string.Empty;
            return slotLabel
                .Replace("\u0627\u0644\u0623\u062D\u062F", "Sunday")
                .Replace("\u0627\u0644\u0627\u062B\u0646\u064A\u0646", "Monday")
                .Replace("\u0627\u0644\u062B\u0644\u0627\u062B\u0627\u0621", "Tuesday")
                .Replace("\u0627\u0644\u0623\u0631\u0628\u0639\u0627\u0621", "Wednesday")
                .Replace("\u0627\u0644\u062E\u0645\u064A\u0633", "Thursday")
                .Replace("\u0627\u0644\u062C\u0645\u0639\u0629", "Friday")
                .Replace("\u0627\u0644\u0633\u0628\u062A", "Saturday");
        }

        public static string FacultyDutyLabel(string? reason)
            => string.IsNullOrWhiteSpace(reason) ? "Faculty Release / Official Duty" : reason!.Trim();
    }
}
