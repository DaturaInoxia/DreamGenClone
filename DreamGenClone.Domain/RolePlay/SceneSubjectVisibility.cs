namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// How much of one body the composed frame actually shows (B-167).
/// </summary>
/// <remarks>
/// <para>
/// The frame used to be binary: a person was either in the picture or explicitly excluded. That is wrong for the two
/// cases an image composition produces most often — the camera holder, whose hands/arms enter their own first-person
/// frame while the rest of them does not, and a subject whose only depicted engagement is a contact with the holder.
/// </para>
/// <para>
/// <see cref="PartiallyVisible"/> is DERIVED from recorded facts (<c>contacts</c>), never asked of the operator and
/// never guessed: the projection names the part that enters the frame, and a record that carries no contacts cannot
/// produce this state.
/// </para>
/// </remarks>
public enum SceneSubjectVisibility
{
    /// <summary>The whole depicted body is available to the camera.</summary>
    InFrame = 1,

    /// <summary>Only the recorded part(s) enter the frame; the rest of the body is outside it.</summary>
    PartiallyVisible = 2,

    /// <summary>The body is not in the frame at all and must not be depicted.</summary>
    OutOfFrame = 3
}
