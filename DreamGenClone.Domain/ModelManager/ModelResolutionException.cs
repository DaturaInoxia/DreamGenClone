namespace DreamGenClone.Domain.ModelManager;

public sealed class ModelResolutionException : Exception
{
    public ModelResolutionException(string message) : base(message) { }
    public ModelResolutionException(string message, Exception innerException) : base(message, innerException) { }
}
