namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RemoveFlo()
        {
            if (SelectedFloRow is null) return;
            Store.FacultyLoadOverrides.RemoveAll(x => x.FacultyId == SelectedFloRow.FacultyId);
            FloRows.Remove(SelectedFloRow);
            SelectedFloRow = null;
        }
    }
}
