using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
                        void RefreshDeptFaculties()
        {
            FacultiesInSelectedDepartment.Clear();
            if (SelectedDepartment is null) return;

            foreach (var f in Store.Faculties
                         .Where(f => f.DepartmentId == SelectedDepartment.Id || f.IsGeneralStudies)
                         .OrderBy(f => f.Name))
                FacultiesInSelectedDepartment.Add(f);
        }
    }
}
