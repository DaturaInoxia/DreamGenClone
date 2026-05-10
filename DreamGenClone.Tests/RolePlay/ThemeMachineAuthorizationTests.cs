using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ThemeMachineAuthorizationTests
{
    [Fact]
    public async Task AuthorizeMutationAsync_AllowsAdminRole()
    {
        var service = new ThemeMachineAuthorizationService(NullLogger<ThemeMachineAuthorizationService>.Instance);

        var result = await service.AuthorizeMutationAsync(new ThemeMachineAuthorizationRequest
        {
            SessionId = "session-1",
            ActorId = "admin-1",
            ActorRole = "Admin",
            Operation = "activate"
        });

        Assert.True(result.Authorized);
        Assert.Contains("authorized", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizeMutationAsync_DeniesNonAdminRole()
    {
        var service = new ThemeMachineAuthorizationService(NullLogger<ThemeMachineAuthorizationService>.Instance);

        var result = await service.AuthorizeMutationAsync(new ThemeMachineAuthorizationRequest
        {
            SessionId = "session-1",
            ActorId = "operator-1",
            ActorRole = "Operator",
            Operation = "migrate"
        });

        Assert.False(result.Authorized);
        Assert.Contains("Admin", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizeMutationAsync_ThrowsWhenOperationMissing()
    {
        var service = new ThemeMachineAuthorizationService(NullLogger<ThemeMachineAuthorizationService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthorizeMutationAsync(new ThemeMachineAuthorizationRequest
            {
                SessionId = "session-1",
                ActorId = "admin-1",
                ActorRole = "Admin",
                Operation = ""
            }));
    }
}
