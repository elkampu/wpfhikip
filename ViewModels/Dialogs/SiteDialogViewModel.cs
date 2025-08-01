using System.Collections.ObjectModel;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Input;

using wpfhikip.Models;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels.Dialogs
{
    public class SiteDialogViewModel : ViewModelBase
    {
        private Site _site;
        private readonly bool _isEditMode;
        private ObservableCollection<string> _validationErrors = new();

        public event Action<bool?> RequestClose;

        #region Properties

        public Site Site
        {
            get => _site;
            set
            {
                if (SetProperty(ref _site, value))
                {
                    ValidateSite();
                }
            }
        }

        public string DialogTitle => _isEditMode ? "Edit Site" : "Add New Site";
        public string DialogSubtitle => _isEditMode ? "Update site information" : "Enter site details";
        public string SaveButtonText => _isEditMode ? "Update" : "Create";

        public ObservableCollection<string> ValidationErrors
        {
            get => _validationErrors;
            set => SetProperty(ref _validationErrors, value);
        }

        public bool HasValidationErrors => ValidationErrors.Count > 0;

        #endregion

        #region Commands

        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        #endregion

        public SiteDialogViewModel(Site? site = null, string? clientId = null)
        {
            _isEditMode = site != null;

            if (_isEditMode)
            {
                // Create a copy to avoid modifying the original until save
                _site = new Site
                {
                    Id = site!.Id,
                    ClientId = site.ClientId,
                    Name = site.Name,
                    Location = site.Location,
                    Description = site.Description,
                    NetworkRange = site.NetworkRange,
                    VpnAccess = site.VpnAccess,
                    Notes = site.Notes,
                    CreatedDate = site.CreatedDate,
                    LastModified = site.LastModified,
                    Devices = site.Devices
                };
            }
            else
            {
                _site = new Site
                {
                    Id = Guid.NewGuid().ToString(),
                    ClientId = clientId ?? string.Empty,
                    Name = string.Empty,
                    Location = string.Empty,
                    Description = string.Empty,
                    NetworkRange = string.Empty,
                    VpnAccess = string.Empty,
                    Notes = string.Empty,
                    CreatedDate = DateTime.Now,
                    LastModified = DateTime.Now
                };
            }

            InitializeCommands();
            ValidateSite();

            // Subscribe to property changes on the site
            _site.PropertyChanged += (s, e) => ValidateSite();
        }

        private void InitializeCommands()
        {
            SaveCommand = new RelayCommand(_ => Save(), _ => CanSave());
            CancelCommand = new RelayCommand(_ => Cancel());
        }

        private void ValidateSite()
        {
            ValidationErrors.Clear();

            // Required field validation
            if (string.IsNullOrWhiteSpace(_site.Name))
            {
                ValidationErrors.Add("Site name is required.");
            }

            // Network range validation (if provided)
            if (!string.IsNullOrWhiteSpace(_site.NetworkRange) && !IsValidNetworkRange(_site.NetworkRange))
            {
                ValidationErrors.Add("Please enter a valid network range (e.g., 192.168.1.0/24).");
            }

            OnPropertyChanged(nameof(HasValidationErrors));
        }

        private static bool IsValidNetworkRange(string networkRange)
        {
            try
            {
                // Basic CIDR notation validation
                var cidrPattern = @"^(\d{1,3}\.){3}\d{1,3}/\d{1,2}$";
                if (!Regex.IsMatch(networkRange, cidrPattern))
                    return false;

                var parts = networkRange.Split('/');
                if (parts.Length != 2)
                    return false;

                // Validate IP address
                if (!IPAddress.TryParse(parts[0], out var ipAddress))
                    return false;

                // Validate subnet mask
                if (!int.TryParse(parts[1], out var subnetMask) || subnetMask < 0 || subnetMask > 32)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool CanSave()
        {
            return !HasValidationErrors && !string.IsNullOrWhiteSpace(_site.Name);
        }

        private void Save()
        {
            if (!CanSave()) return;

            _site.LastModified = DateTime.Now;
            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
    }
}