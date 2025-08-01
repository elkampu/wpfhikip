using System.Collections.ObjectModel;

namespace wpfhikip.Models
{
    /// <summary>
    /// Represents a client/customer that can have multiple sites
    /// </summary>
    public class Client : BaseNotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _contactPerson = string.Empty;
        private string _email = string.Empty;
        private string _phone = string.Empty;
        private string _address = string.Empty;
        private string _notes = string.Empty;
        private DateTime _createdDate = DateTime.Now;
        private DateTime _lastModified = DateTime.Now;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string ContactPerson
        {
            get => _contactPerson;
            set => SetProperty(ref _contactPerson, value);
        }

        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        public string Phone
        {
            get => _phone;
            set => SetProperty(ref _phone, value);
        }

        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
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

        public ObservableCollection<Site> Sites { get; set; } = new();

        /// <summary>
        /// Gets the number of sites for this client
        /// </summary>
        public int SiteCount => Sites.Count;

        /// <summary>
        /// Gets the total number of devices across all sites
        /// </summary>
        public int TotalDeviceCount => Sites.Sum(s => s.Devices.Count);
    }
}