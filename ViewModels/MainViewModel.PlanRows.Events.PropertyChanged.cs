using System;
using System.ComponentModel;
using System.Linq;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void PlanRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CoursePlanRow r)
            {
                if (e.PropertyName == nameof(CoursePlanRow.SectionsRequested) ||
                    e.PropertyName == nameof(CoursePlanRow.PreferredFacultyId) ||
                    e.PropertyName == nameof(CoursePlanRow.PreferredSlotId) ||
                    e.PropertyName == nameof(CoursePlanRow.SectionIndex))
                {
                    RaiseAllCanExec();
                }
                else if (e.PropertyName == nameof(CoursePlanRow.IsPractical))
                {
                    if (r.IsPractical)
                        Store.PracticalCourseIds.Add(r.CourseId);
                    else
                        Store.PracticalCourseIds.Remove(r.CourseId);

                    var c = Store.Courses.FirstOrDefault(x => x.Id == r.CourseId);
                    if (c != null)
                    {
                        var (th, lab, _) = SectionPlanner.GetHoursSplit(Store, c);
                        r.TheoryHoursRequired = th;
                        r.PracticalHoursRequired = lab;
                    }
                }
                else if (e.PropertyName == nameof(CoursePlanRow.StudentsThisTerm))
                {
                    if (r.SectionIndex is not null) return;
                    if (r.StudentsThisTerm is int students && students > 0)
                    {
                        int cap = r.IsPractical ? 15 : 35;

                        int n = (int)Math.Ceiling(students / (double)cap);
                        if (n < 1) n = 1;

                        r.SectionsRequested = n;
                    }
                    else
                    {
                    }
                }
            }
        }
    }
}
