namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// override-related lists are synced from Store.
        /// </summary>
        public void EnsureOverridesUiSynced(bool cfoOnly = false)
        {
            // Lookups
            SyncCourses();
            SyncFaculties();

            if (cfoOnly)
            {
                SyncCfoRowsFromStore();
            }
            else
            {
                SyncOverrideRowsFromStore();
            }

            RaiseAllCanExec();
        }
    }
}
