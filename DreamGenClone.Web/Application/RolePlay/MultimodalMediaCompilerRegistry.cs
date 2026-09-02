using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class MultimodalMediaCompilerRegistry : IMultimodalMediaCompilerRegistry
{
    private readonly IReadOnlyList<IMultimodalMediaCompiler> _compilers;

    public MultimodalMediaCompilerRegistry(IEnumerable<IMultimodalMediaCompiler> compilers)
    {
        _compilers = compilers.ToList();
    }

    public IMultimodalMediaCompiler Resolve(MediaCompilerTargetProfile targetProfile)
    {
        CompiledMediaContractValidator.ValidateTargetProfile(targetProfile);
        var matches = _compilers.Where(compiler => Matches(compiler.Descriptor, targetProfile)).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No media compiler exactly matches kind '{targetProfile.MediaKind}', family '{targetProfile.FamilyKey}', " +
                $"compiler '{targetProfile.CompilerKey}' version '{targetProfile.CompilerVersion}', and the supplied capability set.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple media compilers exactly match profile '{targetProfile.ProfileId}' version '{targetProfile.ProfileVersion}'.");
        }
        return matches[0];
    }

    private static bool Matches(MediaCompilerDescriptor descriptor, MediaCompilerTargetProfile profile) =>
        descriptor.MediaKind == profile.MediaKind &&
        string.Equals(descriptor.FamilyKey, profile.FamilyKey, StringComparison.Ordinal) &&
        string.Equals(descriptor.CompilerKey, profile.CompilerKey, StringComparison.Ordinal) &&
        string.Equals(descriptor.CompilerVersion, profile.CompilerVersion, StringComparison.Ordinal) &&
        descriptor.Capabilities.SetEquals(profile.Capabilities);
}