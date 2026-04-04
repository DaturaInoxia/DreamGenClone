namespace DreamGenClone.Domain.ModelManager;

public sealed class FunctionModelDefault
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FunctionName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 0.9;
    public int MaxTokens { get; set; } = 500;
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
}
