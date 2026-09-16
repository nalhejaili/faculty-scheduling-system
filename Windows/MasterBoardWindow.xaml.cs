using System.Windows;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.Windows
{
    public partial class MasterBoardWindow : Window
    {
        public MasterBoardWindow(DataStore store)
        {
            InitializeComponent();
            try
            {
                MasterView.Init(store);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message + "\n\n" + ex.StackTrace,
                    "University Master Timetable Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            PrintService.PrintElementUnmirrored(MasterView.TemplateRoot, "University Master Timetable");
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
