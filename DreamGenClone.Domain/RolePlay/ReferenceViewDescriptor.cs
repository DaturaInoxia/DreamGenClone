using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>Which part of the character a reference view shows.</summary>
public enum ReferenceViewAxis
{
    Face = 1,
    Body = 2
}

/// <summary>
/// Fine-grained view descriptor for a face or body reference asset. The canonical-slot enums
/// (<see cref="SceneImageReferenceFaceView"/> / <see cref="SceneImageReferenceBodyView"/>) are the
/// bounded compiler and approval contracts; this record carries the unbounded fine-grained data
/// (pitch, intermediate yaw, body rotation and position) so extended views are first-class data
/// rather than enum proliferation. Enum = contract, variety = data.
/// </summary>
public sealed record ReferenceViewDescriptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ReferenceViewAxis Axis { get; init; }

    /// <summary>Facing relative to camera: -90 left profile … 0 front … +90 right profile.</summary>
    public int YawDeg { get; init; }

    /// <summary>Face only: -90 looking up … 0 level … +90 looking down.</summary>
    public int PitchDeg { get; init; }

    /// <summary>Body only: torso rotation relative to camera, in degrees.</summary>
    public int? BodyRotationDeg { get; init; }

    /// <summary>Body only: standing | sitting | kneeling | lying | … (free key).</summary>
    public string? BodyPositionKey { get; init; }

    public string Label { get; init; } = string.Empty;

    public SceneImageReferenceFaceView? FaceCanonicalSlot { get; init; }

    public SceneImageReferenceBodyView? BodyCanonicalSlot { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ReferenceViewDescriptor FromJson(string json)
    {
        var descriptor = JsonSerializer.Deserialize<ReferenceViewDescriptor>(json, JsonOptions)
            ?? throw new InvalidOperationException("The reference view descriptor is empty or invalid.");
        descriptor.Validate();
        return descriptor;
    }

    /// <summary>Throws when the descriptor's axis, slot and state fields are internally inconsistent.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Axis))
            throw new InvalidOperationException("A reference view descriptor requires an explicit axis (Face or Body).");
        if (YawDeg is < -180 or > 180)
            throw new InvalidOperationException("Reference view yaw must be within -180..180 degrees.");

        var slotCount = (FaceCanonicalSlot is not null ? 1 : 0) + (BodyCanonicalSlot is not null ? 1 : 0);
        if (slotCount > 1)
            throw new InvalidOperationException("A reference view descriptor may set at most one canonical slot.");

        if (Axis == ReferenceViewAxis.Face)
        {
            if (FaceCanonicalSlot is not null && !Enum.IsDefined(FaceCanonicalSlot.Value))
                throw new InvalidOperationException("The face canonical slot is invalid.");
            if (BodyCanonicalSlot is not null)
                throw new InvalidOperationException("A face view descriptor cannot set a body canonical slot.");
            if (BodyRotationDeg is not null || BodyPositionKey is not null)
                throw new InvalidOperationException("Body rotation/position belong to body descriptors, not face descriptors.");
            if (PitchDeg is < -90 or > 90)
                throw new InvalidOperationException("Face pitch must be within -90..90 degrees.");
        }
        else
        {
            if (BodyCanonicalSlot is not null && !Enum.IsDefined(BodyCanonicalSlot.Value))
                throw new InvalidOperationException("The body canonical slot is invalid.");
            if (FaceCanonicalSlot is not null)
                throw new InvalidOperationException("A body view descriptor cannot set a face canonical slot.");
            if (PitchDeg != 0)
                throw new InvalidOperationException("Pitch is a face-only axis; a body descriptor must leave it at 0.");
            if (BodyRotationDeg is < -180 or > 180)
                throw new InvalidOperationException("Body rotation must be within -180..180 degrees.");
        }
    }
}
