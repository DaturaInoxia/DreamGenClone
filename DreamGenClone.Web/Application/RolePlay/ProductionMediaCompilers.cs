using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class PonyProductionMediaCompiler : ProductionMediaCompilerBase
{
    public override ProductionMediaCompilerDescriptor Descriptor { get; } = new("pony-v6", "1", MediaOperation.Generate);

    protected override JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings)
    {
        var qualityTags = RequiredString(settings, "qualityTags");
        if (qualityTags != "score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up")
            throw new InvalidOperationException("Pony qualityTags must be the complete qualified Pony V6 quality string.");
        var rating = RequiredString(settings, "ratingTag");
        if (rating is not ("rating_safe" or "rating_questionable" or "rating_explicit"))
            throw new InvalidOperationException("Pony ratingTag must be explicit and valid.");
        var actorCount = ActorCount(input.Intent);
        var countTag = actorCount switch
        {
            1 => "1person",
            2 => "2people",
            _ => $"{actorCount}people"
        };
        var visualTags = VisualTerms(input.Intent);
        var prompt = string.Join(", ", new[] { qualityTags, rating, countTag }.Concat(visualTags));
        if (prompt.Length > 800) throw new InvalidOperationException("Pony prompt exceeds the qualified 800-character limit.");
        if (RequiredInt(settings, "steps", 1) != 25 || RequiredDouble(settings, "guidance") != 7.0
            || RequiredString(settings, "sampler") != "euler_ancestral"
            || RequiredInt(settings, "clipSkip", 1) != 2)
            throw new InvalidOperationException("Pony V6 requires the qualified 25-step, CFG-7, Euler ancestral, CLIP-skip-2 recipe.");
        return CommonRequest(input, settings, prompt, includeNegative: true, extra: new JsonObject
        {
            ["sampler"] = RequiredString(settings, "sampler"),
            ["scheduler"] = RequiredString(settings, "scheduler"),
            ["clipSkip"] = RequiredInt(settings, "clipSkip", minimum: 1)
        });
    }
}

public sealed class SdxlProductionMediaCompiler : ProductionMediaCompilerBase
{
    public override ProductionMediaCompilerDescriptor Descriptor { get; } = new("sdxl-photographic", "1", MediaOperation.Generate);

    protected override JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings)
    {
        var prompt = NaturalLanguagePrompt(input.Intent);
        if (prompt.Length > 800) throw new InvalidOperationException("SDXL prompt exceeds the qualified 800-character limit.");
        var steps = RequiredInt(settings, "steps", 1);
        var guidance = RequiredDouble(settings, "guidance");
        if (steps is < 30 or > 40 || guidance is < 3 or > 6)
            throw new InvalidOperationException("SDXL requires 30-40 steps and CFG 3-6.");
        if (input.CapabilityProfile.ModelId.Contains("biglust", StringComparison.OrdinalIgnoreCase) && guidance is < 3.5 or > 5)
            throw new InvalidOperationException("BigLust requires CFG 3.5-5.");
        return CommonRequest(input, settings, prompt, includeNegative: true, extra: new JsonObject
        {
            ["sampler"] = RequiredString(settings, "sampler"),
            ["scheduler"] = RequiredString(settings, "scheduler")
        });
    }
}

public sealed class Flux2GenerationProductionMediaCompiler : ProductionMediaCompilerBase
{
    public override ProductionMediaCompilerDescriptor Descriptor { get; } = new("flux2-generate", "1", MediaOperation.Generate);

    protected override JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings) =>
        BuildFluxRequest(input, settings, includeReferences: false);

    internal static JsonObject BuildFluxRequest(
        ProductionMediaCompilationInput input, JsonElement settings, bool includeReferences)
    {
        RejectPropertyRecursive(settings, "negative_prompt");
        RejectPropertyRecursive(settings, "negativePrompt");
        var variant = RequiredString(settings, "variant");
        if (variant is not ("pro" or "max" or "flex" or "dev"))
            throw new InvalidOperationException("FLUX.2 variant must be pro, max, flex, or dev.");
        var endpoint = RequiredString(settings, "endpoint");
        if (endpoint.Contains("preview", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FLUX.2 production profiles require a pinned non-preview endpoint.");
        var width = RequiredInt(settings, "width", 64);
        var height = RequiredInt(settings, "height", 64);
        if (width % 16 != 0 || height % 16 != 0 || (long)width * height > 4_000_000)
            throw new InvalidOperationException("FLUX.2 dimensions must be multiples of 16 and no more than 4MP.");

        var request = new JsonObject
        {
            ["model"] = input.CapabilityProfile.ModelId,
            ["endpoint"] = endpoint,
            ["prompt"] = StructuredFluxPrompt(input.Intent, input.ReferenceBindings),
            ["width"] = width,
            ["height"] = height,
            ["seed"] = RequiredLong(settings, "seed"),
            ["output_format"] = RequiredString(settings, "outputFormat")
        };
        if (variant is "flex" or "dev")
        {
            var steps = RequiredInt(settings, "steps", 1);
            if (variant == "flex" && steps > 50) throw new InvalidOperationException("FLUX.2 flex steps cannot exceed 50.");
            var guidance = RequiredDouble(settings, "guidance");
            if (variant == "flex" && (guidance < 1.5 || guidance > 10))
                throw new InvalidOperationException("FLUX.2 flex guidance must be between 1.5 and 10.");
            request["steps"] = steps;
            request["guidance"] = guidance;
        }
        if (includeReferences)
        {
            if (input.ReferenceBindings.Count == 0)
                throw new InvalidOperationException("FLUX.2 edit requires ordered reference bindings.");
            request["reference_images"] = ReferenceArray(input.ReferenceBindings);
        }
        return request;
    }
}

public sealed class Flux2EditProductionMediaCompiler : ProductionMediaCompilerBase
{
    public override ProductionMediaCompilerDescriptor Descriptor { get; } = new("flux2-edit", "1", MediaOperation.Edit);

    protected override JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings) =>
        Flux2GenerationProductionMediaCompiler.BuildFluxRequest(input, settings, includeReferences: true);
}

public sealed class QwenImage2512ProductionMediaCompiler : ProductionMediaCompilerBase
{
    private static readonly HashSet<(int Width, int Height)> OfficialDimensions =
    [
        (1328, 1328), (1664, 928), (928, 1664), (1472, 1104),
        (1104, 1472), (1584, 1056), (1056, 1584)
    ];

    public override ProductionMediaCompilerDescriptor Descriptor { get; } = new("qwen-image-2512", "1", MediaOperation.Generate);

    protected override JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings)
    {
        if (!string.Equals(input.CapabilityProfile.ModelId, "Qwen/Qwen-Image-2512", StringComparison.Ordinal))
            throw new InvalidOperationException("Qwen Image generation requires exact model Qwen/Qwen-Image-2512.");
        var width = RequiredInt(settings, "width", 1);
        var height = RequiredInt(settings, "height", 1);
        if (!OfficialDimensions.Contains((width, height)))
            throw new InvalidOperationException("Qwen Image 2512 dimensions are outside the official qualified set.");
        if (RequiredInt(settings, "steps", 1) != 50 || RequiredDouble(settings, "trueCfgScale") != 4.0)
            throw new InvalidOperationException("Qwen Image 2512 profile must use the official 50-step, true-CFG-4 recipe.");
        return new JsonObject
        {
            ["model"] = input.CapabilityProfile.ModelId,
            ["pipeline"] = "QwenImagePipeline",
            ["prompt"] = NaturalLanguagePrompt(input.Intent),
            ["negative_prompt"] = RequiredString(settings, "negativePrompt"),
            ["width"] = width,
            ["height"] = height,
            ["num_inference_steps"] = 50,
            ["true_cfg_scale"] = 4.0,
            ["seed"] = RequiredLong(settings, "seed")
        };
    }
}

public sealed class QwenImageEdit2511ProductionMediaCompiler : ProductionMediaCompilerBase
{
    public override ProductionMediaCompilerDescriptor Descriptor { get; } = new("qwen-image-edit-2511", "1", MediaOperation.Edit);

    protected override JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings)
    {
        if (!string.Equals(input.CapabilityProfile.ModelId, "Qwen/Qwen-Image-Edit-2511", StringComparison.Ordinal))
            throw new InvalidOperationException("Qwen Image editing requires exact model Qwen/Qwen-Image-Edit-2511.");
        if (input.ReferenceBindings.Count == 0)
            throw new InvalidOperationException("Qwen Image Edit 2511 requires ordered image references.");
        if (RequiredInt(settings, "steps", 1) != 40
            || RequiredDouble(settings, "trueCfgScale") != 4.0
            || RequiredDouble(settings, "guidanceScale") != 1.0
            || RequiredInt(settings, "numberOfImages", 1) != 1)
        {
            throw new InvalidOperationException("Qwen Image Edit 2511 profile must use the official 40-step, true-CFG-4, guidance-1, one-output recipe.");
        }
        return new JsonObject
        {
            ["model"] = input.CapabilityProfile.ModelId,
            ["pipeline"] = "QwenImageEditPlusPipeline",
            ["prompt"] = EditPrompt(input.Intent, input.ReferenceBindings),
            ["negative_prompt"] = RequiredStringAllowEmpty(settings, "negativePrompt"),
            ["images"] = ReferenceArray(input.ReferenceBindings),
            ["num_inference_steps"] = 40,
            ["true_cfg_scale"] = 4.0,
            ["guidance_scale"] = 1.0,
            ["num_images_per_prompt"] = 1,
            ["seed"] = RequiredLong(settings, "seed")
        };
    }
}

public abstract class ProductionMediaCompilerBase : IProductionMediaCompiler
{
    public abstract ProductionMediaCompilerDescriptor Descriptor { get; }

    public ProductionMediaCompilation Compile(ProductionMediaCompilationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);
        using var settingsDocument = ParseObject(input.SettingsJson, "Production compiler settings");
        RejectSecrets(settingsDocument.RootElement);
        var requestJson = BuildProviderRequest(input, settingsDocument.RootElement).ToJsonString();
        using var requestDocument = JsonDocument.Parse(requestJson);
        RejectSecrets(requestDocument.RootElement);
        var bindings = input.ReferenceBindings.OrderBy(binding => binding.Ordinal).ToList();
        var request = new CompiledMediaRequest
        {
            Id = input.RequestId.Trim(), IntentSnapshotId = input.Intent.Id,
            CapabilityProfileId = input.CapabilityProfile.Id, CapabilityCellId = input.CapabilityCell.Id,
            CompilerId = Descriptor.CompilerId, CompilerVersion = Descriptor.CompilerVersion,
            RequestSchemaVersion = "production-media-request-v1", ProviderKey = input.CapabilityProfile.ProviderKey,
            ModelId = input.CapabilityProfile.ModelId, ModelVersion = input.CapabilityProfile.ModelVersion,
            WorkflowRevision = input.CapabilityProfile.WorkflowRevision,
            CanonicalProviderRequestJson = requestJson,
            ValidationResultJson = "{\"ready\":true,\"unsupported\":[],\"warnings\":[]}",
            CreatedUtc = input.CreatedUtc
        };
        request.ContentHash = ProductionContentHash.ForCompiledRequest(request, bindings);
        return new ProductionMediaCompilation(request, bindings);
    }

    protected abstract JsonObject BuildProviderRequest(ProductionMediaCompilationInput input, JsonElement settings);

    protected static JsonObject CommonRequest(
        ProductionMediaCompilationInput input, JsonElement settings, string prompt, bool includeNegative, JsonObject extra)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("Compiled visual prompt is empty.");
        var request = new JsonObject
        {
            ["model"] = input.CapabilityProfile.ModelId,
            ["prompt"] = prompt,
            ["width"] = RequiredInt(settings, "width", 1),
            ["height"] = RequiredInt(settings, "height", 1),
            ["steps"] = RequiredInt(settings, "steps", 1),
            ["guidance"] = RequiredDouble(settings, "guidance"),
            ["seed"] = RequiredLong(settings, "seed")
        };
        if (includeNegative) request["negative_prompt"] = RequiredStringAllowEmpty(settings, "negativePrompt");
        foreach (var property in extra) request[property.Key] = property.Value?.DeepClone();
        return request;
    }

    protected static string NaturalLanguagePrompt(ProductionIntentSnapshot intent) =>
        string.Join(" ", VisualTerms(intent).Select(term => term.Trim().TrimEnd('.') + "."));

    protected static IReadOnlyList<string> VisualTerms(ProductionIntentSnapshot intent)
    {
        var values = new List<string>();
        foreach (var (json, label) in new[]
        {
            (intent.VisibleActorsJson, "visible actors"), (intent.CompositionIntentJson, "composition intent"),
            (intent.CameraIntentJson, "camera intent"), (intent.StyleIntentJson, "style intent")
        })
        {
            using var document = ParseJson(json, label);
            CollectStrings(document.RootElement, values);
        }
        if (values.Count == 0) throw new InvalidOperationException("Production visual intent contains no renderable terms.");
        return values;
    }

    protected static JsonObject StructuredFluxPrompt(
        ProductionIntentSnapshot intent, IReadOnlyList<OrderedMediaReferenceBinding> bindings) => new()
    {
        ["subjects"] = ParseNode(intent.VisibleActorsJson, "visible actors"),
        ["composition"] = ParseNode(intent.CompositionIntentJson, "composition intent"),
        ["camera"] = ParseNode(intent.CameraIntentJson, "camera intent"),
        ["style"] = ParseNode(intent.StyleIntentJson, "style intent"),
        ["reference_roles"] = new JsonArray(bindings.OrderBy(binding => binding.Ordinal)
            .Select(binding => (JsonNode)new JsonObject
            {
                ["image"] = $"image {binding.Ordinal + 1}",
                ["role"] = binding.SemanticRole,
                ["actor"] = binding.ActorKey
            }).ToArray())
    };

    protected static string EditPrompt(
        ProductionIntentSnapshot intent, IReadOnlyList<OrderedMediaReferenceBinding> bindings)
    {
        var references = string.Join("; ", bindings.OrderBy(binding => binding.Ordinal)
            .Select(binding => $"image {binding.Ordinal + 1}: {binding.SemanticRole}" +
                (string.IsNullOrWhiteSpace(binding.ActorKey) ? string.Empty : $" for actor {binding.ActorKey}")));
        var changes = StringsFromJson(intent.ChangeIntentJson, "change intent");
        var preserve = StringsFromJson(intent.PreservationConstraintsJson, "preservation constraints");
        if (changes.Count == 0 || preserve.Count == 0)
            throw new InvalidOperationException("An edit intent requires explicit changes and preservation constraints.");
        return $"References: {references}. Change: {string.Join("; ", changes)}. Preserve: {string.Join("; ", preserve)}.";
    }

    protected static JsonArray ReferenceArray(IReadOnlyList<OrderedMediaReferenceBinding> bindings) =>
        new(bindings.OrderBy(binding => binding.Ordinal).Select(binding => (JsonNode)new JsonObject
        {
            ["ordinal"] = binding.Ordinal,
            ["scene_asset_id"] = binding.SceneAssetId,
            ["sha256"] = binding.SceneAssetSha256,
            ["role"] = binding.SemanticRole,
            ["actor"] = binding.ActorKey
        }).ToArray());

    protected static int ActorCount(ProductionIntentSnapshot intent)
    {
        using var document = ParseJson(intent.VisibleActorsJson, "visible actors");
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Visible actors must be a JSON array.");
        var count = document.RootElement.GetArrayLength();
        if (count <= 0) throw new InvalidOperationException("At least one visible actor is required.");
        return count;
    }

    protected static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Production compiler setting '{name}' is required.");
        return value.GetString()!.Trim();
    }

    protected static string RequiredStringAllowEmpty(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Production compiler setting '{name}' is required and must be a string.");
        return value.GetString()!;
    }

    protected static int RequiredInt(JsonElement parent, string name, int minimum)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result < minimum)
            throw new InvalidOperationException($"Production compiler setting '{name}' must be an integer of at least {minimum}.");
        return result;
    }

    protected static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidOperationException($"Production compiler setting '{name}' must be an integer.");
        return result;
    }

    protected static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result)
            || double.IsNaN(result) || double.IsInfinity(result))
            throw new InvalidOperationException($"Production compiler setting '{name}' must be a finite number.");
        return result;
    }

    protected static void RejectPropertyRecursive(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"FLUX.2 does not support '{property.Name}'.");
                RejectPropertyRecursive(property.Value, propertyName);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectPropertyRecursive(item, propertyName);
        }
    }

    private void ValidateInput(ProductionMediaCompilationInput input)
    {
        if (string.IsNullOrWhiteSpace(input.RequestId)) throw new InvalidOperationException("Compiled request id is required.");
        if (input.CreatedUtc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Compilation time must be UTC.");
        if (input.Intent.Operation != Descriptor.Operation || input.CapabilityProfile.Operation != Descriptor.Operation)
            throw new InvalidOperationException("Intent, profile, and compiler operations must match exactly.");
        if (!string.Equals(input.CapabilityProfile.CompilerId, Descriptor.CompilerId, StringComparison.Ordinal)
            || !string.Equals(input.CapabilityProfile.CompilerVersion, Descriptor.CompilerVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Capability profile does not exactly match the selected compiler identity.");
        if (!string.Equals(input.CapabilityCell.CapabilityProfileId, input.CapabilityProfile.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Capability cell does not belong to the selected profile.");
        if (input.CapabilityProfile.Status != MediaCapabilityProfileStatus.Qualified || !input.CapabilityProfile.Enabled)
            throw new InvalidOperationException("Production compilation requires an enabled qualified capability profile.");
        if (input.CapabilityCell.Status != MediaCapabilityCellStatus.Qualified)
            throw new InvalidOperationException("Production compilation requires an exact qualified capability cell.");
        if (input.CapabilityCell.ActorCount != ActorCount(input.Intent))
            throw new InvalidOperationException("Capability cell actor count does not match the production intent.");
        var ordered = input.ReferenceBindings.OrderBy(binding => binding.Ordinal).ToList();
        if (ordered.Select(binding => binding.Ordinal).Distinct().Count() != ordered.Count
            || ordered.Where((binding, index) => binding.Ordinal != index).Any())
            throw new InvalidOperationException("Reference binding ordinals must be unique and contiguous from zero.");
        foreach (var binding in ordered)
        {
            if (!string.Equals(binding.CompiledRequestId, input.RequestId, StringComparison.Ordinal))
                throw new InvalidOperationException("Reference binding request ownership does not match the compiled request.");
            if (string.IsNullOrWhiteSpace(binding.SemanticRole) || string.IsNullOrWhiteSpace(binding.SceneAssetId)
                || binding.SceneAssetVersion <= 0 || binding.SceneAssetSha256.Length != 64)
                throw new InvalidOperationException("Every production reference requires exact role, asset, version, and checksum.");
        }
    }

    private static IReadOnlyList<string> StringsFromJson(string json, string label)
    {
        using var document = ParseJson(json, label);
        var values = new List<string>();
        CollectStrings(document.RootElement, values);
        return values;
    }

    private static void CollectStrings(JsonElement element, List<string> values)
    {
        if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
            values.Add(element.GetString()!.Trim());
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectStrings(item, values);
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) CollectStrings(property.Value, values);
    }

    private static JsonNode ParseNode(string json, string label)
    {
        try { return JsonNode.Parse(json) ?? throw new InvalidOperationException($"{label} cannot be null JSON."); }
        catch (JsonException exception) { throw new InvalidOperationException($"{label} must be valid JSON.", exception); }
    }

    private static JsonDocument ParseJson(string json, string label)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException exception) { throw new InvalidOperationException($"{label} must be valid JSON.", exception); }
    }

    private static JsonDocument ParseObject(string json, string label)
    {
        var document = ParseJson(json, label);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidOperationException($"{label} must be a JSON object.");
        }
        return document;
    }

    private static void RejectSecrets(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "apiKey" or "api_key" or "authorization" or "accessToken" or "secret")
                    throw new InvalidOperationException($"Compiled provider request cannot contain secret field '{property.Name}'.");
                RejectSecrets(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectSecrets(item);
    }
}

public sealed class ProductionMediaCompilerRegistry : IProductionMediaCompilerRegistry
{
    private readonly IReadOnlyList<IProductionMediaCompiler> _compilers;

    public ProductionMediaCompilerRegistry(IEnumerable<IProductionMediaCompiler> compilers) =>
        _compilers = compilers.ToList();

    public IProductionMediaCompiler Resolve(MediaCapabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var matches = _compilers.Where(compiler =>
            string.Equals(compiler.Descriptor.CompilerId, profile.CompilerId, StringComparison.Ordinal)
            && string.Equals(compiler.Descriptor.CompilerVersion, profile.CompilerVersion, StringComparison.Ordinal)
            && compiler.Descriptor.Operation == profile.Operation).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No production compiler exactly matches '{profile.CompilerId}' version '{profile.CompilerVersion}' for {profile.Operation}."),
            _ => throw new InvalidOperationException($"Multiple production compilers match '{profile.CompilerId}' version '{profile.CompilerVersion}' for {profile.Operation}.")
        };
    }
}

public sealed class ProductionMediaCompilationService : IProductionMediaCompilationService
{
    private static readonly JsonSerializerOptions IdentitySnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IProductionMediaRepository _repository;
    private readonly IProductionMediaCompilerRegistry _registry;
    private readonly ICharacterLoraRepository _loraRepository;
    private readonly IRegisteredModelRepository _modelRepository;

    public ProductionMediaCompilationService(
        IProductionMediaRepository repository,
        IProductionMediaCompilerRegistry registry,
        ICharacterLoraRepository loraRepository,
        IRegisteredModelRepository modelRepository)
    {
        _repository = repository;
        _registry = registry;
        _loraRepository = loraRepository;
        _modelRepository = modelRepository;
    }

    public async Task<ProductionMediaCompilation> CompileAndPersistAsync(
        string requestId,
        string intentId,
        string capabilityProfileId,
        string capabilityCellId,
        string settingsJson,
        IReadOnlyList<OrderedMediaReferenceBinding> referenceBindings,
        DateTime createdUtc,
        CancellationToken cancellationToken = default)
    {
        var intent = await _repository.GetIntentAsync(intentId, cancellationToken)
            ?? throw new InvalidOperationException($"Production intent '{intentId}' was not found.");
        var profile = await _repository.GetCapabilityProfileAsync(capabilityProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Media capability profile '{capabilityProfileId}' was not found.");
        var cell = (await _repository.ListCapabilityCellsAsync(profile.Id, cancellationToken))
            .SingleOrDefault(candidate => string.Equals(candidate.Id, capabilityCellId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Media capability cell '{capabilityCellId}' was not found in profile '{profile.Id}'.");
        var compiler = _registry.Resolve(profile);
        var result = compiler.Compile(new ProductionMediaCompilationInput(
            requestId, intent, profile, cell, settingsJson, referenceBindings, createdUtc));
        await _repository.CreateCompiledRequestAsync(result.Request, result.ReferenceBindings, cancellationToken);
        return result;
    }

    public async Task<ProductionMediaCompilation> CompileIdentityAndPersistAsync(
        string requestId,
        string intentId,
        string capabilityProfileId,
        string capabilityCellId,
        string settingsJson,
        IReadOnlyList<OrderedMediaReferenceBinding> referenceBindings,
        IReadOnlyList<IdentityStrategyBinding> identityBindings,
        DateTime createdUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identityBindings);
        var intent = await _repository.GetIntentAsync(intentId, cancellationToken)
            ?? throw new InvalidOperationException($"Production intent '{intentId}' was not found.");
        var profile = await _repository.GetCapabilityProfileAsync(capabilityProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Media capability profile '{capabilityProfileId}' was not found.");
        var cell = (await _repository.ListCapabilityCellsAsync(profile.Id, cancellationToken))
            .SingleOrDefault(candidate => string.Equals(candidate.Id, capabilityCellId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Media capability cell '{capabilityCellId}' was not found in profile '{profile.Id}'.");
        await ValidateIdentityBindingsAsync(requestId, profile, cell, identityBindings, cancellationToken);

        var compiler = _registry.Resolve(profile);
        var result = compiler.Compile(new ProductionMediaCompilationInput(
            requestId, intent, profile, cell, settingsJson, referenceBindings, createdUtc));
        result.Request.IdentityStrategySnapshotJson = JsonSerializer.Serialize(
            identityBindings.OrderBy(binding => binding.ActorKey, StringComparer.Ordinal), IdentitySnapshotOptions);
        result.Request.ContentHash = ProductionContentHash.ForCompiledRequest(result.Request, result.ReferenceBindings);
        await _loraRepository.EnsureSchemaAsync(cancellationToken);
        await _repository.CreateIdentityCompiledRequestAsync(
            result.Request, result.ReferenceBindings, identityBindings, cancellationToken);
        return result;
    }

    private async Task ValidateIdentityBindingsAsync(
        string requestId,
        MediaCapabilityProfile profile,
        MediaCapabilityCell cell,
        IReadOnlyList<IdentityStrategyBinding> bindings,
        CancellationToken cancellationToken)
    {
        if (bindings.Count == 0)
            throw new InvalidOperationException("Identity compilation requires an explicit strategy binding for every actor.");
        if (cell.IdentityStrategyKind is not { } cellStrategy)
            throw new InvalidOperationException($"Capability cell '{cell.Id}' does not qualify an identity strategy.");
        if (string.IsNullOrWhiteSpace(profile.RegisteredModelId))
            throw new InvalidOperationException($"Model capability profile '{profile.Id}' is not linked to a Model Manager model.");
        var registeredModel = await _modelRepository.GetByIdAsync(profile.RegisteredModelId, cancellationToken)
            ?? throw new InvalidOperationException($"Registered model '{profile.RegisteredModelId}' was not found.");
        if (!registeredModel.IsEnabled)
            throw new InvalidOperationException($"Registered model '{registeredModel.Id}' is disabled.");
        if (!string.Equals(registeredModel.ModelIdentifier, profile.ModelId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Capability profile model '{profile.ModelId}' does not match registered model '{registeredModel.ModelIdentifier}'.");
        var modelStrategies = JsonSerializer.Deserialize<string[]>(registeredModel.SupportedIdentityStrategiesJson)
            ?? throw new InvalidOperationException("Registered model identity strategy declaration must be a JSON array.");
        if (!modelStrategies.Any(value => string.Equals(value, cellStrategy.ToString(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Registered model '{registeredModel.Id}' does not declare strategy '{cellStrategy}'.");
        var declared = JsonSerializer.Deserialize<string[]>(profile.SupportedIdentityStrategiesJson)
            ?? throw new InvalidOperationException("Capability profile identity strategy declaration must be a JSON array.");
        if (!declared.Any(value => string.Equals(value, cellStrategy.ToString(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Model capability profile '{profile.Id}' does not declare strategy '{cellStrategy}'.");
        if (bindings.Select(binding => binding.ActorKey).Distinct(StringComparer.Ordinal).Count() != bindings.Count)
            throw new InvalidOperationException("Identity strategy actor bindings must be unique.");

        foreach (var binding in bindings)
        {
            if (!string.Equals(binding.CompiledRequestId, requestId, StringComparison.Ordinal)
                || !string.Equals(binding.CapabilityProfileId, profile.Id, StringComparison.Ordinal)
                || !string.Equals(binding.CapabilityCellId, cell.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Identity strategy binding does not own the exact request, profile, and cell.");
            if (binding.StrategyKind != cellStrategy)
                throw new InvalidOperationException(
                    $"Identity strategy '{binding.StrategyKind}' is not qualified by selected cell '{cell.Id}' ({cellStrategy}).");
            if (binding.StrategyKind is CharacterIdentityStrategyKind.Lora or CharacterIdentityStrategyKind.Combined)
            {
                var artifact = await _loraRepository.GetArtifactAsync(binding.LoraArtifactId!, cancellationToken)
                    ?? throw new InvalidOperationException($"LoRA artifact '{binding.LoraArtifactId}' was not found.");
                if (artifact.Status != CharacterLoraArtifactStatus.Qualified
                    || !string.Equals(artifact.Sha256, binding.LoraArtifactSha256, StringComparison.Ordinal)
                    || !string.Equals(artifact.BaseModelId, profile.ModelId, StringComparison.Ordinal)
                    || !string.Equals(artifact.BaseModelVersion, profile.ModelVersion, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The selected LoRA artifact is not qualified for the exact model/version and checksum.");
            }
        }
    }
}