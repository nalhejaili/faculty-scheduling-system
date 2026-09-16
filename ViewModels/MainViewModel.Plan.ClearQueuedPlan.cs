namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void ClearQueuedPlan()
        {
            QueuedPlan.Clear();
            RaiseAllCanExec();
        }
    }
}
