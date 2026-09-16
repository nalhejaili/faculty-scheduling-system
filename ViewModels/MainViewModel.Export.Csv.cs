using Microsoft.Win32;
using System.Windows;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void ExportCsv()
        {
            var sfd = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "schedule_export.csv" };
            if (sfd.ShowDialog() == true)
            {
                CsvService.ExportAssignmentsCsv(sfd.FileName, Store);
                MessageBox.Show("The export has been completed.", "Completed");
            }
        }
    }
}
