using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetFoldersRequestMessageHandler : IUiTransportMessageHandler<GetFoldersRequest, GetFoldersResponse>
{
	private readonly IFolderCache _folderCache;
	private readonly IProfileRegistry _profileRegistry;
	private readonly StartupReadiness _readiness;

	public GetFoldersRequestMessageHandler(
		IFolderCache folderCache,
		IProfileRegistry profileRegistry,
		StartupReadiness readiness)
	{
		_folderCache = folderCache;
		_profileRegistry = profileRegistry;
		_readiness = readiness;
	}

	public async ValueTask<GetFoldersResponse> Handle(GetFoldersRequest request, CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; an early read must not
		// observe the still-empty folder cache as "no folders".
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var response = new GetFoldersResponse();

		response.Folders.AddRange(!string.IsNullOrEmpty(request.ProfileId)
			? _profileRegistry.GetFoldersForProfile(request.ProfileId)
			: _folderCache.GetAllFolders().Select(FolderDtoMapper.MapToDto));

		return response;
	}
}
