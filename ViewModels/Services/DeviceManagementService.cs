using System.Collections.ObjectModel;
using System.Windows;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.ViewModels.Services
{
    /// <summary>
    /// Service responsible for managing device operations
    /// </summary>
    public class DeviceManagementService
    {
        public void AddDevice(Site selectedSite, Action onDeviceAdded)
        {
            if (selectedSite == null) return;

            var newDevice = CreateNewCamera();
            selectedSite.Devices.Add(newDevice);
            onDeviceAdded?.Invoke();
        }

        public void DeleteSelectedDevices(Site selectedSite, ObservableCollection<Camera> siteDevices,
            Action onDevicesDeleted)
        {
            if (selectedSite == null) return;

            var selectedDevices = siteDevices.Where(d => d.IsSelected).ToList();
            if (selectedDevices.Count == 0) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedDevices.Count} selected devices?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var device in selectedDevices)
                {
                    selectedSite.Devices.Remove(device);
                }
                onDevicesDeleted?.Invoke();
            }
        }

        public void SelectAllDevices(ObservableCollection<Camera> siteDevices)
        {
            foreach (var device in siteDevices)
            {
                device.IsSelected = true;
            }
        }

        public bool CanDeleteSelectedDevices(Site selectedSite, ObservableCollection<Camera> siteDevices) =>
            selectedSite != null && siteDevices.Any(d => d.IsSelected);

        private static Camera CreateNewCamera()
        {
            return new Camera
            {
                Protocol = CameraProtocol.Auto,
                Connection = new CameraConnection { Port = "80", Username = "admin" },
                Settings = new CameraSettings(),
                VideoStream = new CameraVideoStream()
            };
        }
    }
}