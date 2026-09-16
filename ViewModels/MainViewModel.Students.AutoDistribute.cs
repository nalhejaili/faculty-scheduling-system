using System;
using System.Threading.Tasks;
using System.Windows;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels;

public sealed partial class MainViewModel
{
    // 4.4
    private async Task TryAutoDistributeStudentsAfterGenerateAsync()
    {
        // If no schedule, nothing to do
        if (Store.Assignments.Count == 0) return;

        try
        {
            int? scopeDepartmentId = null;

            // Respect supervisor scoping
            if (AuthContext.IsSupervisor && AuthContext.Current is not null)
                scopeDepartmentId = AuthContext.Current.DepartmentId;
            else
                scopeDepartmentId = SelectedDepartment?.Id;

            // Ensure students/plans are loaded from DB (these calls clear the current in-memory lists and reload)
            await _sqlite.TryLoadStudentsAsync(Store, scopeDepartmentId);
            await _sqlite.TryLoadStudentPlansAsync(Store, scopeDepartmentId);

            if (Store.Students.Count == 0) return;
            if (Store.StudentPlans.Count == 0) return;

            // Distribute (this method already saves + reloads assignments if needed, and saves enrollments to DB)
            await DistributeStudentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Student Allocation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
