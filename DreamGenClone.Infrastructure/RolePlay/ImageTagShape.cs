namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// The structural rule every store applies to a tag before it writes one: a tag is <c>prefix:value</c>, and never blank.
/// </summary>
/// <remarks>
/// <para>
/// Structural only, deliberately. Which PREFIXES exist is the catalog's business (<c>ImageTagCatalog</c>, in the Web
/// layer), and a store must not learn it — but a store must still refuse something that is not a tag at all, because a
/// row written in a shape no search can match is a tag the operator will never find again.
/// </para>
///
/// <para>
/// It lives here, once, because TWO stores now write tags — the asset library and the rendered-image store — and a
/// second copy of this rule is how one of them would start accepting what the other refuses.
/// </para>
/// </remarks>
internal static class ImageTagShape
{
    public static void Require(IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidOperationException("An image tag cannot be blank.");
            }

            var separator = tag.IndexOf(':');
            if (separator <= 0 || separator == tag.Length - 1)
            {
                throw new InvalidOperationException(
                    $"'{tag}' is not a tag: every tag is 'prefix:value' (for example 'stance:kneeling').");
            }
        }
    }
}
