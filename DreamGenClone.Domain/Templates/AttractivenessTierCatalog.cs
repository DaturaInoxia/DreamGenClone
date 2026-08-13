namespace DreamGenClone.Domain.Templates;

/// <summary>
/// A single attractiveness rating band defined by a rating range (within 1-10),
/// a short label, and effect-based prose containing a physical descriptor AND
/// behavioral-cue sentences (how others react).
/// </summary>
public sealed record AttractivenessTier(int Min, int Max, string Label, string Prose);

/// <summary>
/// Code-defined catalog of the five attractiveness bands (Striking / Attractive /
/// Average / Plain / Repelling), covering ratings 1-10. Single source of truth for
/// band labels and effect-based prose. Analogous to <see cref="PhysicalAttributesCatalog"/>.
/// </summary>
public static class AttractivenessTierCatalog
{
    private static readonly IReadOnlyList<AttractivenessTier> _all =
    [
        new(1, 2, "Repelling",
            "Strikingly unattractive — neglected or actively unappealing features that repel rather than invite. People avoid eye contact and keep their distance; their presence works against them, and attraction is actively absent."),
        new(3, 4, "Plain",
            "Forgettable features that make little impression — unremarkable, slightly off-putting, easily overlooked in a room. Others rarely volunteer attention; their presence registers as neutral-to-negative."),
        new(5, 6, "Average",
            "Unremarkable, ordinary features that fit comfortably into any crowd. People register them without particular interest; interactions run neutral, drawing neither special attention nor avoidance."),
        new(7, 8, "Attractive",
            "Genuinely good-looking with pleasant, well-kept features that read as naturally appealing. Others give lingering looks and easy smiles, act warmer and more attentive than usual, and feel a noticeable pull toward their company."),
        new(9, 10, "Striking",
            "Features that command attention — striking symmetry, a face and body that draw the eye and linger in memory. People turn to look when they enter a room, get flustered or nervous up close, and feel their presence before a word is spoken; attention follows them wherever they go."),
    ];

    /// <summary>All five bands, ordered low→high, non-overlapping, covering 1–10.</summary>
    public static IReadOnlyList<AttractivenessTier> All => _all;

    /// <summary>
    /// Maps a rating (1–10) to exactly one band. Returns null for null or
    /// out-of-range ratings (no tier → the formatter omits the line).
    /// </summary>
    public static AttractivenessTier? Resolve(int? rating)
    {
        if (rating is null) return null;
        return _all.FirstOrDefault(t => rating.Value >= t.Min && rating.Value <= t.Max);
    }
}
