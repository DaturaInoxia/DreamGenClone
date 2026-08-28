using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageStorageServiceTests
{
    [Fact]
    public async Task Save_Open_Delete_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scene-images-{Guid.NewGuid():N}");
        var service = new SceneImageStorageService(
            Options.Create(new PersistenceOptions { SceneImageRoot = root }),
            NullLogger<SceneImageStorageService>.Instance);

        try
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            await using var input = new MemoryStream(bytes);
            var relativePath = await service.SaveAsync("session-1", "image-1.png", input);

            Assert.Equal("session-1/image-1.png", relativePath);
            Assert.True(File.Exists(Path.Combine(root, relativePath)));

            {
                await using var opened = await service.OpenReadAsync(relativePath);
                using var buffer = new MemoryStream();
                await opened.CopyToAsync(buffer);
                Assert.Equal(bytes, buffer.ToArray());
            } // dispose the read handle before delete

            await service.DeleteAsync(relativePath);
            Assert.False(File.Exists(Path.Combine(root, relativePath)));

            // Idempotent delete must not throw.
            await service.DeleteAsync(relativePath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Save_SanitizesSessionSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scene-images-{Guid.NewGuid():N}");
        var service = new SceneImageStorageService(
            Options.Create(new PersistenceOptions { SceneImageRoot = root }),
            NullLogger<SceneImageStorageService>.Instance);

        try
        {
            await using var input = new MemoryStream(new byte[] { 9 });
            // Non-alphanumeric session id is sanitized.
            var relativePath = await service.SaveAsync("../evil/name", "image.png", input);
            Assert.False(relativePath.Contains("..", StringComparison.Ordinal));
            Assert.EndsWith("/image.png", relativePath, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
