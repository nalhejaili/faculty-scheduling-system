using System;
using System.Threading.Tasks;
using System.Windows;
using TrainerScheduler.Services;
using TrainerScheduler.Network;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        private readonly SqliteStoreService _sqlite = new();

        public string SqliteDbPath => _sqlite.DatabasePath;

        /// <summary>
        /// If DB is empty, nothing changes and the user can still import JSON/CSV as before.
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            try
            {
                var loaded = false;
                var settings = LanClientContext.Settings ?? LanSettingsStore.LoadOrDefault();

            // Apply persisted auto-refresh preference
            IsLanAutoRefreshEnabled = settings.AutoRefreshEnabled;

                if (settings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    loaded = await LanDataClient.TryLoadCoreLookupsIntoStoreAsync(Store);
                }

                // Fallback to local SQLite (local-only mode or if server fetch fails).
                if (!loaded)
                    loaded = await _sqlite.TryLoadCoreAsync(Store);

                if (loaded)
                {
                    // Refresh UI collections / selections based on the newly loaded Store.
                    SyncDepartments();
                    SyncCourses();
                    SyncFaculties();
                    SyncRooms();
                    SyncSlots();

                    RefreshLevels();
                    RefreshDeptFaculties();
                    RefreshPlanRows();

                    ApplySupervisorDepartmentLock(showWarnings: true);

                    // We load only when nothing is currently in-memory.
                    if (Store.Assignments.Count == 0)
                        await TryLoadSavedResultsAsync(showMessageIfEmpty: false);

                    try
                    {
                        int? scopeDeptId = AuthContext.IsSupervisor ? AuthContext.Current?.DepartmentId : null;
                        await _sqlite.TryLoadStudentsAsync(Store, scopeDepartmentId: scopeDeptId);
                        await _sqlite.TryLoadStudentPlansAsync(Store, scopeDepartmentId: scopeDeptId);
                        await _sqlite.TryLoadStudentEnrollmentsAsync(Store, scopeDepartmentId: scopeDeptId);
                        OnPropertyChanged(nameof(StudentsStatusText));
                    }
                    catch
                    {
                        // silent: optional data; core scheduler must still work
                    }


                    RaiseAllCanExec();

                    EnsureLanAutoRefreshStarted();

                    InitLanStatusOnStartup();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to initialize or load the local SQLite database.\n\n{ex.Message}",
                    "SQLite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// </summary>
        public void SaveCoreToDatabase()
        {
            try
            {
                _sqlite.SaveCoreAsync(Store).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to save the data to SQLite.\n\n{ex.Message}",
                    "SQLite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Used by the overrides UI window so changes survive restart.
        /// </summary>
        public void SaveCourseFacultyOverridesToDatabase()
        {
            try
            {
                _sqlite.SaveCourseFacultyOverridesAsync(Store).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to save course/faculty assignments to SQLite.\n\n{ex.Message}",
                    "SQLite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }


        /// <summary>
        /// Used by the overrides UI window so changes survive restart.
        /// </summary>
        public void SaveCourseCodeFacultyOverridesToDatabase()
        {
            try
            {
                _sqlite.SaveCourseCodeFacultyOverridesAsync(Store).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to save course-code faculty assignments to the database.\n\n" + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }




        /// <summary>
        /// Useful after manual edits (CRUD) so pickers update immediately.
        /// </summary>
        public async Task ReloadCoreFromDatabaseAsync()
        {
            try
            {
                var loaded = false;
                var settings = LanClientContext.Settings ?? LanSettingsStore.LoadOrDefault();

            // Apply persisted auto-refresh preference
            IsLanAutoRefreshEnabled = settings.AutoRefreshEnabled;

                if (settings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    loaded = await LanDataClient.TryLoadCoreLookupsIntoStoreAsync(Store);
                }

                if (!loaded)
                    loaded = await _sqlite.TryLoadCoreAsync(Store);

                if (loaded)
                {
                    SyncDepartments();
                    SyncCourses();
                    SyncFaculties();
                    SyncRooms();
                    SyncSlots();

                    RefreshLevels();
                    RefreshDeptFaculties();
                    RefreshPlanRows();

                    ApplySupervisorDepartmentLock(showWarnings: false);

                    RaiseAllCanExec();

                    EnsureLanAutoRefreshStarted();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to reload the data from SQLite.\n\n{ex.Message}",
                    "SQLite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
