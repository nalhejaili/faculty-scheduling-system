using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RefreshManualFaculties()
        {
            ManualDeptFaculties.Clear();

            foreach (var f in Store.Faculties.OrderBy(f => f.Name))
                ManualDeptFaculties.Add(f);

            ManualSelectedFaculty = ManualDeptFaculties.FirstOrDefault();
            RaiseAllCanExec();
        }
    }
}
