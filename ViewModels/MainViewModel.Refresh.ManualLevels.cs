using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RefreshManualLevels()
        {
            var prev = ManualSelectedLevel;

            ManualLevels.Clear();
            if (ManualSelectedDepartment is null) { RaiseAllCanExec(); return; }

            foreach (var lv in Store.LevelsForDepartment(ManualSelectedDepartment.Id))
                ManualLevels.Add(lv);

            if (ManualLevels.Count == 0)
                ManualSelectedLevel = null;
            else if (prev.HasValue && ManualLevels.Contains(prev.Value))
                ManualSelectedLevel = prev.Value;
            else
                ManualSelectedLevel = ManualLevels.First();

            RaiseAllCanExec();
        }
    }
}
