using System.Text;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Turns a location image analysis (B-160) into a reference direction statement, and decides when it is ALLOWED to.
/// </summary>
/// <remarks>
/// <para>
/// This is the join between "a vision model read the location image" and "the prompt says what the image is doing".
/// The analysis proposes; the reference's own direction statement is what the prompt reads, so the operator's controls
/// stay the single owner of what is said.
/// </para>
///
/// <para>
/// The station is stated as a NOTE and not as a side/facing, deliberately. A location image holds MANY features — two
/// trailers, a deck, a shed, a line — so there is no single side or facing that could truthfully describe it, and
/// picking one would be a guess of exactly the kind that put a character on the wrong trailer in the first place. The
/// note carries what the image contains; the operator sets a side when the reference really is one thing.
/// </para>
/// </remarks>
public static class ReferenceDirectionsPrefill
{
    /// <summary>
    /// The statement an analysis proposes for a location reference, or null when there is nothing to propose.
    /// </summary>
    /// <remarks>
    /// Always analysis-sourced, so the operator's next edit takes ownership of it and no later analysis can replace
    /// what they wrote.
    /// </remarks>
    public static ReferenceDirections? ForLocationAnalysis(LocationImageAnalysis? analysis)
    {
        if (analysis is null)
        {
            return null;
        }

        var note = Compose(analysis);
        return string.IsNullOrWhiteSpace(note)
            ? null
            : new ReferenceDirections(null, null, note, ReferenceDirectionSource.Analysis);
    }

    /// <summary>
    /// The statement that should be in force for a reference, given what it already carries and what an analysis found.
    /// </summary>
    /// <remarks>
    /// The ONE decision path for "may the model talk over the operator?", and the answer is no: an analysis replaces
    /// only nothing-yet or a statement an earlier analysis proposed. An operator-authored statement is returned
    /// untouched even when a fresh analysis disagrees with it — which is the whole reason "the model tries first, the
    /// operator can overwrite" holds in both directions.
    ///
    /// <para>
    /// A null or unreadable analysis leaves the existing statement exactly as it is, so binding an unanalysed location
    /// changes nothing about what the prompt says.
    /// </para>
    /// </remarks>
    public static ReferenceDirections? Apply(
        ReferenceDirections? existing,
        LocationImageAnalysis? analysis)
    {
        if (existing is not null && !existing.MayBeReplacedByAnalysis)
        {
            return existing;
        }

        return ForLocationAnalysis(analysis) ?? existing;
    }

    /// <summary>
    /// The note text: what the place is, then the features the analysis could place, each with where it sits.
    /// </summary>
    private static string Compose(LocationImageAnalysis analysis)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(analysis.Description))
        {
            sb.Append(analysis.Description.Trim());
        }

        if (analysis.Anchors.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append("Features in this location: ");
            sb.Append(string.Join("; ", analysis.Anchors.Select(DescribeAnchor)));
            sb.Append('.');
        }

        return sb.ToString();
    }

    private static string DescribeAnchor(LocationAnchor anchor)
    {
        var placed = new List<string>();
        if (anchor.Horizontal is { } horizontal)
        {
            placed.Add(horizontal.ToString());
        }

        if (anchor.Depth is { } depth)
        {
            placed.Add(depth.ToString());
        }

        return placed.Count == 0
            ? anchor.Name.Trim()
            : $"{anchor.Name.Trim()} ({string.Join(", ", placed)})";
    }
}
