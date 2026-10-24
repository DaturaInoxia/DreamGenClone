using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Deriving the camera holder's visible body parts for a participant POV from the moment data: hands/arms/chest/genitals/
/// feet come from the holder's own facts, their continuity state, and the operator's hand-edited prose — never the
/// in-frame subject's body, and never identity (face/head/hair/eyes).
/// </summary>
public sealed class CameraHolderPartsExtractorTests
{
    private const string Snapshot = """
        {"frozenState":{
          "characters":[
            {"profileKey":"p0","characterId":"c0","name":"Becky","actionOrObservation":"Bent forward with her palms flat on the boards","position":"Bent over the workbench facing the boards","sightline":"Down at the boards","clothing":"Nude"},
            {"profileKey":"p1","characterId":"c1","name":"Dean","actionOrObservation":"He stands close behind Becky, his hands gripping her hips","position":"Standing behind Becky, his chest against her back","sightline":"Down at Becky's back","clothing":"Trousers open"}
          ],
          "visualDescription":"Becky is bent over the bench; Dean stands behind her, his erect penis pressed between her thighs",
          "continuityState":"stable"
        },
        "moment":{"visibleAction":"Becky bent over, Dean's hands on her hips","frozenState":"stable"},
        "continuity":{"start":{"characterStates":[{"key":"p1","value":"Dean stands behind Becky, his feet planted"}]}}
        }
        """;

    [Fact]
    public void Extract_ReturnsTheHoldersBodyPartClauses()
    {
        var parts = CameraHolderPartsExtractor.Extract(Snapshot, "p1");

        Assert.NotNull(parts);
        Assert.Contains("his hands gripping her hips", parts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("his chest against her back", parts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("his erect penis", parts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("his feet planted", parts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ReturnsNullForTheInFrameSubjectsBody()
    {
        // Becky's palms/face are the SUBJECT's, not the holder's, so they never come back as camera-holder parts.
        var parts = CameraHolderPartsExtractor.Extract(Snapshot, "p1");

        Assert.DoesNotContain("her palms", parts ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ReturnsNullWhenTheHolderIsUnknown()
    {
        Assert.Null(CameraHolderPartsExtractor.Extract(Snapshot, "absent"));
    }

    [Fact]
    public void Extract_ReturnsNullWhenNoBodyPartIsRecorded()
    {
        const string noParts = """
            {"frozenState":{"characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","actionOrObservation":"Bent forward","position":"Bent over","clothing":"Nude"},
              {"profileKey":"p1","characterId":"c1","name":"Dean","actionOrObservation":"He stands close","position":"Standing behind","clothing":"Trousers open"}
            ],"visualDescription":"Both near the bench"}}
            """;

        Assert.Null(CameraHolderPartsExtractor.Extract(noParts, "p1"));
    }

    [Fact]
    public void Extract_IdentityFactsAreNeverBodyParts()
    {
        const string withIdentity = """
            {"frozenState":{"characters":[
              {"profileKey":"p0","characterId":"c0","name":"Becky","actionOrObservation":"Bent forward","position":"Bent over","clothing":"Nude"},
              {"profileKey":"p1","characterId":"c1","name":"Dean","actionOrObservation":"He stands close, his dark hair falling over his face","position":"Standing behind, his eyes on Becky","clothing":"Trousers open"}
            ],"visualDescription":"Both near the bench"}}
            """;

        // hair/face/eyes are identity and never visible camera-holder parts.
        Assert.Null(CameraHolderPartsExtractor.Extract(withIdentity, "p1"));
    }
}
