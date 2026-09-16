using System.Linq;
using System.Collections.ObjectModel;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncRooms()
        {
            Rooms.Clear();
            foreach (var r in Store.Rooms.OrderBy(x => x.Name))
                Rooms.Add(r);
        }
    }
}
