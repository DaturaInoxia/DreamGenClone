using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The governance record a production approval writes is FILLED IN from these defaults rather than typed, so they are
/// the values that actually reach the database for every ordinary approval. That makes them worth pinning: a blank one
/// would fail the store's own validation, and a malformed one would be stored as JSON and displayed as such.
///
/// <para>
/// The use scope is derived only where the container type settles it, and is deliberately NULL where it does not — the
/// form then requires a real choice instead of inventing one.
/// </para>
/// </summary>
public sealed class SceneAssetApprovalDefaultsTests
{
    /// <summary>
    /// Every default that the store requires non-blank must actually be non-blank, or the one-click approval the
    /// defaults exist to enable would be refused by the repository.
    /// </summary>
    [Fact]
    public void EveryRecordedDefaultIsPresent()
    {
        Assert.False(string.IsNullOrWhiteSpace(SceneAssetApprovalDefaults.ProvenanceJson));
        Assert.False(string.IsNullOrWhiteSpace(SceneAssetApprovalDefaults.Consent));
        Assert.False(string.IsNullOrWhiteSpace(SceneAssetApprovalDefaults.License));
        Assert.False(string.IsNullOrWhiteSpace(SceneAssetApprovalDefaults.LicenseLabel));
        Assert.False(string.IsNullOrWhiteSpace(SceneAssetApprovalDefaults.ContentPolicyKey));
        Assert.False(string.IsNullOrWhiteSpace(SceneAssetApprovalDefaults.CompatibilityJson));
    }

    /// <summary>
    /// The two JSON columns are stored as JSON and shown as JSON, so the defaults have to BE valid JSON — an empty
    /// string would pass the store's "non-blank" check while being unparseable to anything that ever reads it.
    /// </summary>
    [Fact]
    public void TheJsonDefaultsAreValidJson()
    {
        using var provenance = JsonDocument.Parse(SceneAssetApprovalDefaults.ProvenanceJson);
        using var compatibility = JsonDocument.Parse(SceneAssetApprovalDefaults.CompatibilityJson);

        Assert.Equal(JsonValueKind.Object, provenance.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Object, compatibility.RootElement.ValueKind);
        Assert.Equal(0, compatibility.RootElement.EnumerateObject().Count());
    }

    /// <summary>
    /// The consent and licence defaults must parse to the enum members the approval path expects, and must not be the
    /// <c>Unknown</c> state the store refuses.
    /// </summary>
    [Fact]
    public void TheConsentAndLicenceDefaultsAreTheNotApplicableStates()
    {
        Assert.Equal(
            SceneAssetConsentState.NotApplicable,
            Enum.Parse<SceneAssetConsentState>(SceneAssetApprovalDefaults.Consent));
        Assert.Equal(
            SceneAssetLicenseState.NotApplicable,
            Enum.Parse<SceneAssetLicenseState>(SceneAssetApprovalDefaults.License));
    }

    /// <summary>
    /// A scope is derived only from types that settle it. A location is approved as a location; a prop or a playground
    /// container has no single scope, so the default is null and the operator is asked.
    /// </summary>
    [Theory]
    [InlineData(SceneAssetType.Location, SceneAssetApprovedUseScope.Location)]
    [InlineData(SceneAssetType.Wardrobe, SceneAssetApprovedUseScope.CharacterWardrobe)]
    [InlineData(SceneAssetType.CharacterFace, SceneAssetApprovedUseScope.CharacterIdentity)]
    [InlineData(SceneAssetType.CharacterBody, SceneAssetApprovedUseScope.CharacterBody)]
    public void ASettledScopeIsDerived(SceneAssetType type, SceneAssetApprovedUseScope expected)
        => Assert.Equal(expected, SceneAssetApprovalDefaults.UseScopeFor(type));

    [Theory]
    [InlineData(SceneAssetType.Prop)]
    [InlineData(SceneAssetType.Style)]
    [InlineData(SceneAssetType.Playground)]
    [InlineData(SceneAssetType.Character)]
    [InlineData(SceneAssetType.ProductionFrame)]
    [InlineData(SceneAssetType.CharacterPose)]
    public void AnUnsettledScopeIsLeftForTheOperatorToChoose(SceneAssetType type)
        => Assert.Null(SceneAssetApprovalDefaults.UseScopeFor(type));
}
