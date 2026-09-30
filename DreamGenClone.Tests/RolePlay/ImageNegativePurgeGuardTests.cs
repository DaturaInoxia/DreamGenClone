using System.Reflection;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-007 — the structural guard that stops the negative prompt coming back.
///
/// <para>
/// Negatives were purged across this app on 2026-09-08 as VALUES (SDXL/BigLust/Juggernaut/FLUX/Qwen/API all resolve
/// to empty), yet they returned anyway, because the CAPABILITY survived in four places: a compiler interface member,
/// a named constant, a settings field with a UI textbox, and a builder method. Blanking a value is not a purge.
/// </para>
///
/// <para>
/// These tests assert absence at the type level rather than over a value, so a future change cannot re-add one
/// without failing here. The ONE permitted negative is declared on the checkpoint's
/// <see cref="ImageCompilerProfile"/> — asserted with its citation in <c>ImageCompilerProfileTests</c>, which is
/// where the rule now lives.
/// </para>
/// </summary>
public sealed class ImageNegativePurgeGuardTests
{
    /// <summary>
    /// Every member name the purge removed. A type that re-introduces any of them — on an interface, a builder, a
    /// constant — fails this test.
    /// </summary>
    private static readonly string[] PurgedMemberNames =
    [
        "CanonicalNegativePrompt",
        "BuildNegativePrompt",
        "BuildDeterministicBeatNegativePrompt",
        "DefaultNegativePrompt",
        "ResolveNegativeOverride",
        // The body-reference compiler's own verbatim copy of the Pony guard set and its empty SDXL twin. Two sources
        // for one string is exactly how the guard set drifted; both are declared on the checkpoint profile now.
        "PonyNegativeGuard",
        "SdxlNegativePrompt",
    ];

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static IEnumerable<Assembly> ImagePipelineAssemblies() =>
    [
        typeof(ISceneImagePromptCompiler).Assembly,   // DreamGenClone.Web
        typeof(ImageCompilerProfile).Assembly,        // DreamGenClone.Domain
    ];

    [Fact]
    public void NoTypeInTheImagePipelineExposesAPurgedNegativeMember()
    {
        var offenders = ImagePipelineAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type
                .GetMembers(AllMembers)
                .Where(member => PurgedMemberNames.Contains(member.Name, StringComparer.Ordinal))
                .Select(member => $"{type.FullName}.{member.Name}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A negative-prompt member was re-introduced. The negative is declared on the checkpoint's "
            + "ImageCompilerProfile and read from there by the render path; a compiler, builder or constant must not "
            + "carry one. Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void TheCompilerContractCarriesNoNegativeSurface()
    {
        var negativeMembers = typeof(ISceneImagePromptCompiler)
            .GetMembers(AllMembers)
            .Select(member => member.Name)
            .Where(name => name.Contains("Negative", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(negativeMembers.Count == 0,
            "ISceneImagePromptCompiler must not expose anything negative-related; it declares family, dialect and the "
            + "positive-prompt builder only. Found: " + string.Join("; ", negativeMembers));
    }

    [Fact]
    public void TheRenderHandlerResolvesNoNegativeOfItsOwn()
    {
        // Exactly ONE negative-resolution helper is permitted: the one that reads the checkpoint profile. A second
        // helper is the shape that kept regrowing (a settings override, a computed beat negative, a constant), and the
        // handler must be fed by a profile resolver rather than resolving a negative from anything else.
        var handlerType = typeof(SceneImageRenderingJobHandler);
        var negativeHelpers = handlerType
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.Contains("Negative", StringComparison.OrdinalIgnoreCase))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["ResolveNegativePromptAsync"], negativeHelpers);

        // The permitted helper's only source is the profile resolver, which is a required constructor dependency.
        var resolverParameter = handlerType
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .SingleOrDefault(parameter => parameter.ParameterType == typeof(IImageCompilerProfileResolver));

        Assert.NotNull(resolverParameter);
        Assert.True(resolverParameter!.IsOptional is false,
            "IImageCompilerProfileResolver must be a required dependency of the render handler: an optional resolver "
            + "would let the negative silently become empty instead of failing fast.");
    }

    [Fact]
    public void TheProfileIsWhereANegativeIsDeclared()
    {
        // The permitted home. Asserted as a positive so that the purge cannot be "completed" by deleting the concept
        // entirely — Pony's cited guard set is a deliberate, researched exception, not an oversight.
        var negative = typeof(ImageCompilerProfile).GetProperty(nameof(ImageCompilerProfile.Negative));
        var source = typeof(ImageCompilerProfile).GetProperty(nameof(ImageCompilerProfile.NegativeSource));

        Assert.NotNull(negative);
        Assert.NotNull(source);
    }
}
