using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneImageIdentityReadiness(
    string CharacterId,
    string CharacterName,
    string IdentityPackId,
    int IdentityPackVersion,
    string CanonicalFaceAssetId,
    string FileRelativePath,
    string Sha256,
    SceneImageReferenceFaceView? FaceView = null);

public sealed record SceneImageIdentityReferenceSelection(
    string CharacterId,
    string ReferenceAssetId);
