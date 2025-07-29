using wpfhikip.Models;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Unified compatibility result for all camera protocols
    /// </summary>
    public sealed record ProtocolCompatibilityResult
    {
        public bool Success { get; init; }
        public bool IsCompatible { get; init; }
        public bool RequiresAuthentication { get; init; }
        public bool IsAuthenticated { get; init; }
        public string Message { get; init; } = string.Empty;
        public string AuthenticationMessage { get; init; } = string.Empty;
        public CameraProtocol DetectedProtocol { get; init; }

        public static ProtocolCompatibilityResult CreateSuccess(CameraProtocol protocol, bool requiresAuth = false, bool isAuthenticated = false, string authMessage = "")
        {
            return new ProtocolCompatibilityResult
            {
                Success = true,
                IsCompatible = true,
                DetectedProtocol = protocol,
                RequiresAuthentication = requiresAuth,
                IsAuthenticated = isAuthenticated,
                AuthenticationMessage = authMessage,
                Message = $"{protocol} device detected"
            };
        }

        public static ProtocolCompatibilityResult CreateFailure(string message, CameraProtocol protocol = CameraProtocol.Auto)
        {
            return new ProtocolCompatibilityResult
            {
                Success = false,
                IsCompatible = false,
                DetectedProtocol = protocol,
                Message = message
            };
        }
    }

    /// <summary>
    /// Unified authentication result for all camera protocols
    /// </summary>
    public sealed record AuthenticationResult
    {
        public bool IsAuthenticated { get; init; }
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;

        public static AuthenticationResult CreateSuccess(string message = "Authentication successful")
        {
            return new AuthenticationResult
            {
                IsAuthenticated = true,
                Success = true,
                Message = message
            };
        }

        public static AuthenticationResult CreateFailure(string message)
        {
            return new AuthenticationResult
            {
                IsAuthenticated = false,
                Success = true,
                Message = message
            };
        }

        public static AuthenticationResult CreateError(string message)
        {
            return new AuthenticationResult
            {
                IsAuthenticated = false,
                Success = false,
                Message = message
            };
        }
    }
}