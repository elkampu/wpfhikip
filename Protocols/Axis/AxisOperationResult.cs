namespace wpfhikip.Protocols.Axis
{
    /// <summary>
    /// Result of Axis camera operations
    /// </summary>
    public sealed record AxisOperationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public Dictionary<string, object>? Data { get; init; }

        public static AxisOperationResult CreateSuccess(string message = "Operation completed successfully", Dictionary<string, object>? data = null)
        {
            return new AxisOperationResult
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        public static AxisOperationResult CreateFailure(string message)
        {
            return new AxisOperationResult
            {
                Success = false,
                Message = message
            };
        }
    }
}