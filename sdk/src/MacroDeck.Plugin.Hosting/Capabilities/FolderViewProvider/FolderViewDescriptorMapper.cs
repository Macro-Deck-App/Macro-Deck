using MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;
using MacroDeck.Sdk.FolderViews;

namespace MacroDeck.Plugin.Hosting.Capabilities.FolderViewProvider;

/// <summary>Maps between the SDK's folder view descriptor and its wire shape.</summary>
internal static class FolderViewDescriptorMapper
{
	public static FolderViewDescriptorDto ToDto(FolderViewDescriptor folderView)
	{
		ArgumentNullException.ThrowIfNull(folderView);

		return new FolderViewDescriptorDto
		{
			Id = folderView.Id,
			Name = folderView.Name,
			Description = folderView.Description,
			// The resolved value, not the declared one: an unrecognised mode means "default" on both
			// sides, and sending the raw string would make each peer re-derive that separately.
			Navigation = folderView.ResolvedNavigation,
			HasConfiguration = folderView.HasConfiguration,
			Metadata = folderView.Metadata
		};
	}
}
