using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        bool CanGenerate()
            => Store is not null
               && !IsBusy
               && QueuedPlan.Any(q => q.SectionsRequested > 0);
    }
}
