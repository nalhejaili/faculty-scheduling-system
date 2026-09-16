using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void PlanRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var it in e.NewItems.OfType<CoursePlanRow>())
                    it.PropertyChanged += PlanRow_PropertyChanged;

            if (e.OldItems != null)
                foreach (var it in e.OldItems.OfType<CoursePlanRow>())
                    it.PropertyChanged -= PlanRow_PropertyChanged;

            RaiseAllCanExec();
        }
    }
}
