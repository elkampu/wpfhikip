namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Result wrapper for ONVIF operations
    /// </summary>
    public sealed class OnvifOperationResult
    {
        public bool Success { get; private init; }
        public string Message { get; private init; } = string.Empty;

        private OnvifOperationResult() { }

        public static OnvifOperationResult CreateSuccess(string message = "")
        {
            return new OnvifOperationResult { Success = true, Message = message };
        }

        public static OnvifOperationResult CreateFailure(string message)
        {
            return new OnvifOperationResult { Success = false, Message = message };
        }
    }
}