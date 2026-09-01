namespace DreamGenClone.Application.Abstractions;

public sealed class StructuredTextCompletionException : Exception
{
    public StructuredTextCompletionException(
        string errorCode,
        string message,
        bool isTransient,
        Exception? innerException = null) : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsTransient = isTransient;
    }

    public string ErrorCode { get; }
    public bool IsTransient { get; }
}