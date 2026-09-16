using System;


namespace MiniTrainerScheduler.Models
{
    public static class ArabicText
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
    }
}
