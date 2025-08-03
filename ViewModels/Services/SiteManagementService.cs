using System.Collections.ObjectModel;
using System.Windows;

using Microsoft.Win32;

using wpfhikip.Models;
using wpfhikip.Services;

namespace wpfhikip.ViewModels.Services
{
    /// <summary>
    /// Service responsible for managing site operations
    /// </summary>
    public class SiteManagementService
    {
        private readonly SiteDataService _dataService;

        public SiteManagementService(SiteDataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public void AddSite(Client selectedClient, Action<Site> onSiteAdded)
        {
            if (selectedClient == null) return;

            var dialog = new Views.Dialogs.SiteDialog(null, selectedClient.Id);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var newSite = dialog.SiteResult;
                if (newSite != null)
                {
                    selectedClient.Sites.Add(newSite);
                    onSiteAdded?.Invoke(newSite);
                }
            }
        }

        public void EditSite(Site selectedSite, Action onSiteUpdated)
        {
            if (selectedSite == null) return;

            var dialog = new Views.Dialogs.SiteDialog(selectedSite);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var updatedSite = dialog.SiteResult;
                if (updatedSite != null)
                {
                    UpdateSiteProperties(selectedSite, updatedSite);
                    onSiteUpdated?.Invoke();
                }
            }
        }

        public void DeleteSite(Client selectedClient, Site selectedSite, Action<Site> onSiteDeleted)
        {
            if (selectedSite == null || selectedClient == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete site '{selectedSite.Name}' and all its devices?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                selectedClient.Sites.Remove(selectedSite);
                onSiteDeleted?.Invoke(selectedSite);
            }
        }

        public async Task ExportSiteAsync(Site selectedSite)
        {
            if (selectedSite == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{selectedSite.Name}_export.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    await _dataService.ExportSiteAsync(selectedSite, saveDialog.FileName);
                    MessageBox.Show("Site exported successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting site: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void UpdateSiteProperties(Site target, Site source)
        {
            target.Name = source.Name;
            target.Location = source.Location;
            target.Description = source.Description;
            target.NetworkRange = source.NetworkRange;
            target.VpnAccess = source.VpnAccess;
            target.Notes = source.Notes;
            target.LastModified = source.LastModified;
        }
    }
}