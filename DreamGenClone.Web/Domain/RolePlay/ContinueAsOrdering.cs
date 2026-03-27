namespace DreamGenClone.Web.Domain.RolePlay;

public static class ContinueAsOrdering
{
    public static IReadOnlyList<ContinueAsActor> OrderDistinct(IEnumerable<ContinueAsActor> actors)
    {
        var ordered = new List<ContinueAsActor>();
        var seen = new HashSet<ContinueAsActor>();

        foreach (var actor in new[] { ContinueAsActor.You, ContinueAsActor.Npc, ContinueAsActor.Custom })
        {
            if (actors.Contains(actor) && seen.Add(actor))
            {
                ordered.Add(actor);
            }
        }

        return ordered;
    }
}
