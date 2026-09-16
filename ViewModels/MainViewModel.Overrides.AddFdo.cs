using MiniTrainerScheduler.Models;
using System.Linq;
using System.Windows;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void AddFdo()
        {
            if (SelectedOverrideFaculty is null || SelectedOverrideDepartment is null) { MessageBox.Show("Select the faculty member and department."); return; }
            var fId = SelectedOverrideFaculty.Id;
            var dId = SelectedOverrideDepartment.Id;
            if (Store.FacultyDeptOverrides.Any(o => o.FacultyId == fId && o.DepartmentId == dId)) { MessageBox.Show("This assignment already exists."); return; }

            Store.FacultyDeptOverrides.Add(new FacultyDeptOverride(fId, dId));
            FdoRows.Add(new FacultyDeptOverrideRow { FacultyId = fId, FacultyName = SelectedOverrideFaculty.Name, DepartmentId = dId, DepartmentName = SelectedOverrideDepartment.Name });
        }
    }
}
