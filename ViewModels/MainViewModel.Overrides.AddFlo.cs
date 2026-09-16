using MiniTrainerScheduler.Models;
using System.Linq;
using System.Windows;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void AddFlo()
        {
            if (SelectedOverrideFaculty is null || SelectedFloHours is null || SelectedFloHours <= 0)
            { MessageBox.Show("Select the faculty member and enter a valid hour limit (>0)."); return; }

            var fId = SelectedOverrideFaculty.Id;
            var hrs = SelectedFloHours.Value;

            Store.FacultyLoadOverrides.RemoveAll(x => x.FacultyId == fId);
            Store.FacultyLoadOverrides.Add(new FacultyLoadOverride(fId, hrs));

            var row = FloRows.FirstOrDefault(x => x.FacultyId == fId);
            if (row is null)
                FloRows.Add(new FacultyLoadOverrideRow { FacultyId = fId, FacultyName = SelectedOverrideFaculty.Name, MaxHours = hrs });
            else
                row.MaxHours = hrs;
        }
    }
}
