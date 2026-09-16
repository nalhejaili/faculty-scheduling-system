using System;

namespace MiniTrainerScheduler.Models
{
    public enum SectionType { Normal, Remedial }

    public sealed class SectionPlanEntry
    {
        public string Term { get; set; } = "";
        public string Program { get; set; } = "";
        public int Level { get; set; }
        public int CourseId { get; set; }
        public int SectionIndex { get; set; }   // 1..N
        public int Size { get; set; }
        public SectionType Type { get; set; } = SectionType.Normal;

        public override string ToString() => $"{Term}/{Program}/L{Level}/C{CourseId} S{SectionIndex} -> {Size}";
    }
}
