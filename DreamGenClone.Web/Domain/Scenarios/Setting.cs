namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents the setting/world component of a scenario.
/// Includes world description, rules, and environmental context.
/// </summary>
public class Setting
{
    public string? WorldDescription { get; set; }
    public string? TimeFrame { get; set; }
    public List<string> EnvironmentalDetails { get; set; } = [];
    public List<string> WorldRules { get; set; } = [];

    /// <summary>
    /// The world/Setting's root location container and its rendering-oriented description, kept separate from the
    /// narrative <see cref="WorldDescription"/>. Null on a payload written before this existed (null = not configured),
    /// which deserialization preserves without a migration.
    /// </summary>
    public WorldLocationSetting? WorldLocation { get; set; }
}

/// <summary>
/// The world/Setting's link to its root location container. <see cref="Name"/> is the operator's name for the
/// world container ("Camp Ground") — required, because the scenario is what CREATES that container, and a container
/// cannot be created without a name. <see cref="AssetContainerId"/> is the created container;
/// <see cref="RenderingDescription"/> is what the place looks like (a rendering brief), not the narrative mood.
///
/// <para>
/// A mutable class because the Scenario Editor binds it directly.
/// </para>
/// </summary>
public sealed class WorldLocationSetting
{
    /// <summary>
    /// The world container's name — the root of every location container in this scenario.
    /// Required on save: a blank name is refused rather than defaulted.
    /// </summary>
    public string? Name { get; set; }

    public string? AssetContainerId { get; set; }

    public string? RenderingDescription { get; set; }
}
