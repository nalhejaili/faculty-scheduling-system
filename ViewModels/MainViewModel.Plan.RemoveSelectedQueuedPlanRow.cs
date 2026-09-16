namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RemoveSelectedQueuedPlanRow()
        {
            if (SelectedQueuedPlanRow is null) return;
            QueuedPlan.Remove(SelectedQueuedPlanRow);
            SelectedQueuedPlanRow = null;
            RaiseAllCanExec();
        }
    }
}
