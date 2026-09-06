namespace MacroDeckHost.Application.Deck;

public interface IDeviceDeckNavigator
{
	Task<bool> ChangeFolderOnDeviceAsync(Guid deviceId,
		Guid folderId,
		Guid navigationToken,
		CancellationToken cancellationToken);

	Task<bool> ChangeProfileOnDeviceAsync(Guid deviceId, string profileId, CancellationToken cancellationToken);
}
