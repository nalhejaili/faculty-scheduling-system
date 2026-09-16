using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using MiniTrainerScheduler.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.Views
{
    public partial class DepartmentScheduleWindow : Window
    {
        public DepartmentScheduleWindow(DataStore store)
        {
            InitializeComponent();
            // 4B-5c: If opened by a supervisor, lock to their department.
            int? scopeDeptId = null;
            if (AuthContext.IsSupervisor)
                scopeDeptId = AuthContext.Current?.DepartmentId;

            DataContext = new DepartmentScheduleViewModel(store, scopeDeptId);
        }

        public DepartmentScheduleWindow(DepartmentScheduleViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateRoot == null)
                return;

            PrintWithScaling(TemplateRoot, "Department Timetable");
        }

        public void PrintSilent()
        {
            if (TemplateRoot == null)
                return;

            PrintWithScaling(TemplateRoot, "Department Timetable");
        }

        /// <summary>
        /// </summary>
        private static void PrintWithScaling(FrameworkElement visual, string jobName)
        {
            if (visual == null)
                return;

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;

            var originalTransform = visual.LayoutTransform;
            double ow = visual.ActualWidth;
            double oh = visual.ActualHeight;

            if (ow <= 0 || oh <= 0)
            {
                visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                visual.Arrange(new Rect(0, 0, visual.DesiredSize.Width, visual.DesiredSize.Height));
                visual.UpdateLayout();
                ow = visual.ActualWidth;
                oh = visual.ActualHeight;
            }

            double pw = dlg.PrintableAreaWidth;
            double ph = dlg.PrintableAreaHeight;

            double scale = Math.Min(pw / ow, ph / oh);
            if (scale > 1.0) scale = 1.0;

            visual.LayoutTransform = new ScaleTransform(scale, scale);

            var scaledSize = new Size(ow * scale, oh * scale);
            visual.Measure(scaledSize);
            visual.Arrange(new Rect(new Point(0, 0), scaledSize));
            visual.UpdateLayout();

            dlg.PrintVisual(visual, jobName);

            visual.LayoutTransform = originalTransform;
            visual.Measure(new Size(ow, oh));
            visual.Arrange(new Rect(new Size(ow, oh)));
            visual.UpdateLayout();
        }
    }
}
