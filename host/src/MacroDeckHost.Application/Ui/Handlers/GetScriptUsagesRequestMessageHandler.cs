using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetScriptUsagesRequestMessageHandler
	: IUiTransportMessageHandler<GetScriptUsagesRequest, GetScriptUsagesResponse>
{
	private readonly IScriptService _scriptService;
	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly StartupReadiness _readiness;

	public GetScriptUsagesRequestMessageHandler(
		IScriptService scriptService,
		IFolderCache folderCache,
		IProfileCache profileCache,
		StartupReadiness readiness)
	{
		_scriptService = scriptService;
		_folderCache = folderCache;
		_profileCache = profileCache;
		_readiness = readiness;
	}

	public async ValueTask<GetScriptUsagesResponse> Handle(
		GetScriptUsagesRequest request,
		CancellationToken cancellationToken)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var response = new GetScriptUsagesResponse();
		if (!Guid.TryParse(request.Id, out var scriptId))
		{
			return response;
		}

		var wanted = new HashSet<Guid> { scriptId };
		var profileNames = _profileCache.GetAll().ToDictionary(profile => profile.Id, profile => profile.Name);

		foreach (var folder in _folderCache.GetAllFolders())
		{
			var count = folder.Widgets.Count(widget => ScriptReferences.Extract(widget.Data, wanted).Count > 0);
			if (count == 0)
			{
				continue;
			}

			response.Usages.Add(new ScriptUsage
			{
				Kind = "widget",
				Location = profileNames.TryGetValue(folder.ProfileId, out var profileName)
					? $"{profileName} / {folder.Name}"
					: folder.Name,
				Count = count
			});
		}

		foreach (var script in _scriptService.GetAll())
		{
			if (script.Id != scriptId && ScriptReferences.Extract(script.Flows, wanted).Count > 0)
			{
				response.Usages.Add(new ScriptUsage { Kind = "script", Location = script.Name, Count = 1 });
			}
		}

		return response;
	}
}
