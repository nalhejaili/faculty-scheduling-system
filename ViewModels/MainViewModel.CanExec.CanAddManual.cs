namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        bool CanAddManual()
            => ManualSelectedDepartment is not null
               && ManualSelectedLevel is not null
               && ManualSelectedCourse is not null
               && ManualSections > 0;
    }
}
