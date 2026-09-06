using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class ListUiPreviewsRequestMessageHandler
	: IUiTransportMessageHandler<ListUiPreviewsRequest, ListUiPreviewsResponse>
{
	private readonly IEnumerable<IUiPreviewSource> _sources;
	private readonly IIntegrationRegistry _integrations;
	private readonly IRemotePluginSnapshotStore _snapshots;

	public ListUiPreviewsRequestMessageHandler(
		IEnumerable<IUiPreviewSource> sources,
		IIntegrationRegistry integrations,
		IRemotePluginSnapshotStore snapshots)
	{
		_sources = sources;
		_integrations = integrations;
		_snapshots = snapshots;
	}

	public ValueTask<ListUiPreviewsResponse> Handle(
		ListUiPreviewsRequest request,
		CancellationToken cancellationToken)
	{
		var previews = new List<UiPreviewEntry>();
		var diagnostics = new List<UiPreviewDiagnosticEntry>();

		foreach (var source in _sources)
		{
			previews.AddRange(source.Previews.Select(preview => new UiPreviewEntry
			{
				Id = preview.Id,
				View = preview.View,
				Scenario = preview.Scenario,
				Profile = preview.Profile,
				OwnerId = string.Empty
			}));

			diagnostics.AddRange(source.Skipped.Select(skipped => new UiPreviewDiagnosticEntry
			{
				Member = skipped.Member, Reason = skipped.Reason
			}));
		}

		foreach (var integration in _integrations.Integrations)
		{
			if (!_snapshots.Has(integration.Id))
			{
				continue;
			}

			previews.AddRange(_snapshots.GetSnapshot(integration.Id).UiPreviews.Select(preview => new UiPreviewEntry
			{
				Id = preview.Id,
				View = preview.View,
				Scenario = preview.Scenario,
				Profile = preview.Profile,
				OwnerId = integration.Id
			}));
		}

		return ValueTask.FromResult(new ListUiPreviewsResponse { Previews = previews, Diagnostics = diagnostics });
	}
}
