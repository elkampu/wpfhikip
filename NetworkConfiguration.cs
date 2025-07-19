using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

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
    private string? customPort;
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

    public string? CustomPort
    {
        get => customPort;
        set
        {
            customPort = value;
            OnPropertyChanged(nameof(CustomPort));
        }
    }

    /// <summary>
    /// Gets the effective port to use for connections.
    /// Returns the custom port if specified, otherwise returns the default port for the model.
    /// </summary>
    public int EffectivePort
    {
        get
        {
            // If custom port is specified and valid, use it
            if (!string.IsNullOrEmpty(CustomPort) && int.TryParse(CustomPort, out int port) && port > 0 && port <= 65535)
            {
                return port;
            }

            // Return default port based on model
            return GetDefaultPortForModel(Model);
        }
    }

    /// <summary>
    /// Gets the default port for a specific camera model
    /// </summary>
    private static int GetDefaultPortForModel(string? model)
    {
        return model?.ToLower() switch
        {
            "hikvision" => 80,
            "dahua" => 80,
            "axis" => 80,
            "onvif" => 80,
            "bosch" => 80,
            "hanwha" => 80,
            _ => 80 // Default HTTP port
        };
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
            OnPropertyChanged(nameof(ShortStatus)); // Update ShortStatus when Status changes
        }
    }

    /// <summary>
    /// Gets a shortened version of the status for display in the DataGrid
    /// </summary>
    public string? ShortStatus
    {
        get
        {
            if (string.IsNullOrEmpty(status))
                return status;

            // Truncate status to fit in the cell (approximately 10 characters)
            return status.Length > 10 ? status.Substring(0, 7) + "..." : status;
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