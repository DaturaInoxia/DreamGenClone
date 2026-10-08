using System.Text.Json.Nodes;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.ModelManager;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the fail-fast contract of the MiniMax H3 video qualification resolver (B-152, S2).
///
/// <para>
/// The fixture is the qualification the <c>h3-video-configure</c> DbQuery command writes, so this test also proves
/// the SEEDED data satisfies the resolver. Every refusal below is deliberate: a video model missing an artifact
/// name, a frame bound or a loudness target must fail loudly with the field named, because the alternative is a
/// render that runs for 25 minutes and produces something other than what was asked for.
/// </para>
/// </summary>
public sealed class MiniMaxH3ModelSettingsTests
{
    /// <summary>Verbatim from the seeded dev row (dbq: h3-video-configure).</summary>
    private const string SeededQualification =
        """
        [{"Strategy":"MiniMaxH3Ref2VA","EndpointId":"80262aaa-b069-4be3-b6e2-8fb25c9f7520","Qualified":true,
          "ProofId":"b150-h3-16gb-sweep-2026-10-06",
          "DitName":"minimax_h3_ref2va_pruned_w4a8_mixed.safetensors",
          "TextEncoderName":"qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors",
          "TextEncoderDevice":"cpu",
          "VideoVae":"minimax_h3_video_vae_int8_convrot.safetensors",
          "VideoAudioVae":"minimax_h3_audio_vae_fp32.safetensors",
          "SamplerName":"euler","Scheduler":"beta","Steps":40,"Denoise":1.0,"Fps":24,"BitDepth":8,
          "DefaultWidth":1344,"DefaultHeight":768,"DefaultRefImageSize":"match",
          "FrameMinFrames":5,"FrameMaxFrames":3600,"FrameStep":17,
          "TrainedMinFrames":124,"TrainedMaxFrames":362,"WarnAboveFrames":192,
          "PresetShortFrames":124,"PresetLongFrames":192,
          "MaxReferenceImages":9,"MaxReferenceVideos":3,"MaxReferenceAudios":3,
          "LoudnessTargetLufs":-16,"RenderTimeoutSeconds":7200,
          "FfmpegPath":"D:\\src\\DreamGenClone\\.venv\\Lib\\site-packages\\imageio_ffmpeg\\binaries\\ffmpeg-win-x86_64-v7.1.exe",
          "MaxContinuationChainLength":3}]
        """;

    private static RegisteredModel Model(string? qualifications = null) => new()
    {
        Id = "h3-model",
        ProviderId = "provider",
        ModelIdentifier = "minimax_h3_ref2va_pruned_w4a8_mixed.safetensors",
        DisplayName = "MiniMax H3 Ref2VA (Local ComfyUI)",
        ModelKind = ModelKind.Video,
        SceneImageModelFamily = SceneImageModelFamily.MiniMaxH3Ref2VA,
        PromptDialect = SceneImagePromptDialect.MiniMaxH3SixSection,
        CapabilityQualificationsJson = qualifications ?? SeededQualification
    };

    /// <summary>Returns the seeded qualification with one property replaced or removed.</summary>
    private static string QualificationWithout(string property, string? replacement = null)
    {
        var entries = JsonNode.Parse(SeededQualification)!.AsArray();
        var entry = entries[0]!.AsObject();
        if (replacement is null)
        {
            entry.Remove(property);
        }
        else
        {
            entry[property] = JsonNode.Parse(replacement);
        }

        return entries.ToJsonString();
    }

    [Fact]
    public void Resolve_ReadsEverySeededValue()
    {
        var refs = MiniMaxH3ModelSettings.Resolve(Model());

        Assert.Equal("minimax_h3_ref2va_pruned_w4a8_mixed.safetensors", refs.DitName);
        Assert.Equal("qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors", refs.TextEncoderName);
        Assert.Equal("cpu", refs.TextEncoderDevice);
        Assert.Equal("minimax_h3_video_vae_int8_convrot.safetensors", refs.VideoVaeName);
        Assert.Equal("minimax_h3_audio_vae_fp32.safetensors", refs.AudioVaeName);
        Assert.Equal("euler", refs.SamplerName);
        Assert.Equal("beta", refs.Scheduler);
        Assert.Equal(40, refs.Steps);
        Assert.Equal(1.0, refs.Denoise);
        Assert.Equal(24, refs.Fps);
        Assert.Equal(8, refs.BitDepth);
        Assert.Equal(1344, refs.DefaultWidth);
        Assert.Equal(768, refs.DefaultHeight);
        Assert.Equal("match", refs.DefaultRefImageSize);
        Assert.Equal(9, refs.MaxReferenceImages);
        Assert.Equal(3, refs.MaxReferenceVideos);
        Assert.Equal(3, refs.MaxReferenceAudios);
        Assert.Equal(-16, refs.LoudnessTargetLufs);
        Assert.Equal(7200, refs.RenderTimeoutSeconds);
        Assert.Contains("ffmpeg", refs.FfmpegPath);

        // B-156 C-13: the continuation budget is configured, so it resolves like every other required value.
        Assert.Equal(3, refs.MaxContinuationChainLength);

        Assert.Equal(5, refs.FramePolicy.MinFrames);
        Assert.Equal(3600, refs.FramePolicy.MaxFrames);
        Assert.Equal(17, refs.FramePolicy.FrameStep);
        Assert.Equal(124, refs.FramePolicy.TrainedMinFrames);
        Assert.Equal(362, refs.FramePolicy.TrainedMaxFrames);
        Assert.Equal(192, refs.FramePolicy.WarnAboveFrames);
        Assert.Equal(124, refs.FramePolicy.PresetShortFrames);
        Assert.Equal(192, refs.FramePolicy.PresetLongFrames);

        // The whole policy is self-consistent, which is what the graph builder relies on.
        Assert.True(refs.FramePolicy.IsAccepted(refs.FramePolicy.PresetShortFrames));
        Assert.True(refs.FramePolicy.IsAccepted(refs.FramePolicy.PresetLongFrames));
        Assert.True(refs.FramePolicy.IsTrained(refs.FramePolicy.PresetShortFrames));
    }

    [Theory]
    [InlineData("DitName", "DitName")]
    [InlineData("TextEncoderName", "TextEncoderName")]
    [InlineData("VideoVae", "VideoVae")]
    [InlineData("VideoAudioVae", "VideoAudioVae")]
    [InlineData("SamplerName", "SamplerName")]
    [InlineData("Scheduler", "Scheduler")]
    [InlineData("Steps", "Steps")]
    [InlineData("Fps", "Fps")]
    [InlineData("LoudnessTargetLufs", "LoudnessTargetLufs")]
    [InlineData("RenderTimeoutSeconds", "RenderTimeoutSeconds")]
    [InlineData("TrainedMinFrames", "TrainedMinFrames")]
    [InlineData("WarnAboveFrames", "WarnAboveFrames")]
    [InlineData("MaxReferenceImages", "MaxReferenceImages")]
    [InlineData("FfmpegPath", "FfmpegPath")]
    [InlineData("MaxContinuationChainLength", "MaxContinuationChainLength")]
    public void Resolve_RefusesAMissingSetting_NamingIt(string property, string expectedInMessage)
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout(property))));

        Assert.Contains(expectedInMessage, exception.Message);
        Assert.Contains("h3-video-configure", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesAModelWithNoPassingRef2VAQualification()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model("[]")));

        Assert.Contains("MiniMaxH3Ref2VA", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesAQualificationThatIsNotQualified()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("Qualified", "false"))));

        Assert.Contains("no passing", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesATrainedBandOutsideTheNodeRange()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("TrainedMaxFrames", "4000"))));

        Assert.Contains("Trained", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesALengthThatIsNotCongruentToTheNodeStep()
    {
        // 130 is inside 5..3600 but is not 5 (mod 17), so the node would reject it.
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("TrainedMinFrames", "130"))));

        Assert.Contains("TrainedMinFrames", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesAWarnThresholdOutsideTheTrainedBand()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("WarnAboveFrames", "400"))));

        Assert.Contains("WarnAboveFrames", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesARenderBudgetUnderAMinute()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("RenderTimeoutSeconds", "30"))));

        Assert.Contains("RenderTimeoutSeconds", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesACanvasThatIsNotAMultipleOfThirtyTwo()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("DefaultHeight", "770"))));

        Assert.Contains("multiple", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesAnUnknownRefImageSize()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model(QualificationWithout("DefaultRefImageSize", "\"huge\""))));

        Assert.Contains("DefaultRefImageSize", exception.Message);
    }

    [Fact]
    public void Resolve_RefusesInvalidJson()
    {
        var exception = Assert.Throws<ModelResolutionException>(() =>
            MiniMaxH3ModelSettings.Resolve(Model("{not json")));

        Assert.Contains("not valid JSON", exception.Message);
    }
}
