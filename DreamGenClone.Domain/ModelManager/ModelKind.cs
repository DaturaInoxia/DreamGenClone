namespace DreamGenClone.Domain.ModelManager;

/// <summary>What a registered model can do. Gates the function-default dropdown filter.</summary>
public enum ModelKind
{
    /// <summary>Text completion model (default for all existing models).</summary>
    Text = 0,

    /// <summary>Image generation model.</summary>
    Image = 1
}
