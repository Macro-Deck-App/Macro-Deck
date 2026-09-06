using MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;
using MacroDeck.Sdk.FolderViews;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

/// <summary>Turns the wire shape of a folder view registration into the SDK descriptor the host works
/// with.</summary>
public static class FolderViewDescriptorMapper
{
	public static FolderViewDescriptor ToDescriptor(FolderViewDescriptorDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new FolderViewDescriptor(dto.Id,
			dto.Name,
			dto.Description,
			// Passed through unnormalized: FolderViewDescriptor.ResolvedNavigation reads an unrecognised
			// value as the default, so a mode a later plugin sends survives the trip and lands on the safe
			// answer rather than being rewritten here.
			dto.Navigation,
			dto.HasConfiguration,
			dto.Metadata);
	}
}
