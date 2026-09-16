namespace MiniTrainerScheduler.Services
{
    public static class TextRtlHelper
    {
        /// <summary>
        /// </summary>
        public static string ToRtl(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            // RLE ... PDF
            return "\u202B" + text + "\u202C";
        }
    }
}
