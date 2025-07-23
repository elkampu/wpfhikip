namespace wpfhikip.Discovery.Models
{
    /// <summary>
    /// Types of devices that can be discovered on the network
    /// </summary>
    public enum DeviceType
    {
        Unknown = 0,

        // Network Infrastructure
        Router = 1,
        Switch = 2,
        AccessPoint = 3,
        Firewall = 4,
        Gateway = 5,
        Modem = 6,

        // Security Devices
        Camera = 10,
        NVR = 11,
        DVR = 12,
        VideoEncoder = 13,
        AccessControl = 14,
        IntercomSystem = 15,
        AlarmSystem = 16,

        // Servers and Workstations
        Server = 20,
        Workstation = 21,
        Laptop = 22,
        MobileDevice = 23,
        Tablet = 24,

        // IoT and Smart Devices
        SmartTV = 30,
        SmartSpeaker = 31,
        SmartLight = 32,
        SmartPlug = 33,
        SmartThermostat = 34,
        SmartLock = 35,
        SmartSensor = 36,

        // Media and Entertainment
        MediaServer = 40,
        MediaPlayer = 41,
        GameConsole = 42,
        StreamingDevice = 43,

        // Printers and Scanners
        Printer = 50,
        Scanner = 51,
        Copier = 52,
        Fax = 53,

        // Storage Devices
        NAS = 60,
        SAN = 61,
        ExternalStorage = 62,

        // Industrial and Specialized
        PLCController = 70,
        HMI = 71,
        IndustrialSensor = 72,
        MedicalDevice = 73,
        EnergyManagement = 74,

        // Virtual and Cloud
        VirtualMachine = 80,
        Container = 81,
        CloudService = 82
    }

    /// <summary>
    /// Extension methods for DeviceType enum
    /// </summary>
    public static class DeviceTypeExtensions
    {
        /// <summary>
        /// Gets a human-readable description of the device type
        /// </summary>
        public static string GetDescription(this DeviceType deviceType)
        {
            return deviceType switch
            {
                DeviceType.Unknown => "Unknown Device",
                DeviceType.Router => "Router",
                DeviceType.Switch => "Network Switch",
                DeviceType.AccessPoint => "Wireless Access Point",
                DeviceType.Firewall => "Firewall",
                DeviceType.Gateway => "Gateway",
                DeviceType.Modem => "Modem",
                DeviceType.Camera => "IP Camera",
                DeviceType.NVR => "Network Video Recorder",
                DeviceType.DVR => "Digital Video Recorder",
                DeviceType.VideoEncoder => "Video Encoder",
                DeviceType.AccessControl => "Access Control System",
                DeviceType.IntercomSystem => "Intercom System",
                DeviceType.AlarmSystem => "Alarm System",
                DeviceType.Server => "Server",
                DeviceType.Workstation => "Workstation",
                DeviceType.Laptop => "Laptop",
                DeviceType.MobileDevice => "Mobile Device",
                DeviceType.Tablet => "Tablet",
                DeviceType.SmartTV => "Smart TV",
                DeviceType.SmartSpeaker => "Smart Speaker",
                DeviceType.SmartLight => "Smart Light",
                DeviceType.SmartPlug => "Smart Plug",
                DeviceType.SmartThermostat => "Smart Thermostat",
                DeviceType.SmartLock => "Smart Lock",
                DeviceType.SmartSensor => "Smart Sensor",
                DeviceType.MediaServer => "Media Server",
                DeviceType.MediaPlayer => "Media Player",
                DeviceType.GameConsole => "Game Console",
                DeviceType.StreamingDevice => "Streaming Device",
                DeviceType.Printer => "Printer",
                DeviceType.Scanner => "Scanner",
                DeviceType.Copier => "Copier",
                DeviceType.Fax => "Fax Machine",
                DeviceType.NAS => "Network Attached Storage",
                DeviceType.SAN => "Storage Area Network",
                DeviceType.ExternalStorage => "External Storage",
                DeviceType.PLCController => "PLC Controller",
                DeviceType.HMI => "Human Machine Interface",
                DeviceType.IndustrialSensor => "Industrial Sensor",
                DeviceType.MedicalDevice => "Medical Device",
                DeviceType.EnergyManagement => "Energy Management System",
                DeviceType.VirtualMachine => "Virtual Machine",
                DeviceType.Container => "Container",
                DeviceType.CloudService => "Cloud Service",
                _ => "Unknown Device"
            };
        }

        /// <summary>
        /// Gets the category of the device type
        /// </summary>
        public static string GetCategory(this DeviceType deviceType)
        {
            return deviceType switch
            {
                >= DeviceType.Router and <= DeviceType.Modem => "Network Infrastructure",
                >= DeviceType.Camera and <= DeviceType.AlarmSystem => "Security",
                >= DeviceType.Server and <= DeviceType.Tablet => "Computing",
                >= DeviceType.SmartTV and <= DeviceType.SmartSensor => "Smart Home/IoT",
                >= DeviceType.MediaServer and <= DeviceType.StreamingDevice => "Media & Entertainment",
                >= DeviceType.Printer and <= DeviceType.Fax => "Office Equipment",
                >= DeviceType.NAS and <= DeviceType.ExternalStorage => "Storage",
                >= DeviceType.PLCController and <= DeviceType.EnergyManagement => "Industrial",
                >= DeviceType.VirtualMachine and <= DeviceType.CloudService => "Virtual/Cloud",
                _ => "Unknown"
            };
        }
    }
}