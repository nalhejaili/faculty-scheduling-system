using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RefreshLevels()
        {
            var prev = SelectedLevel;

            Levels.Clear();
            if (SelectedDepartment is null) { SelectedLevel = null; return; }

            foreach (var lv in Store.LevelsForDepartment(SelectedDepartment.Id))
                Levels.Add(lv);

            if (Levels.Count == 0)
            {
                SelectedLevel = null;
            }
            else if (prev.HasValue && Levels.Contains(prev.Value))
            {
                SelectedLevel = prev.Value;
            }
            else
            {
                SelectedLevel = Levels.First();
            }
        }
    }
}
