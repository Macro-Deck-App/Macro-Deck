using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetFolderFocusRulesRequestMessageHandler
	: IUiTransportMessageHandler<GetFolderFocusRulesRequest, GetFolderFocusRulesResponse>
{
	private readonly IFolderCache _folderCache;
	private readonly IDeviceRepository _deviceRepository;

	public GetFolderFocusRulesRequestMessageHandler(IFolderCache folderCache, IDeviceRepository deviceRepository)
	{
		_folderCache = folderCache;
		_deviceRepository = deviceRepository;
	}

	public async ValueTask<GetFolderFocusRulesResponse> Handle(GetFolderFocusRulesRequest request,
		CancellationToken cancellationToken)
	{
		var folders = _folderCache.GetAllFolders();
		var devices = (await _deviceRepository.GetAll()).ToDictionary(d => d.Id);

		var rules = folders
			.SelectMany(folder => folder.FocusRules.Select(rule =>
				FolderFocusRuleDtoMapper.MapToDto(folder, rule, devices.GetValueOrDefault(rule.DeviceId))))
			.ToList();

		return new GetFolderFocusRulesResponse { Rules = rules };
	}
}
