using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Input;

using wpfhikip.Models;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels.Dialogs
{
    public class ClientDialogViewModel : ViewModelBase
    {
        private Client _client;
        private readonly bool _isEditMode;
        private ObservableCollection<string> _validationErrors = new();

        public event Action<bool?> RequestClose;

        #region Properties

        public Client Client
        {
            get => _client;
            set
            {
                if (SetProperty(ref _client, value))
                {
                    ValidateClient();
                }
            }
        }

        public string DialogTitle => _isEditMode ? "Edit Client" : "Add New Client";
        public string DialogSubtitle => _isEditMode ? "Update client information" : "Enter client details";
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

        public ClientDialogViewModel(Client? client = null)
        {
            _isEditMode = client != null;

            if (_isEditMode)
            {
                // Create a copy to avoid modifying the original until save
                _client = new Client
                {
                    Id = client!.Id,
                    Name = client.Name,
                    ContactPerson = client.ContactPerson,
                    Email = client.Email,
                    Phone = client.Phone,
                    Address = client.Address,
                    Notes = client.Notes,
                    CreatedDate = client.CreatedDate,
                    LastModified = client.LastModified,
                    Sites = client.Sites
                };
            }
            else
            {
                _client = new Client
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = string.Empty,
                    ContactPerson = string.Empty,
                    Email = string.Empty,
                    Phone = string.Empty,
                    Address = string.Empty,
                    Notes = string.Empty,
                    CreatedDate = DateTime.Now,
                    LastModified = DateTime.Now
                };
            }

            InitializeCommands();
            ValidateClient();

            // Subscribe to property changes on the client
            _client.PropertyChanged += (s, e) => ValidateClient();
        }

        private void InitializeCommands()
        {
            SaveCommand = new RelayCommand(_ => Save(), _ => CanSave());
            CancelCommand = new RelayCommand(_ => Cancel());
        }

        private void ValidateClient()
        {
            ValidationErrors.Clear();

            // Required field validation
            if (string.IsNullOrWhiteSpace(_client.Name))
            {
                ValidationErrors.Add("Client name is required.");
            }

            // Email validation
            if (!string.IsNullOrWhiteSpace(_client.Email) && !IsValidEmail(_client.Email))
            {
                ValidationErrors.Add("Please enter a valid email address.");
            }

            OnPropertyChanged(nameof(HasValidationErrors));
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var emailAttribute = new EmailAddressAttribute();
                return emailAttribute.IsValid(email);
            }
            catch
            {
                return false;
            }
        }

        private bool CanSave()
        {
            return !HasValidationErrors && !string.IsNullOrWhiteSpace(_client.Name);
        }

        private void Save()
        {
            if (!CanSave()) return;

            _client.LastModified = DateTime.Now;
            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }
    }
}