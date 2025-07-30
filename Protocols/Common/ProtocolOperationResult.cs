namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Generic result wrapper for protocol operations
    /// </summary>
    /// <typeparam name="T">The type of data returned by the operation</typeparam>
    public sealed class ProtocolOperationResult<T>
    {
        public bool Success { get; private init; }
        public T? Data { get; private init; }
        public string ErrorMessage { get; private init; } = string.Empty;

        private ProtocolOperationResult() { }

        public static ProtocolOperationResult<T> CreateSuccess(T data)
        {
            return new ProtocolOperationResult<T>
            {
                Success = true,
                Data = data,
                ErrorMessage = string.Empty
            };
        }

        public static ProtocolOperationResult<T> CreateFailure(string errorMessage)
        {
            return new ProtocolOperationResult<T>
            {
                Success = false,
                Data = default,
                ErrorMessage = errorMessage
            };
        }

        public void Deconstruct(out bool success, out T? data, out string errorMessage)
        {
            success = Success;
            data = Data;
            errorMessage = ErrorMessage;
        }
    }
}