using System.Windows.Controls;
using MiniTrainerScheduler.Services;
using MiniTrainerScheduler.ViewModels;

namespace MiniTrainerScheduler.Views
{
    public partial class MasterBoardView : UserControl
    {
        public MasterBoardView()
        {
            InitializeComponent();
        }

        public void Init(DataStore store)
        {
            DataContext = new MasterBoardViewModel(store);
        }

        public System.Windows.FrameworkElement TemplateRoot => MasterTemplateRoot;
    }
}
