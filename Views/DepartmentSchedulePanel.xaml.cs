using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using MiniTrainerScheduler.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace MiniTrainerScheduler.Views
{
    public partial class DepartmentSchedulePanel : UserControl
    {
        public DepartmentSchedulePanel(DataStore store, int? scopeDepartmentId = null)
        {
            InitializeComponent();
            DataContext = new DepartmentScheduleViewModel(store, scopeDepartmentId);
        }

        private DepartmentScheduleViewModel? Vm
            => DataContext as DepartmentScheduleViewModel;

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                Vm?.RefreshRows();
                Vm?.RefreshTemplate();
                Vm?.BuildGeneralRows();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Refresh Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Vm is null)
                {
                    MessageBox.Show("There is no prepared data available for printing.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Vm.RefreshTemplate();
                Vm.BuildGeneralRows();

                var win = new DepartmentScheduleWindow(Vm)
                {
                    Owner = Window.GetWindow(this)
                };

                win.PrintSilent();
                win.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
