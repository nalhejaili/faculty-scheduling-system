using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncFdoRowsFromStore()
        {
            FdoRows.Clear();
            foreach (var o in Store.FacultyDeptOverrides)
            {
                var f = Store.Faculties.FirstOrDefault(x => x.Id == o.FacultyId);
                var d = Store.Departments.FirstOrDefault(x => x.Id == o.DepartmentId);
                FdoRows.Add(new FacultyDeptOverrideRow
                {
                    FacultyId = o.FacultyId,
                    FacultyName = f?.Name ?? o.FacultyId.ToString(),
                    DepartmentId = o.DepartmentId,
                    DepartmentName = d?.Name ?? o.DepartmentId.ToString()
                });
            }
        }
    }
}
