namespace MarkItDown;

/// <summary>
/// Controls how much detail is emitted through <see cref="IProgress{ConversionProgress}"/> callbacks.
/// </summary>
public enum ProgressDetailLevel
{
    /// <summary>
    /// Emits only high-level milestones.
    /// </summary>
    Basic,

    /// <summary>
    /// Emits detailed, step-by-step progress suitable for debugging.
    /// </summary>
    Detailed
}
