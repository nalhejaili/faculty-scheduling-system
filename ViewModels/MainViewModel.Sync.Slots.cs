namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncSlots()
        {
            Slots.Clear();
            foreach (var s in Store.Slots) Slots.Add(s);
            OnPropertyChanged(nameof(Slots));
        }
    }
}
