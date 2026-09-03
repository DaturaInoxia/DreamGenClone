using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ICharacterAppearanceVersionRepository
{
    Task<CharacterBodyProfileVersion?> GetBodyProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterBodyProfileVersion>> ListBodyProfilesAsync(string characterProfileId, CancellationToken cancellationToken = default);
    Task<CharacterBodyProfileVersion?> GetLatestApprovedBodyProfileAsync(string characterProfileId, CancellationToken cancellationToken = default);
    Task<CharacterBodyProfileVersion> CreateBodyProfileDraftAsync(CharacterBodyProfileVersion version, CancellationToken cancellationToken = default);
    Task AddBodyAssetBindingAsync(CharacterBodyAssetBinding binding, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterBodyAssetBinding>> ListBodyAssetBindingsAsync(string bodyProfileVersionId, CancellationToken cancellationToken = default);
    Task<CharacterBodyProfileVersion> ApproveBodyProfileAsync(string id, string descriptorSnapshotJson, CancellationToken cancellationToken = default);
    Task<CharacterBodyProfileVersion> SupersedeBodyProfileAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteBodyProfileAsync(string id, CancellationToken cancellationToken = default);

    Task<CharacterWardrobeLookVersion?> GetWardrobeLookAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterWardrobeLookVersion>> ListWardrobeLooksAsync(string characterProfileId, CancellationToken cancellationToken = default);
    Task<CharacterWardrobeLookVersion?> GetLatestApprovedWardrobeLookAsync(string characterProfileId, CancellationToken cancellationToken = default);
    Task<CharacterWardrobeLookVersion> CreateWardrobeLookDraftAsync(CharacterWardrobeLookVersion version, CancellationToken cancellationToken = default);
    Task AddWardrobeAssetBindingAsync(CharacterWardrobeAssetBinding binding, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterWardrobeAssetBinding>> ListWardrobeAssetBindingsAsync(string wardrobeLookVersionId, CancellationToken cancellationToken = default);
    Task<CharacterWardrobeLookVersion> ApproveWardrobeLookAsync(string id, string descriptorSnapshotJson, string coverageFactsJson, CancellationToken cancellationToken = default);
    Task<CharacterWardrobeLookVersion> SupersedeWardrobeLookAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteWardrobeLookAsync(string id, CancellationToken cancellationToken = default);
}