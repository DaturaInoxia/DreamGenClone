using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ProductionMediaCompilerTests
{
    [Fact]
    public void PonyAndSdxl_CompileFamilyNativeGoldenRequests()
    {
        var pony = Compile(new PonyProductionMediaCompiler(), GenerateProfile("pony-v6", "pony-v6-xl"), """
            {"qualityTags":"score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up","ratingTag":"rating_safe","negativePrompt":"lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry","width":1024,"height":1024,"steps":25,"guidance":7,"sampler":"euler_ancestral","scheduler":"normal","clipSkip":2,"seed":42}
            """);
        using var ponyJson = JsonDocument.Parse(pony.Request.CanonicalProviderRequestJson);
        Assert.StartsWith("score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_safe, 1person", ponyJson.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("euler_ancestral", ponyJson.RootElement.GetProperty("sampler").GetString());
        Assert.Equal(2, ponyJson.RootElement.GetProperty("clipSkip").GetInt32());

        var sdxl = Compile(new SdxlProductionMediaCompiler(), GenerateProfile("sdxl-photographic", "bigLust_v16.safetensors"), """
            {"negativePrompt":"deformed, bad anatomy, extra limbs, watermark, text","width":1024,"height":1024,"steps":30,"guidance":5,"sampler":"dpmpp_2m_sde","scheduler":"karras","seed":42}
            """);
        using var sdxlJson = JsonDocument.Parse(sdxl.Request.CanonicalProviderRequestJson);
        Assert.Equal("dpmpp_2m_sde", sdxlJson.RootElement.GetProperty("sampler").GetString());
        Assert.Contains("yellow dress", sdxlJson.RootElement.GetProperty("prompt").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("score_9", sdxl.Request.CanonicalProviderRequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public void FluxGenerate_HasStructuredPromptAndNoNegativeFieldAtAnyDepth()
    {
        var result = Compile(new Flux2GenerationProductionMediaCompiler(), GenerateProfile("flux2-generate", "black-forest-labs/FLUX.2-flex"), """
            {"variant":"flex","endpoint":"flux-2-flex","width":1024,"height":1024,"steps":50,"guidance":4.5,"seed":42,"outputFormat":"png"}
            """);

        using var json = JsonDocument.Parse(result.Request.CanonicalProviderRequestJson);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("prompt").ValueKind);
        Assert.Equal("yellow dress", json.RootElement.GetProperty("prompt").GetProperty("subjects")[0].GetProperty("clothing").GetString());
        AssertNoProperty(json.RootElement, "negative_prompt");
    }

    [Fact]
    public void FluxEdit_PreservesOrderedReferenceRolesAndRejectsNegativeField()
    {
        var profile = EditProfile("flux2-edit", "black-forest-labs/FLUX.2-pro");
        var input = Input(profile, references: References("request-1", "source composition", "identity for actor-a"));
        var compiler = new Flux2EditProductionMediaCompiler();
        var result = compiler.Compile(input with
        {
            SettingsJson = "{\"variant\":\"pro\",\"endpoint\":\"flux-2-pro\",\"width\":1024,\"height\":1024,\"seed\":42,\"outputFormat\":\"png\"}"
        });
        using var json = JsonDocument.Parse(result.Request.CanonicalProviderRequestJson);
        Assert.Equal("source composition", json.RootElement.GetProperty("reference_images")[0].GetProperty("role").GetString());
        Assert.Equal("identity for actor-a", json.RootElement.GetProperty("reference_images")[1].GetProperty("role").GetString());

        Assert.Throws<InvalidOperationException>(() => compiler.Compile(input with
        {
            SettingsJson = "{\"variant\":\"pro\",\"endpoint\":\"flux-2-pro\",\"width\":1024,\"height\":1024,\"seed\":42,\"outputFormat\":\"png\",\"negative_prompt\":\"bad\"}"
        }));
    }

    [Fact]
    public void QwenGenerationAndEdit_UseSeparateExactPipelines()
    {
        var generation = Compile(new QwenImage2512ProductionMediaCompiler(), GenerateProfile("qwen-image-2512", "Qwen/Qwen-Image-2512"), """
            {"negativePrompt":"low quality","width":1328,"height":1328,"steps":50,"trueCfgScale":4,"seed":42}
            """);
        using var generationJson = JsonDocument.Parse(generation.Request.CanonicalProviderRequestJson);
        Assert.Equal("QwenImagePipeline", generationJson.RootElement.GetProperty("pipeline").GetString());
        Assert.Equal(50, generationJson.RootElement.GetProperty("num_inference_steps").GetInt32());

        var editCompiler = new QwenImageEdit2511ProductionMediaCompiler();
        var editProfile = EditProfile("qwen-image-edit-2511", "Qwen/Qwen-Image-Edit-2511");
        var edit = editCompiler.Compile(Input(editProfile, References("request-1", "source composition", "identity for actor-a")) with
        {
            SettingsJson = "{\"negativePrompt\":\" \",\"steps\":40,\"trueCfgScale\":4,\"guidanceScale\":1,\"numberOfImages\":1,\"seed\":0}"
        });
        using var editJson = JsonDocument.Parse(edit.Request.CanonicalProviderRequestJson);
        Assert.Equal("QwenImageEditPlusPipeline", editJson.RootElement.GetProperty("pipeline").GetString());
        Assert.Equal(2, editJson.RootElement.GetProperty("images").GetArrayLength());
        Assert.Contains("Preserve:", editJson.RootElement.GetProperty("prompt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SameFrozenInputs_ProduceSameCanonicalRequestAndHash()
    {
        var compiler = new SdxlProductionMediaCompiler();
        var profile = GenerateProfile("sdxl-photographic", "juggernautXL_ragnarok.safetensors");
        const string settings = "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42}";
        var input = Input(profile) with { SettingsJson = settings };

        var first = compiler.Compile(input);
        var second = compiler.Compile(input);

        Assert.Equal(first.Request.CanonicalProviderRequestJson, second.Request.CanonicalProviderRequestJson);
        Assert.Equal(first.Request.ContentHash, second.Request.ContentHash);
    }

    [Fact]
    public void Registry_RequiresOneExactCompilerAndQualifiedCell()
    {
        var compiler = new SdxlProductionMediaCompiler();
        var profile = GenerateProfile("sdxl-photographic", "juggernautXL_ragnarok.safetensors");
        var registry = new ProductionMediaCompilerRegistry([compiler]);
        Assert.Same(compiler, registry.Resolve(profile));

        Assert.Throws<InvalidOperationException>(() => new ProductionMediaCompilerRegistry([]).Resolve(profile));
        Assert.Throws<InvalidOperationException>(() => new ProductionMediaCompilerRegistry([compiler, compiler]).Resolve(profile));

        var input = Input(profile) with
        {
            CapabilityCell = Cell(profile, MediaCapabilityCellStatus.Rejected),
            SettingsJson = "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42}"
        };
        Assert.Throws<InvalidOperationException>(() => compiler.Compile(input));
    }

    [Fact]
    public void Compilers_RejectMissingSettingsAndSecretFields()
    {
        var compiler = new SdxlProductionMediaCompiler();
        var profile = GenerateProfile("sdxl-photographic", "juggernautXL_ragnarok.safetensors");
        Assert.Throws<InvalidOperationException>(() => compiler.Compile(Input(profile) with { SettingsJson = "{}" }));
        Assert.Throws<InvalidOperationException>(() => compiler.Compile(Input(profile) with
        {
            SettingsJson = "{\"negativePrompt\":\"deformed\",\"width\":1024,\"height\":1024,\"steps\":30,\"guidance\":5,\"sampler\":\"dpmpp_2m_sde\",\"scheduler\":\"karras\",\"seed\":42,\"apiKey\":\"secret\"}"
        }));
    }

    private static ProductionMediaCompilation Compile(
        IProductionMediaCompiler compiler, MediaCapabilityProfile profile, string settings)
    {
        var input = Input(profile) with { SettingsJson = settings };
        return compiler.Compile(input);
    }

    private static ProductionMediaCompilationInput Input(
        MediaCapabilityProfile profile, IReadOnlyList<OrderedMediaReferenceBinding>? references = null)
    {
        var intent = Intent(profile.Operation);
        return new ProductionMediaCompilationInput(
            "request-1", intent, profile, Cell(profile), "{}", references ?? [],
            new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc));
    }

    private static MediaCapabilityProfile GenerateProfile(string compilerId, string modelId) =>
        Profile(compilerId, modelId, MediaOperation.Generate);

    private static MediaCapabilityProfile EditProfile(string compilerId, string modelId) =>
        Profile(compilerId, modelId, MediaOperation.Edit);

    private static MediaCapabilityProfile Profile(string compilerId, string modelId, MediaOperation operation) => new()
    {
        Id = "profile-1", ProviderKey = "test-provider", ModelId = modelId, ModelVersion = "v1",
        Operation = operation, CompilerId = compilerId, CompilerVersion = "1", WorkflowRevision = "workflow-1",
        NodeRevision = "nodes-1", ArtifactManifestJson = "{}", SettingsSchemaJson = "{}",
        ReferenceLayoutJson = "{}", ControlLayoutJson = "{}", ContentPolicyKey = "test-policy",
        Status = MediaCapabilityProfileStatus.Qualified, Enabled = true, EvidenceRunId = "proof-1",
        CreatedUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
    };

    private static MediaCapabilityCell Cell(
        MediaCapabilityProfile profile, MediaCapabilityCellStatus status = MediaCapabilityCellStatus.Qualified) => new()
    {
        Id = "cell-1", CapabilityProfileId = profile.Id, ActorCount = 1,
        FaceAngleKey = "front", CropKey = "medium", PoseClassKey = "standing",
        CompositionClassKey = "single", ReferenceControlTupleJson = "{}", Status = status,
        EvidenceRunId = "proof-1", CreatedUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
    };

    private static ProductionIntentSnapshot Intent(MediaOperation operation) => new()
    {
        Id = "intent-1", ContextKind = ProductionContextKind.SceneMoment,
        ContextId = "session-1", ContextSnapshotJson = "{}",
        ProductionGroupId = "group-1", SessionId = "session-1", CatalogueId = "catalogue-1",
        BeatId = "beat-1", BeatProductionPlanId = "plan-1", BeatProductionPlanVersion = 1,
        MomentSetId = "set-1", MomentSetVersion = 1, MomentId = "moment-1",
        MomentEnrichmentId = "enrichment-1", MomentEnrichmentRevision = 1, Pov = "observer",
        Operation = operation,
        VisibleActorsJson = "[{\"actorKey\":\"actor-a\",\"description\":\"a middle-aged woman with auburn hair\",\"clothing\":\"yellow dress\",\"action\":\"standing beside a window\"}]",
        CompositionIntentJson = "{\"framing\":\"medium portrait\",\"background\":\"rain-lit living room\"}",
        CameraIntentJson = "{\"angle\":\"eye level\",\"lens\":\"35mm\"}",
        StyleIntentJson = "{\"style\":\"photorealistic\",\"lighting\":\"warm natural light\"}",
        PreservationConstraintsJson = "[\"identity\",\"setting\",\"unaffected clothing\"]",
        ChangeIntentJson = "[\"turn the subject toward the window\"]", ContentPolicyJson = "{\"key\":\"test-policy\"}",
        ContentHash = new string('A', 64), CreatedUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
    };

    private static IReadOnlyList<OrderedMediaReferenceBinding> References(string requestId, params string[] roles) =>
        roles.Select((role, index) => new OrderedMediaReferenceBinding
        {
            Id = $"binding-{index}", CompiledRequestId = requestId, Ordinal = index,
            SemanticRole = role, ActorKey = index == 0 ? null : "actor-a", SceneAssetId = $"asset-{index}",
            SceneAssetVersion = 1, SceneAssetSha256 = new string((char)('A' + index), 64),
            BindingSnapshotJson = "{}", CreatedUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
        }).ToList();

    private static void AssertNoProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            Assert.False(element.TryGetProperty(name, out _));
            foreach (var property in element.EnumerateObject()) AssertNoProperty(property.Value, name);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) AssertNoProperty(item, name);
    }
}