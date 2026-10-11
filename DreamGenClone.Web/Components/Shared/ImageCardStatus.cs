namespace DreamGenClone.Components.Shared;

/// <summary>
/// One decision a list of images can be narrowed to, with how many of them carry it.
/// </summary>
/// <param name="Key">The normalized decision, or null for the "All" option.</param>
/// <param name="Label">What the operator reads, e.g. "Approved".</param>
/// <param name="Count">How many images in the list carry this decision.</param>
public sealed record ImageCardFilterOption(string? Key, string Label, int Count);

/// <summary>
/// The status filter every image list shares: which decisions exist in THIS list, and whether a card matches the
/// chosen one.
///
/// <para>
/// The vocabulary is deliberately a fixed set of normalized keys rather than each host inventing its own, so "the same
/// filter" means the same thing on the asset studio, the review deck and the body deck. It is derived from the cards
/// the list actually holds — an option with nothing behind it is never offered — and the order is fixed so the control
/// does not reshuffle as decisions change.
/// </para>
/// </summary>
public static class ImageCardStatus
{
    public const string Undecided = "undecided";
    public const string Pending = "pending";
    public const string Shortlisted = "shortlisted";
    public const string Accepted = "accepted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Revoked = "revoked";
    public const string Archived = "archived";
    public const string Failed = "failed";

    /// <summary>The fixed presentation order: the outcomes an operator looks for, worst last.</summary>
    private static readonly (string Key, string Label)[] Order =
    [
        (Undecided, "Undecided"),
        (Pending, "Pending"),
        (Shortlisted, "Shortlisted"),
        (Accepted, "Accepted"),
        (Approved, "Approved"),
        (Rejected, "Rejected"),
        (Revoked, "Revoked"),
        (Archived, "Archived"),
        (Failed, "Failed")
    ];

    /// <summary>
    /// The options this list can be narrowed to: "All" first (always offered), then every decision that is actually
    /// present. A label is taken from the cards themselves when they supply one, so a host can say "Approved" where
    /// another says "Accepted" while the key stays the same.
    /// </summary>
    public static IReadOnlyList<ImageCardFilterOption> Build(IEnumerable<SceneImageCardModel> cards)
    {
        var materialised = cards as IReadOnlyCollection<SceneImageCardModel> ?? cards.ToList();
        var options = new List<ImageCardFilterOption>
        {
            new(null, "All", materialised.Count)
        };

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var card in materialised)
        {
            if (string.IsNullOrWhiteSpace(card.DecisionKey))
            {
                continue;
            }

            counts[card.DecisionKey] = counts.TryGetValue(card.DecisionKey, out var count) ? count + 1 : 1;
            if (!string.IsNullOrWhiteSpace(card.DecisionLabel))
            {
                labels[card.DecisionKey] = card.DecisionLabel;
            }
        }

        foreach (var (key, defaultLabel) in Order)
        {
            if (counts.TryGetValue(key, out var count) && count > 0)
            {
                options.Add(new ImageCardFilterOption(key, labels.TryGetValue(key, out var label) ? label : defaultLabel, count));
            }
        }

        // A host may use a key outside the fixed set; it is still filterable rather than silently unreachable.
        foreach (var (key, count) in counts.Where(pair => Order.All(entry => !string.Equals(entry.Key, pair.Key, StringComparison.Ordinal))))
        {
            options.Add(new ImageCardFilterOption(key, labels.TryGetValue(key, out var label) ? label : key, count));
        }

        return options;
    }

    /// <summary>True when the card belongs in the chosen view. Null selects everything.</summary>
    public static bool Matches(SceneImageCardModel card, string? selectedKey)
        => string.IsNullOrWhiteSpace(selectedKey)
            || string.Equals(card.DecisionKey, selectedKey, StringComparison.Ordinal);
}
