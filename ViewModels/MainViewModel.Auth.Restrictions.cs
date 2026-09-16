using MiniTrainerScheduler.Models;
using System.Linq;
using TrainerScheduler.Security;
using System.Windows;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// Exposed for XAML binding.
        /// Supervisor cannot change department pickers.
        /// </summary>
        public bool CanSelectDepartment => !AuthContext.IsSupervisor;

        public bool IsAdmin => AuthContext.IsAdmin;
        public bool IsSupervisor => AuthContext.IsSupervisor;
        public bool IsAdminOrSupervisor => IsAdmin || IsSupervisor;

        private Department? GetSupervisorDepartment()
        {
            if (!AuthContext.IsSupervisor)
                return null;

            var deptId = AuthContext.Current?.DepartmentId;
            if (deptId is null)
                return null;

            return Store.Departments.FirstOrDefault(d => d.Id == deptId.Value)
                   ?? Departments.FirstOrDefault(d => d.Id == deptId.Value);
        }

        /// <summary>
        /// Coerce any department selection for supervisors to their locked department.
        /// Allows callers to pass null (e.g., during refresh) and still get the locked value.
        /// </summary>
        private Department? CoerceDepartmentForSupervisor(Department? requested)
        {
            var locked = GetSupervisorDepartment();
            if (locked is null)
                return requested;

            if (requested is null || requested.Id != locked.Id)
                return locked;

            return requested;
        }

        /// <summary>
        /// Applies supervisor restrictions after Store/Departments are loaded.
        /// Ensures both SelectedDepartment and ManualSelectedDepartment are set.
        /// Also narrows the Departments UI list to the locked department.
        /// </summary>
        private void ApplySupervisorDepartmentLock(bool showWarnings)
        {
            if (!AuthContext.IsSupervisor)
                return;

            var locked = GetSupervisorDepartment();

            if (locked is null)
            {
                if (showWarnings)
                {
                    MessageBox.Show(
                        "The supervisor account is not linked to any department.\n\n" +
                        "Please ask the system administrator to assign a department to this account, then sign in again.",
                        "Supervisor Access",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            // Narrow the UI list so the supervisor cannot even see other departments.
            if (Departments.Count != 1 || Departments.FirstOrDefault()?.Id != locked.Id)
            {
                Departments.Clear();
                Departments.Add(locked);
                OnPropertyChanged(nameof(Departments));
            }

            SelectedDepartment = locked;
            ManualSelectedDepartment = locked;
        }
    }
}
