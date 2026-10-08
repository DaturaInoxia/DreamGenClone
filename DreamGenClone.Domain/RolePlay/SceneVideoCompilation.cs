using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The vocabulary register the clip is authored in (G-8). A CONFIGURED choice, not a compiler default: the NSFW
/// adapters are trained on explicit vocabulary, so a "clinical" register measurably underperforms and must be a
/// deliberate selection the operator can see.
/// </summary>
public enum SceneVideoContentRegister
{
    Clinical = 1,
    Explicit = 2
}

/// <summary>How a dialogue line relates to a cut, so continuity markers are emitted where they apply.</summary>
public enum SceneVideoCutContinuity
{
    /// <summary>The line sits wholly inside one shot.</summary>
    None = 0,

    /// <summary>The line continues across a cut; both connection points get <c>&lt;scenetrans&gt;</c>.</summary>
    CrossesCut = 1,

    /// <summary>The line is cut off by the end of the clip and gets <c>&lt;cutoff&gt;</c>.</summary>
    Truncated = 2
}

/// <summary>One shot of the clip. <c>[Shot 1]</c> has no timestamp by rule; later shots require a cut time.</summary>
/// <param name="Number">1-based shot number.</param>
/// <param name="CutTime">Cut time as <c>MM:SS.mmm</c>; must be null for shot 1.</param>
/// <param name="Description">What the shot shows, in the operator's words.</param>
/// <param name="CameraMotionType">Closed-vocabulary motion type, or empty for none.</param>
/// <param name="CameraAmplitude">"small" / "large" / null for medium.</param>
/// <param name="CameraSpeed">"slow" / "fast" / null for normal.</param>
public sealed record SceneVideoShot(
    int Number,
    string? CutTime,
    string Description,
    string CameraMotionType,
    string? CameraAmplitude = null,
    string? CameraSpeed = null);

/// <summary>
/// One vocal event. Speaker IDs are assigned by the compiler from <paramref name="Order"/> (the vocal-event order),
/// never typed by the operator.
/// </summary>
/// <param name="Order">Position of the vocal event in the clip; the ID assignment sorts by this.</param>
/// <param name="SpeakerName">The speaker's name, shown in the identifying phrase.</param>
/// <param name="SubjectLabel">The <c>&lt;Subject N&gt;</c> the speaker is, when the speaker is a referenced subject.</param>
/// <param name="IdentityInfo">
/// Character type, age, gender, on/off-screen, pitch, timbre, rate or accent. Required at first appearance.
/// </param>
/// <param name="LanguageCode">Language tag for the <c>&lt;d&gt;</c> block, for example "English".</param>
/// <param name="Content">The verbatim spoken content. Never translated or rewritten.</param>
/// <param name="Delivery">Delivery description, outside the <c>&lt;d&gt;</c> block.</param>
public sealed record SceneVideoDialogueLine(
    int Order,
    int ShotNumber,
    string SpeakerName,
    string? SubjectLabel,
    string IdentityInfo,
    string LanguageCode,
    string Content,
    string Delivery,
    bool OffScreen = false,
    bool Voiceover = false,
    SceneVideoCutContinuity Continuity = SceneVideoCutContinuity.None);

/// <summary>A piece of text visible in frame. Rendered verbatim in English double quotes.</summary>
public sealed record SceneVideoOnScreenText(string Text, int ShotNumber = 1);

/// <summary>
/// Everything the deterministic H3 prompt compiler needs. Every field is operator-supplied or comes from a bound
/// reference; the compiler invents nothing.
/// </summary>
/// <param name="StyleDeclaration">One or two sentences, emitted before <c>[Shot 1]</c>.</param>
/// <param name="SceneDescription">The scene/action body (word-count band applies).</param>
/// <param name="References">Ordered references; order IS the <c>&lt;Picture i&gt;</c> numbering.</param>
/// <param name="Shots">Shots in order; shot 1 first and untimed.</param>
/// <param name="Dialogue">Vocal events, ordered by <see cref="SceneVideoDialogueLine.Order"/>.</param>
/// <param name="OnScreenText">Visible text entries.</param>
/// <param name="Soundscape">Concrete object + physical action sentences, or the N/A sentinel.</param>
/// <param name="NonDiegeticMusic">Instrumentation/rhythm sentences, or the N/A sentinel.</param>
/// <param name="ContentRegister">The configured register.</param>
/// <param name="FrameLength">Frame count; validated against the node's rule and the trained band.</param>
/// <param name="Fps">Frames per second, from the model's configuration.</param>
/// <param name="FramePolicy">The model's configured frame envelope.</param>
/// <param name="MinWordCount">Lower bound of the description band.</param>
/// <param name="MaxWordCount">Upper bound of the description band.</param>
/// <param name="VideoVaeName">The configured video VAE, required whenever references are bound (rule 28).</param>
/// <param name="AudioVaeName">The configured audio VAE, required whenever reference audio is bound (rule 28).</param>
/// <param name="DeclaredTimelineEnd">Optional timeline end (MM:SS.mmm) the operator declares for the clip.</param>
/// <param name="AllowUntrainedLength">
/// The operator's deliberate override for a length the node accepts but the model was not trained on (rule 29). It
/// is never assumed: the finding names the override when it is set.
/// </param>
public sealed record SceneVideoCompilationInput(
    string StyleDeclaration,
    string SceneDescription,
    IReadOnlyList<SceneVideoReference> References,
    IReadOnlyList<SceneVideoShot> Shots,
    IReadOnlyList<SceneVideoDialogueLine> Dialogue,
    IReadOnlyList<SceneVideoOnScreenText> OnScreenText,
    string Soundscape,
    string NonDiegeticMusic,
    SceneVideoContentRegister ContentRegister,
    int FrameLength,
    int Fps,
    MiniMaxH3FramePolicy FramePolicy,
    int MinWordCount,
    int MaxWordCount,
    string? VideoVaeName,
    string? AudioVaeName,
    string? DeclaredTimelineEnd = null,
    bool AllowUntrainedLength = false);

/// <summary>
/// One rule's result.
///
/// <para>
/// <b>Blocking</b> findings are the rules a document or graph must satisfy to be worth rendering at all: a malformed
/// section (structure, labels, slot order, retention vocabulary, cut times, camera vocabulary, language tags,
/// frame length, VAE binding, negative phrasing, quote and continuation markers, on-screen quoting, sheet
/// numbering). A blocking failure stops queueing.
/// </para>
/// <para>
/// <b>Advisory</b> findings are QUALITY targets derived from the H3 guidance: how long the body should be, whether a
/// style sentence was supplied, how the soundscape and score are phrased, how richly a speaker is identified, and
/// the prose heuristics (abstract-only wording, plot-summary drift, declared timeline end). They are shown plainly
/// and never stop the operator queueing: an unrenderable document is a bug, while a thin description is a choice.
/// </para>
/// </summary>
/// <param name="RuleId">Stable rule id (R01..R33).</param>
/// <param name="Section">The document section the rule governs.</param>
/// <param name="Passed">Whether the rule is satisfied.</param>
/// <param name="IsAdvisory">True when a failure is a quality target rather than a blocker.</param>
/// <param name="Message">What the rule requires, or what was found.</param>
public sealed record SceneVideoValidationFinding(
    string RuleId,
    string Section,
    bool Passed,
    bool IsAdvisory,
    string Message)
{
    /// <summary>True when this finding must stop the composition from queueing.</summary>
    public bool BlocksQueueing => !Passed && !IsAdvisory;
}

/// <summary>
/// The compiled document plus every rule's result. Queueing is blocked only while a BLOCKING rule fails, so a
/// quality target can never hold a ~25 minute render hostage.
/// </summary>
public sealed record SceneVideoCompilationResult(
    string Prompt,
    IReadOnlyList<SceneVideoValidationFinding> Findings,
    string CompilerKey,
    string CompilerVersion)
{
    /// <summary>True when no blocking rule fails; advisory findings do not make a composition invalid.</summary>
    public bool IsValid => Findings.All(finding => !finding.BlocksQueueing);

    /// <summary>The failures that stop queueing.</summary>
    public IReadOnlyList<SceneVideoValidationFinding> BlockingFailures =>
        Findings.Where(finding => finding.BlocksQueueing).ToList();

    /// <summary>The quality targets that were not met. Visible, never blocking.</summary>
    public IReadOnlyList<SceneVideoValidationFinding> Advisories =>
        Findings.Where(finding => !finding.Passed && finding.IsAdvisory).ToList();

    /// <summary>Every failed rule, blocking or advisory (for diagnostics).</summary>
    public IReadOnlyList<SceneVideoValidationFinding> Failures =>
        Findings.Where(finding => !finding.Passed).ToList();
}
