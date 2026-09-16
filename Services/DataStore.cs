
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;
using System.Collections.ObjectModel;

using System.Collections.Generic;


namespace MiniTrainerScheduler.Services
{
    public sealed class DataStore
    {
        public List<FacultySlotBlock> FacultySlotBlocks { get; set; } = new();

        public List<Department> Departments { get; } = new();
        public List<Faculty> Faculties { get; } = new();
        public List<Course> Courses { get; } = new();
        public List<Room> Rooms { get; } = new();
        public List<Slot> Slots { get; } = new();
        public List<Assignment> Assignments { get; } = new();

        // -------------------------
        // -------------------------
        public List<Student> Students { get; } = new();
        public List<StudentPlan> StudentPlans { get; } = new();
        public List<StudentEnrollment> StudentEnrollments { get; } = new();
        public ObservableCollection<IntakeDemand> Demands { get; } = new();
        public List<CourseHourSplitOverride> CourseHourSplits { get; } = new();
        public HashSet<int> PracticalCourseIds { get; } = new();

        public Dictionary<int, int> CourseCapOverrides { get; } = new();


        public ObservableCollection<PlanningIssue> PlanningIssues { get; } = new();

        public List<FacultyDeptOverride> FacultyDeptOverrides { get; } = new();
        public List<CourseFacultyOverride> CourseFacultyOverrides { get; } = new();

        
        public List<CourseCodeFacultyOverride> CourseCodeFacultyOverrides { get; } = new();
public List<CourseSlotOverride> CourseSlotOverrides { get; } = new();
        public List<FacultyLoadOverride> FacultyLoadOverrides { get; } = new();
        public bool AllowOverloadFallbackThisRun { get; set; } = false;

        public void ClearAll()
        {
            Departments.Clear(); Faculties.Clear(); Courses.Clear();
            Rooms.Clear(); Slots.Clear(); Assignments.Clear();
            Students.Clear(); StudentPlans.Clear(); StudentEnrollments.Clear();
        }
        /// <summary>
        /// </summary>
        public void ClearCoreLookups()
        {
            Departments.Clear();
            Faculties.Clear();
            Courses.Clear();
            Rooms.Clear();
            Slots.Clear();
            Assignments.Clear();

            FacultyDeptOverrides.Clear();
            CourseFacultyOverrides.Clear();
                        CourseCodeFacultyOverrides.Clear();
CourseSlotOverrides.Clear();
            FacultyLoadOverrides.Clear();
            FacultySlotBlocks.Clear();

            Demands.Clear();
            CourseHourSplits.Clear();
            PracticalCourseIds.Clear();
            CourseCapOverrides.Clear();
            PlanningIssues.Clear();
        }


        public IEnumerable<int> LevelsForDepartment(int deptId) =>
            Courses.Where(c => c.DepartmentId == deptId).Select(c => c.Level).Distinct().OrderBy(x => x);
    }
}
