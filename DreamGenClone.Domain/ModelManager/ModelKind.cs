namespace DreamGenClone.Domain.ModelManager;

/// <summary>What a registered model can do. Gates the function-default dropdown filter.</summary>
public enum ModelKind
{
    /// <summary>Text completion model (default for all existing models).</summary>
    Text = 0,

    /// <summary>Image generation model.</summary>
    Image = 1,

    /// <summary>
    /// Video generation model (MiniMax H3 local, today). Appended, never renumbered: the integer is persisted and
    /// the Model Manager transfer table indexes enum names by value. A video model is NOT an image model - it
    /// renders picture and audio together through a different node - which is why it gets its own member rather
    /// than a new <see cref="SceneImageModelFamily"/> value.
    /// </summary>
    Video = 2
}
