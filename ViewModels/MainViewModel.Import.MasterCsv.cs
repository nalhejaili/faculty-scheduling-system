using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void ImportMasterCsv()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to import data.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                Title = "Select any file from a folder that contains: departments, faculties, courses, rooms, and slots"
            };
            if (ofd.ShowDialog() != true) return;

            var folder = Path.GetDirectoryName(ofd.FileName)!;
            var preservedCfo = Store.CourseFacultyOverrides.ToList();
            var preservedCcfo = Store.CourseCodeFacultyOverrides.ToList();
            CsvMasterImportService.ImportFolder(Store, folder);

            RestoreCourseFacultyOverridesAfterImport(preservedCfo, preferCurrent: true);
            RestoreCourseCodeFacultyOverridesAfterImport(preservedCcfo, preferCurrent: true);


            SelectedDepartment = null;
            SelectedLevel = null;
            PlanRows.Clear();

            SyncDepartments();
            RefreshLevels();
            SyncFaculties();
            SyncCourses();
            SyncSlots();
            RefreshDeptFaculties();
            SyncOverrideRowsFromStore();
            RefreshPlanRows();
            RefreshRows();
            RaiseAllCanExec();

            ApplySupervisorDepartmentLock(showWarnings: false);

            SaveCoreToDatabase();


            MessageBox.Show("The master data has been imported from CSV.", "Completed");
        }
    }
}
