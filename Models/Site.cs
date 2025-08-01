using System.Collections.ObjectModel;

namespace wpfhikip.Models
{
    /// <summary>
    /// Represents a site that belongs to a client and contains multiple network devices
    /// </summary>
    public class Site : BaseNotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _location = string.Empty;
        private string _description = string.Empty;
        private string _networkRange = string.Empty;
        private string _vpnAccess = string.Empty;
        private string _notes = string.Empty;
        private DateTime _createdDate = DateTime.Now;
        private DateTime _lastModified = DateTime.Now;
        private string _clientId = string.Empty;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string ClientId
        {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Location
        {
            get => _location;
            set => SetProperty(ref _location, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string NetworkRange
        {
            get => _networkRange;
            set => SetProperty(ref _networkRange, value);
        }

        public string VpnAccess
        {
            get => _vpnAccess;
            set => SetProperty(ref _vpnAccess, value);
        }

        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set => SetProperty(ref _createdDate, value);
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set => SetProperty(ref _lastModified, value);
        }

        public ObservableCollection<Camera> Devices { get; set; } = new();

        /// <summary>
        /// Gets the number of devices in this site
        /// </summary>
        public int DeviceCount => Devices.Count;

        /// <summary>
        /// Gets the display text for the site
        /// </summary>
        public string DisplayName => $"{Name} ({DeviceCount} devices)";
    }
}