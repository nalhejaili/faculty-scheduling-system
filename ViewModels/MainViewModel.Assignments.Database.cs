using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TrainerScheduler.Security;
using TrainerScheduler.Network;
using TrainerScheduler.Data.Entities;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// Save the current scheduled results (Store.Assignments) into SQLite.
        /// - Admin: saves full snapshot.
        /// - Supervisor: saves only their department's assignments.
        /// </summary>
        public async Task SaveScheduledResultsAsync()
        {
            try
            {
                int? scopeDeptId = AuthContext.IsSupervisor
                    ? AuthContext.Current?.DepartmentId
                    : null;

                var lanSettings = LanSettingsStore.LoadOrDefault();
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    SetLanStatus("Sending the results to the server...");
                    // Diagnostics: compute what we actually intend to send.
                    var assignmentsToSend = Store.Assignments.Count;
                    if (scopeDeptId is int did)
                        assignmentsToSend = Store.Assignments.Count(a => a.DepartmentId == did);

                    var blocksToSend = Store.FacultySlotBlocks.Count;
                    if (scopeDeptId is int bdid)
                    {
                        var allowedFacultyIds = Store.Faculties
                            .Where(f => f.DepartmentId == bdid)
                            .Select(f => f.Id)
                            .ToHashSet();
                        blocksToSend = Store.FacultySlotBlocks.Count(b => allowedFacultyIds.Contains(b.FacultyId));
                    }

                    var (ok, err) = await LanDataClient.PushAssignmentsAndBlocksFromStoreAsync(Store);
                    if (!ok)
                    {
                        SetLanStatusErrorThrottled("Failed to send the results to the server.", null);
                        MessageBox.Show(
	                            $"Unable to send the results to the server.\n\n{err}",
                            "LAN",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    
                    try
                    {
                        var rev = await LanDataClient.TryGetRevisionAsync();
                        if (rev is not null) _lanLastRevision = rev.Value.revision;
                    }
                    catch
                    {
                        // ignore
                    }

                    try
                    {
                        await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(Store);
                        RefreshRows();
                    }
                    catch
                    {
                        // ignore
                    }

                    SetLanStatus($"Send and save completed — Results={assignmentsToSend} Blocks={blocksToSend}");

MessageBox.Show(
                        $"The results were sent to the server successfully.\n\n" +
                        $"- Scheduling results: {assignmentsToSend}\n" +
                        $"- Faculty blocks: {blocksToSend}",
                        "LAN",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var (assignOk, assignError) = await _sqlite.SaveAssignmentsAsync(Store, scopeDeptId);

                var (blockOk, blockError) = await _sqlite.SaveFacultySlotBlocksAsync(Store, scopeDeptId);

                // so connected clients will auto-refresh.
                try
                {
                    if ((assignOk || blockOk) && lanSettings.Mode == LanMode.Server && LanServerManager.IsRunning)
                        await _sqlite.BumpServerRevisionAsync();
                }
                catch
                {
                    // Don't block the save UX if revision bump fails.
                }

                if (!assignOk && blockOk)
                {
                    MessageBox.Show(
                        $"Faculty blocks were saved, but the scheduling results could not be saved.\n\n{assignError}",
                        "Save Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (assignOk && !blockOk)
                {
                    MessageBox.Show(
                        $"The scheduling results were saved, but the faculty blocks could not be saved.\n\n{blockError}",
                        "Save Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!assignOk && !blockOk)
                {
                    MessageBox.Show(
                        $"Unable to save the results.\n\n{assignError}\n\n{blockError}",
                        "Save Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show(
                    "The scheduling results and faculty blocks were saved to the local SQLite database.",
                    "Save Results",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                SetLanStatus($"Local SQLite save completed — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to save the results.\n\n{ex.Message}",
                    "Save Results",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Load the last saved schedule from SQLite into Store.Assignments.
        /// We load the full snapshot (all departments) so supervisors still see other departments
        /// as fixed constraints, while the UI will filter what they can view.
        /// </summary>
        public async Task<bool> TryLoadSavedResultsAsync(bool showMessageIfEmpty)
        {
            try
            {
                var lanSettings = LanSettingsStore.LoadOrDefault();
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    var any = await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(Store);
                    if (any)
                    {
                        RefreshRows();
                        SetLanStatus($"Results loaded from the server — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");
                        return true;
                    }

                    if (showMessageIfEmpty)
                    {
                        MessageBox.Show(
                            "There are no saved results on the server.",
                            "LAN",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    return false;
                }

                var loadedAssignments = await _sqlite.TryLoadAssignmentsAsync(Store, scopeDepartmentId: null);
                var loadedBlocks = await _sqlite.TryLoadFacultySlotBlocksAsync(Store);

                if (loadedAssignments || loadedBlocks)
                {
                    RefreshRows();
                    SetLanStatus($"Results loaded from SQLite — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");
                    return true;
                }

                if (showMessageIfEmpty)
                {
                    MessageBox.Show(
                        "There are no saved results in the database.",
                        "Load Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to load the results from SQLite.\n\n{ex.Message}",
                    "Load Results",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }
    }
}
