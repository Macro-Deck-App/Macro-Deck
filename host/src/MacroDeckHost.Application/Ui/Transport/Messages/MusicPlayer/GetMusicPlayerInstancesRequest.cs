using MacroDeckHost.Application.MusicPlayer;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;

public class GetMusicPlayerInstancesRequest;

public class MusicPlayerInstanceDto
{
	public string InstanceId { get; set; } = string.Empty;

	public string IntegrationId { get; set; } = string.Empty;

	public LocalizedText ProviderName { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public bool HasIcon { get; set; }

	public static MusicPlayerInstanceDto From(MusicPlayerInstanceDescriptor descriptor)
		=> new()
		{
			InstanceId = descriptor.InstanceId,
			IntegrationId = descriptor.IntegrationId,
			ProviderName = descriptor.ProviderName,
			DisplayName = descriptor.DisplayName,
			HasIcon = descriptor.HasIcon
		};
}

public class GetMusicPlayerInstancesResponse
{
	public List<MusicPlayerInstanceDto> Instances { get; set; } = new();
}
