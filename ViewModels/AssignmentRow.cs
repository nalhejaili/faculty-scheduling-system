using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// </summary>
    public sealed class AssignmentRow
    {
        public string Department { get; set; } = "";
        public string Course { get; set; } = "";
        public int Section { get; set; }
        public string Slot { get; set; } = "";
        public int Hours { get; set; }
        public string Faculty { get; set; } = "";
        public string Room { get; set; } = "";
        public Assignment? Source { get; set; }

        /// <summary>
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// </summary>
        public string Kind { get; set; } = "";
    }
}
