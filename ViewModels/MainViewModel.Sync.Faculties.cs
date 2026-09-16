namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncFaculties()
        {
            Faculties.Clear();
            foreach (var f in Store.Faculties) Faculties.Add(f);
            OnPropertyChanged(nameof(Faculties));
        }
    }
}
