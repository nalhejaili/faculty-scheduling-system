using MiniTrainerScheduler.Models;
using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void BuildDemandsFromUI()
        {
            Store.Demands.Clear();

            if (SelectedDepartment is not null && SelectedLevel is not null)
            {
                foreach (var r in PlanRows.Where(x => x.SectionIndex is null))
                {
                    int s = r.StudentsThisTerm < 0 ? 0 : r.StudentsThisTerm;
                    int rep = r.Repeaters < 0 ? 0 : r.Repeaters;
                    if (s + rep <= 0) continue;

                    Store.Demands.Add(new IntakeDemand(
                        departmentId: SelectedDepartment.Id,
                        level: SelectedLevel.Value,
                        courseId: r.CourseId,
                        demandCount: s,
                        repeatersCount: rep
                    ));
                }
            }

            foreach (var g in QueuedPlan.GroupBy(x => new { x.DepartmentId, x.Level, x.CourseId }))
            {
                var first = g.First();

                int s = g.Sum(x => x.StudentsThisTerm < 0 ? 0 : x.StudentsThisTerm);
                int rep = g.Sum(x => x.Repeaters < 0 ? 0 : x.Repeaters);
                if (s + rep <= 0) continue;

                Store.Demands.Add(new IntakeDemand(
                    departmentId: first.DepartmentId,
                    level: first.Level,
                    courseId: first.CourseId,
                    demandCount: s,
                    repeatersCount: rep
                ));
            }

        }
    }
}
