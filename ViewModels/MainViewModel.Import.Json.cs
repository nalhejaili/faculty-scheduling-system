using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void ImportJson()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to import data.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Store is null)
            {
                MessageBox.Show("The data store is not initialized.", "Notice");
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                Title = "Select a JSON file"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                var preservedCfo = Store.CourseFacultyOverrides.ToList();
                var preservedCcfo = Store.CourseCodeFacultyOverrides.ToList();

                var root = JsonImportService.ImportFile(Store, ofd.FileName);

                RestoreCourseFacultyOverridesAfterImport(preservedCfo, preferCurrent: (root?.CourseFacultyOverrides?.Count ?? 0) > 0);
                RestoreCourseCodeFacultyOverridesAfterImport(preservedCcfo, preferCurrent: (root?.CourseCodeFacultyOverrides?.Count ?? 0) > 0);


                JsonImportService.ImportPracticalFlags(Store, ofd.FileName, clearExisting: true);
                JsonImportService.ImportCourseHourSplits(Store, ofd.FileName, clearExisting: true, preferCourses: true);

                SelectedDepartment = null;
                SelectedLevel = null;
                SyncDepartments();
                SyncFaculties();
                SyncCourses();
                SyncSlots();
                SyncRooms();

                RefreshDeptFaculties();
                RefreshPlanRows();
                RefreshRows();
                SyncOverrideRowsFromStore();
                RaiseAllCanExec();

                ApplySupervisorDepartmentLock(showWarnings: false);

                SaveCoreToDatabase();


                var splits = Store.CourseHourSplits?.Count ?? 0;
                var prac = Store.PracticalCourseIds?.Count ?? 0;

                MessageBox.Show(
                    $"The data has been imported from JSON.\n" +
                    $"Lecture/Lab splits: {splits}\n" +
                    $"Courses marked as lab-based: {prac}",
                    "Completed");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "JSON Import Error");
            }
        }
    }
}
