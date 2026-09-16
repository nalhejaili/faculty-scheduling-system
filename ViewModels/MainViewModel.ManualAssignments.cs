using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public void InitManualAssignmentListener()
        {
            ManualPlanBridge.ManualAssignmentRegistered += OnManualAssignmentRegistered;
        }
    }
}
