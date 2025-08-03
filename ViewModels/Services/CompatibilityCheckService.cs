using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.ViewModels.Services
{
    /// <summary>
    /// Service responsible for device compatibility checking operations
    /// </summary>
    public class CompatibilityCheckService
    {
        private CancellationTokenSource? _compatibilityCheckCancellation;

        public bool IsCheckingCompatibility { get; private set; }

        public event Action<bool>? IsCheckingCompatibilityChanged;

        public async Task CheckCompatibilityAsync(ObservableCollection<Camera> siteDevices)
        {
            var selectedDevices = GetSelectedDevicesWithIP(siteDevices);
            if (selectedDevices.Count == 0)
            {
                MessageBox.Show("Please select devices with IP addresses to check compatibility.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RunCompatibilityCheck(selectedDevices);
        }

        public void CancelCompatibilityCheck()
        {
            _compatibilityCheckCancellation?.Cancel();
        }

        public bool CanCheckCompatibility(ObservableCollection<Camera> siteDevices) =>
            !IsCheckingCompatibility && siteDevices.Any(d => d.IsSelected && !string.IsNullOrEmpty(d.CurrentIP));

        public bool CanCancelCompatibilityCheck() =>
            IsCheckingCompatibility && _compatibilityCheckCancellation != null;

        private static List<Camera> GetSelectedDevicesWithIP(ObservableCollection<Camera> siteDevices)
        {
            return siteDevices.Where(d => d.IsSelected && !string.IsNullOrEmpty(d.CurrentIP)).ToList();
        }

        private async Task RunCompatibilityCheck(List<Camera> selectedDevices)
        {
            _compatibilityCheckCancellation?.Cancel();
            _compatibilityCheckCancellation = new CancellationTokenSource();
            SetIsCheckingCompatibility(true);

            try
            {
                InitializeDevicesForCheck(selectedDevices);

                var tasks = selectedDevices.Select(device =>
                    CheckSingleDeviceCompatibilityAsync(device, _compatibilityCheckCancellation.Token));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                HandleCancelledChecks(selectedDevices);
            }
            catch (Exception ex)
            {
                HandleCheckErrors(selectedDevices, ex);
            }
            finally
            {
                SetIsCheckingCompatibility(false);
                _compatibilityCheckCancellation?.Dispose();
                _compatibilityCheckCancellation = null;
            }
        }

        private void SetIsCheckingCompatibility(bool value)
        {
            IsCheckingCompatibility = value;
            IsCheckingCompatibilityChanged?.Invoke(value);
        }

        private static void InitializeDevicesForCheck(List<Camera> devices)
        {
            foreach (var device in devices)
            {
                device.ClearProtocolLogs();
                device.AddProtocolLog("System", "Check Started",
                    $"Initializing compatibility check for {device.CurrentIP}:{device.EffectivePort}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    device.Status = "Initializing check...";
                    device.CellColor = Brushes.LightYellow;
                });
            }
        }

        private async Task CheckSingleDeviceCompatibilityAsync(Camera device, CancellationToken cancellationToken)
        {
            try
            {
                var result = await ProtocolManager.CheckSingleProtocolAsync(device, device.Protocol, cancellationToken);
                SetFinalDeviceStatus(device, result);
            }
            catch (OperationCanceledException)
            {
                HandleCancelledDevice(device);
                throw;
            }
            catch (Exception ex)
            {
                HandleDeviceError(device, ex);
            }
        }

        private static async void SetFinalDeviceStatus(Camera device, ProtocolCompatibilityResult result)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (result.IsCompatible)
                {
                    device.Protocol = result.DetectedProtocol;
                    device.RequiresAuthentication = result.RequiresAuthentication;
                    device.IsAuthenticated = result.IsAuthenticated;
                    device.IsCompatible = true;

                    // Enhanced status determination based on authentication state
                    if (!result.RequiresAuthentication)
                    {
                        // Protocol is compatible and doesn't need authentication
                        device.Status = $"{result.DetectedProtocol} compatible";
                        device.CellColor = Brushes.LightGreen;
                    }
                    else if (result.IsAuthenticated)
                    {
                        // Protocol is compatible and authentication successful
                        device.Status = $"{result.DetectedProtocol} authenticated";
                        device.CellColor = Brushes.LightGreen;
                    }
                    else
                    {
                        // Protocol is compatible but authentication failed
                        device.Status = $"{result.DetectedProtocol} - Auth failed";
                        device.CellColor = Brushes.Orange; // Use orange to distinguish from full success

                        // Add specific authentication message if available
                        if (!string.IsNullOrEmpty(result.AuthenticationMessage))
                        {
                            device.AddProtocolLog(result.DetectedProtocol.ToString(), "Authentication",
                                $"Auth failed: {result.AuthenticationMessage}", ProtocolLogLevel.Warning);
                        }
                    }

                    // Only try to retrieve device information if authentication is successful or not required
                    if (!result.RequiresAuthentication || result.IsAuthenticated)
                    {
                        try
                        {
                            device.AddProtocolLog("System", "Device Info",
                                "Retrieving device information after successful compatibility check...");

                            // Load device information to get model and manufacturer
                            var infoLoaded = await ProtocolManager.LoadCameraInfoAsync(device, CancellationToken.None);

                            if (infoLoaded)
                            {
                                device.AddProtocolLog("System", "Device Info",
                                    $"Successfully retrieved device info - Manufacturer: {device.Manufacturer ?? "Unknown"}, Model: {device.Model ?? "Unknown"}",
                                    ProtocolLogLevel.Success);
                            }
                            else
                            {
                                device.AddProtocolLog("System", "Device Info",
                                    "Failed to retrieve complete device information",
                                    ProtocolLogLevel.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            device.AddProtocolLog("System", "Device Info Error",
                                $"Error retrieving device information: {ex.Message}",
                                ProtocolLogLevel.Error);
                        }
                    }
                    else
                    {
                        device.AddProtocolLog("System", "Device Info",
                            "Skipping device information retrieval due to authentication failure",
                            ProtocolLogLevel.Info);
                    }
                }
                else
                {
                    device.Status = "Not compatible";
                    device.CellColor = Brushes.LightCoral;
                    device.IsCompatible = false;
                }
            });
        }

        private static void HandleCancelledChecks(List<Camera> devices)
        {
            foreach (var device in devices)
            {
                HandleCancelledDevice(device);
            }
        }

        private static void HandleCheckErrors(List<Camera> devices, Exception ex)
        {
            foreach (var device in devices)
            {
                HandleDeviceError(device, ex);
            }
        }

        private static void HandleCancelledDevice(Camera device)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                device.Status = "Check cancelled";
                device.CellColor = Brushes.LightGray;
            });
        }

        private static void HandleDeviceError(Camera device, Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                device.Status = $"Error: {ex.Message}";
                device.CellColor = Brushes.LightCoral;
            });
        }
    }
}