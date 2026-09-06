namespace MacroDeck.Sdk.Layouts;

/// <summary>The host's identity for a registered layout.</summary>
/// <param name="LayoutId">
/// The qualified id - <c>provider.id::layout-id</c> - that a <c>DeviceDescriptor.LayoutReference</c> must
/// carry to resolve to this layout. The host derives it; a provider never constructs it itself.
/// </param>
/// <param name="ProviderId">The integration or plugin that owns the layout.</param>
public sealed record LayoutRegistration(string LayoutId, string ProviderId);
