using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RefreshRows()
        {
            Rows.Clear();

            int? lockedDeptId = IsSupervisor ? GetSupervisorDepartment()?.Id : null;

            foreach (var a in Store.Assignments)
            {
                if (lockedDeptId is int did && a.DepartmentId != did)
                    continue;

                var deptName = Store.Departments
                    .FirstOrDefault(x => x.Id == a.DepartmentId)?.Name ?? "—";

                var course = Store.Courses.FirstOrDefault(c => c.Id == a.CourseId);
                var courseName = course?.Name ?? "—";
                var courseHours = course?.HoursPerWeek ?? 0;

                var slotObj = Store.Slots.FirstOrDefault(x => x.Id == a.SlotId);
                var slotText = (slotObj is null || a.SlotId <= 0)
                    ? "—"
                    : AcademicEnglishText.Slot(slotObj);

                var faculty = Store.Faculties
                    .FirstOrDefault(x => x.Id == a.FacultyId)?.Name ?? "—";

                var roomText = a.RoomId is null
                    ? ""
                    : (Store.Rooms.FirstOrDefault(x => x.Id == a.RoomId)?.Name
                       ?? a.RoomId.Value.ToString());

                string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                string courseWithKind = string.IsNullOrWhiteSpace(kindSuffix)
                    ? courseName
                    : courseName + kindSuffix;
                // ===============================================

                Rows.Add(new AssignmentRow
                {
                    Source = a,

                    Department = deptName,
                    Course = courseWithKind,
                    Section = a.SectionIndex,
                    Slot = slotText,
                    Hours = (int)Math.Round(Convert.ToDouble(courseHours)),
                    Faculty = faculty,
                    Room = roomText,

                    Status = a.Status,   // OK / MANUAL / ...
                    Kind = a.Kind        // THEORY / LAB
                });


            }

            RefreshFacultyTotalsByCourseLoad();
            RaiseAllCanExec();
        }
    }
}
