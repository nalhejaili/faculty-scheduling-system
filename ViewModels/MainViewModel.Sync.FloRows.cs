using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncFloRowsFromStore()
        {
            FloRows.Clear();
            foreach (var o in Store.FacultyLoadOverrides.OrderBy(x => x.FacultyId))
            {
                var f = Store.Faculties.FirstOrDefault(x => x.Id == o.FacultyId);
                FloRows.Add(new FacultyLoadOverrideRow
                {
                    FacultyId = o.FacultyId,
                    FacultyName = f?.Name ?? o.FacultyId.ToString(),
                    MaxHours = o.MaxHours
                });
            }
        }
    }
}
