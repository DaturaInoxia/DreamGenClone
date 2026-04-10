namespace DreamGenClone.Application.Abstractions;

public interface IRolePlayDebugEventSink
{
    Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default);
}
