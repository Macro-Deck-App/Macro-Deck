using MacroDeck.Plugin.Protocol.Capabilities.Variables;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.VariableValues" />'s
/// <c>value</c> operation. <c>invalidate</c> needs no wrapper - it carries nothing beyond the session
/// itself.</summary>
public sealed record VariableValuesValueArguments
{
	public required IReadOnlyList<VariableIdValueDto> Values { get; init; }
}
