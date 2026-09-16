namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RemoveFdo()
        {
            if (SelectedFdoRow is null) return;
            Store.FacultyDeptOverrides.RemoveAll(o => o.FacultyId == SelectedFdoRow.FacultyId && o.DepartmentId == SelectedFdoRow.DepartmentId);
            FdoRows.Remove(SelectedFdoRow);
            SelectedFdoRow = null;
        }
    }
}
