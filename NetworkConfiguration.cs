using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;

namespace wpfhikip
{

    public class NetworkConfiguration : INotifyPropertyChanged
    {
        private bool isSelected;
        private string? model;
        private string? currentIP;
        private string? newIP;
        private string? newMask;
        private string? newGateway;
        private string? newNTPServer;
        private string? user;
        private string? password;
        private string? status;
        private string? onlineStatus;
        private Brush? rowColor;
        private Brush? cellColor;
        private FontWeight cellFontWeight;
        private bool isCompleted;


        public bool IsSelected
        {
            get
            {
                return isSelected;
            }

            set
            {
                isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
        public string? Model
        {
            get
            {
                return model;
            }

            set
            {
                model = value;
                OnPropertyChanged(nameof(Model));
            }
        }
        public string? CurrentIP
        {
            get
            {
                return currentIP;
            }

            set
            {
                currentIP = value;
                OnPropertyChanged(nameof(CurrentIP));
            }
        }
        public string? NewIP
        {
            get => newIP;
            set
            {
                newIP = value;
                OnPropertyChanged(nameof(NewIP));
            }
        }
        public string? NewMask
        {
            get => newMask;
            set
            {
                newMask = value;
                OnPropertyChanged(nameof(NewMask));
            }
        }
        public string? NewGateway
        {
            get => newGateway;
            set
            {
                newGateway = value;
                OnPropertyChanged(nameof(NewGateway));
            }
        }


        public string? NewNTPServer
        {
            get => newNTPServer;

            set
            {
                newNTPServer = value;
                OnPropertyChanged(nameof(NewNTPServer));
            }
        }

        public string? User
        {
            get
            {
                return user;
            }

            set
            {
                user = value;
                OnPropertyChanged(nameof(User));
            }
        }
        public string? Password
        {
            get
            {
                return password;
            }

            set
            {
                password = value;
                OnPropertyChanged(nameof(Password));
            }
        }
        public Brush? RowColor
        {
            get
            {
                return rowColor;
            }

            set
            {
                rowColor = value;
                OnPropertyChanged(nameof(RowColor));
            }
        }
        public Brush? CellColor
        {
            get
            {
                return cellColor;
            }

            set
            {
                cellColor = value;
                OnPropertyChanged(nameof(CellColor));
            }
        }
        public FontWeight CellFontWeight
        {
            get
            {
                return cellFontWeight;
            }

            set
            {
                cellFontWeight = value;
                OnPropertyChanged(nameof(CellFontWeight));
            }
        }
        public bool IsCompleted
        {
            get
            {
                return isCompleted;
            }

            set
            {
                isCompleted = value;
                OnPropertyChanged(nameof(IsCompleted));
            }
        }
        public string? Status
        {
            get
            {
                return status;
            }

            set
            {
                status = value;
                OnPropertyChanged(nameof(Status));
            }
        }
        public string? OnlineStatus
        {
            get
            {
                return onlineStatus;
            }

            set
            {
                onlineStatus = value;
                OnPropertyChanged(nameof(OnlineStatus));
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }


    }

}
