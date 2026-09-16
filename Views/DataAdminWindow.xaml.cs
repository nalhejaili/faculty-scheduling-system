using MiniTrainerScheduler.ViewModels;
using System.Windows;
using System.Windows.Controls;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Views
{
    public partial class DataAdminWindow : Window
    {
        private readonly DataAdminViewModel _vm;

        public DataAdminWindow(MainViewModel main)
        {
            InitializeComponent();
            _vm = new DataAdminViewModel(main);
            DataContext = _vm;

            // Load data after the window is shown. Use a named handler to avoid unhandled
            // exceptions from async void lambdas crashing the whole app.
            Loaded += DataAdminWindow_Loaded;
        }

        private async void DataAdminWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.ReloadAsync();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Unable to load the Data Management window.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveFaculties_Click(object sender, RoutedEventArgs e)
        {
            // Ensure any pending edits (checkbox/combobox) are committed to the bound entities
            // before executing the Save command; otherwise the last edit may be lost.
            try
            {
                FacultiesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                FacultiesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (System.Exception ex)
            {
                // Don't block saving, but keep a trace for diagnostics.
                DiagnosticsLog.TryWrite(ex);
            }

            _vm.SaveFacultiesCommand.Execute(null);
        }


        private void SaveStudents_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StudentsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                StudentsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (System.Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            _vm.SaveStudentsCommand.Execute(null);
        }

        private void SaveStudentPlans_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StudentPlansGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                StudentPlansGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (System.Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            _vm.SaveStudentPlansCommand.Execute(null);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
