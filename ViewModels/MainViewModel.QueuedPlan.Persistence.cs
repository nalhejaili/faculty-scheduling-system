using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        private void SaveQueuedPlanJson()
        {
            try
            {
                if (QueuedPlan.Count == 0)
                {
                    MessageBox.Show("There are no items in the selected plan to save.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sfd = new SaveFileDialog
                {
                    Filter = "JSON (*.json)|*.json",
                    Title = "Save Selected Plan (QueuedPlan)",
                    FileName = "queued_plan.json"
                };

                if (sfd.ShowDialog() != true) return;

                QueuedPlanFileService.Save(sfd.FileName, QueuedPlan);
                MessageBox.Show("The selected plan has been saved successfully.", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save the selected plan.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadQueuedPlanJson()
        {
            try
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "JSON (*.json)|*.json",
                    Title = "Load Selected Plan (QueuedPlan)"
                };
                if (ofd.ShowDialog() != true) return;

                var rows = QueuedPlanFileService.Load(ofd.FileName);
                if (rows.Count == 0)
                {
                    MessageBox.Show("The file does not contain any items.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                QueuedPlan.Clear();
                foreach (var r in rows)
                    QueuedPlan.Add(r);

                RaiseAllCanExec();
                MessageBox.Show("The selected plan has been loaded.", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load the selected plan.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
