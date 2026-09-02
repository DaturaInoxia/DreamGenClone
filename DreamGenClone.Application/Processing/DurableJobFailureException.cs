namespace DreamGenClone.Application.Processing;

public sealed class DurableJobFailureException : Exception
{
    public DurableJobFailureException(string errorCode, string message, bool isTransient)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("A durable job error code is required.", nameof(errorCode));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A durable job error message is required.", nameof(message));

        ErrorCode = errorCode.Trim();
        IsTransient = isTransient;
    }

    public string ErrorCode { get; }
    public bool IsTransient { get; }
}