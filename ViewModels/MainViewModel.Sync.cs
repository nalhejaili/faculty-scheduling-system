using System.Linq;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncDepartments()
        {
            Departments.Clear();

            // 4B-5c: Supervisor should only see their locked department (not just disabled).
            if (AuthContext.IsSupervisor)
            {
                var deptId = AuthContext.Current?.DepartmentId;
                if (deptId is int id && id > 0)
                {
                    var d = Store.Departments.FirstOrDefault(x => x.Id == id);
                    if (d is not null)
                        Departments.Add(d);
                }
            }
            else
            {
                foreach (var d in Store.Departments)
                    Departments.Add(d);
            }

            // Ensure selection is valid.
            if (Departments.Count > 0)
            {
                if (SelectedDepartment is null || !Departments.Any(d => d.Id == SelectedDepartment.Id))
                    SelectedDepartment = Departments[0];

                if (ManualSelectedDepartment is null || !Departments.Any(d => d.Id == ManualSelectedDepartment.Id))
                    ManualSelectedDepartment = Departments[0];
            }

            RefreshStudentWeeklyDepartmentGroups();
        }
    }
}
