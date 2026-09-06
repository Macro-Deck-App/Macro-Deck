namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// The addressable size of one key or display in a region, in pixels. Declare it only when the hardware
/// really has a fixed image size; a region that renders at whatever size the client chooses leaves it
/// unset rather than guessing.
/// </summary>
public sealed record LayoutKeySize(int Width, int Height);
