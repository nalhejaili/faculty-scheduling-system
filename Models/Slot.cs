
using System;
namespace MiniTrainerScheduler.Models
{
    public sealed record Slot(int Id, DayOfWeek Day, TimeOnly Start, TimeOnly End)
    {
        public string DisplayName => ToString();

    public override string ToString() => ArabicText.Slot(this);
    }
}
