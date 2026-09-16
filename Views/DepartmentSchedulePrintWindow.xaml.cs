using MiniTrainerScheduler.Services;
using System;
using System.Windows;

namespace MiniTrainerScheduler.Views
{
    public partial class DepartmentSchedulePrintWindow : Window
    {
        public DepartmentSchedulePrintWindow(object generalProxy)
        {
            InitializeComponent();

            DataContext = generalProxy;

            Loaded += DepartmentSchedulePrintWindow_Loaded;
        }

        private void DepartmentSchedulePrintWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                PrintService.PrintElement(PrintRoot, "Department Timetable");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Close();
            }
        }
    }
}
