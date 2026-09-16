namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncCourses()
        {
            Courses.Clear();
            foreach (var c in Store.Courses) Courses.Add(c);
            OnPropertyChanged(nameof(Courses));
        }
    }
}
