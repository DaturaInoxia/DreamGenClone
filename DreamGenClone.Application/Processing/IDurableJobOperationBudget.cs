namespace DreamGenClone.Application.Processing;

/// <summary>
/// Optional capability for durable handlers whose execution performs more than one
/// structured-text provider call (e.g. a decomposed multi-pass pipeline such as Beat
/// Production). The durable executor extends its whole-run operation watchdog by this
/// multiplier so a healthy multi-pass run is not cut off after a single provider-timeout
/// window. Handlers that do not implement this keep the default multiplier of 1.
/// </summary>
public interface IDurableJobOperationBudget
{
    /// <summary>
    /// Multiplier applied to the lane provider timeout for the whole-run operation watchdog.
    /// Must be &gt;= 1.
    /// </summary>
    int OperationTimeoutMultiplier { get; }
}
