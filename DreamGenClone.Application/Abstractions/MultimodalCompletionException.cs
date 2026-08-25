namespace DreamGenClone.Application.Abstractions;

public sealed class MultimodalCompletionException : Exception
{
    public MultimodalCompletionException(string message) : base(message) { }
    public MultimodalCompletionException(string message, Exception innerException) : base(message, innerException) { }
}