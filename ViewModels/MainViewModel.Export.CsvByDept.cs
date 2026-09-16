using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Windows;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void ExportCsvByDept()
        {
            var sfd = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "schedule_export.csv" };
            if (sfd.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(sfd.FileName)!;
                var baseName = Path.GetFileNameWithoutExtension(sfd.FileName);

                foreach (var dept in Store.Departments)
                {
                    var tmp = new DataStore();
                    tmp.Departments.AddRange(Store.Departments);
                    tmp.Faculties.AddRange(Store.Faculties);
                    tmp.Courses.AddRange(Store.Courses);
                    tmp.Rooms.AddRange(Store.Rooms);
                    tmp.Slots.AddRange(Store.Slots);
                    tmp.Assignments.AddRange(Store.Assignments.Where(a => a.DepartmentId == dept.Id));
                    if (tmp.Assignments.Count == 0) continue;

                    var safe = string.Join("_",
                        dept.Name.Split(Path.GetInvalidFileNameChars(), System.StringSplitOptions.RemoveEmptyEntries));

                    var path = Path.Combine(dir, $"{baseName}_{safe}.csv");
                    CsvService.ExportAssignmentsCsv(path, tmp);
                }

                MessageBox.Show("A CSV file has been created for each department.", "Completed");
            }
        }
    }
}
