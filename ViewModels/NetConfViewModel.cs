using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels
{
    public class NetConfViewModel : ViewModelBase
    {
        public ICommand AddCameraCommand { get; set; }

        public NetConfViewModel() {
            AddCameraCommand = new RelayCommand(AddCamera);
        }

        public void AddCamera(object parameter)
        {
            // Open the Add Camera dialog
            var dialog = new Views.Dialogs.AddCameraRangeDialog();
            dialog.ShowDialog();
        }
    }
}
