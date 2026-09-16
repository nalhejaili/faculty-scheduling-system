using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using MiniTrainerScheduler.ViewModels;
using System.Collections.Generic;
using System.Windows;

namespace MiniTrainerScheduler.Views
{
    public partial class ManualSlotsPickerWindow : Window
    {
        private readonly ManualSlotsPickerViewModel _vm;

        public ManualSlotsPickerWindow(DataStore store, int maxSlots)
            : this(store, maxSlots, preselectedSlotIds: null, headerText: null, requireExact: false)
        {
        }

        public ManualSlotsPickerWindow(
            DataStore store,
            int maxSlots,
            IEnumerable<int>? preselectedSlotIds,
            string? headerText,
            bool requireExact)
        {
            InitializeComponent();
            _vm = new ManualSlotsPickerViewModel(store, maxSlots, preselectedSlotIds, headerText, requireExact);
            DataContext = _vm;
        }

        public IReadOnlyList<Slot> SelectedSlots => _vm.GetSelectedSlotsLimited();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanAccept)
            {
                if (_vm.RequireExact)
                {
                    MessageBox.Show(
                        $"You must select exactly {_vm.MaxSlots} time slot(s).\nCurrently selected: {_vm.SelectedCount}.",
                        "Notice",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "No time slot has been selected.",
                        "Notice",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
