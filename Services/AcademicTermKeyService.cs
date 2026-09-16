using System;
using System.Text;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// Optional academic-term helper.
    ///
    /// This service intentionally keeps the term flexible so users can leave it blank,
    /// or enter their preferred label such as:
    /// - Fall 2025
    /// - Spring 2026
    /// - First Term 2025/2026
    /// </summary>
    public static class AcademicTermKeyService
    {
        public static string? NormalizeForDisplay(string? raw)
            => NormalizeCore(raw);

        public static string? NormalizeForStorage(string? raw)
            => NormalizeCore(raw);

        public static string CreateSuggestedGregorianTermKey(DateTime? now = null)
            => string.Empty;

        private static string? NormalizeCore(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var cleaned = NormalizeDigits(raw).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        private static string NormalizeDigits(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                sb.Append(ch switch
                {
                    '\u0660' => '0',
                    '\u0661' => '1',
                    '\u0662' => '2',
                    '\u0663' => '3',
                    '\u0664' => '4',
                    '\u0665' => '5',
                    '\u0666' => '6',
                    '\u0667' => '7',
                    '\u0668' => '8',
                    '\u0669' => '9',
                    _ => ch
                });
            }
            return sb.ToString();
        }
    }
}

namespace MiniTrainerScheduler.Services
{
    public static class AcademicTermKeyService
    {
        public static string? NormalizeForDisplay(string? raw) => TrainerScheduler.Services.AcademicTermKeyService.NormalizeForDisplay(raw);
        public static string? NormalizeForStorage(string? raw) => TrainerScheduler.Services.AcademicTermKeyService.NormalizeForStorage(raw);
        public static string CreateSuggestedGregorianTermKey(System.DateTime? now = null) => TrainerScheduler.Services.AcademicTermKeyService.CreateSuggestedGregorianTermKey(now);
    }
}
