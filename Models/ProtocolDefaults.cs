namespace wpfhikip.Models
{
    /// <summary>
    /// Provides default configuration values for camera protocols
    /// </summary>
    public static class ProtocolDefaults
    {
        /// <summary>
        /// Gets the default port for a specific camera protocol
        /// </summary>
        public static int GetDefaultPort(CameraProtocol protocol) => protocol switch
        {
            CameraProtocol.Hikvision => 80,
            CameraProtocol.Dahua => 80,
            CameraProtocol.Axis => 80,
            CameraProtocol.Onvif => 80,
            _ => 80
        };

        /// <summary>
        /// Gets the default authentication mode for a protocol
        /// </summary>
        public static AuthenticationMode GetDefaultAuthMode(CameraProtocol protocol) => protocol switch
        {
            CameraProtocol.Hikvision => AuthenticationMode.Digest,
            CameraProtocol.Dahua => AuthenticationMode.Digest,
            CameraProtocol.Axis => AuthenticationMode.Basic,
            CameraProtocol.Onvif => AuthenticationMode.WSUsernameToken,
            _ => AuthenticationMode.Basic
        };
    }
}