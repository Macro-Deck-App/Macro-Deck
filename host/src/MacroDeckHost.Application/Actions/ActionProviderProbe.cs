using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Widgets;
using Serilog;

namespace MacroDeckHost.Application.Actions;

public enum ActionProbeOutcome
{
	Ok,
	NotFound,
	Timeout,
	Failed,
}

public sealed record ActionProbeResult<T>(ActionProbeOutcome Outcome, T? Snapshot)
	where T : class;

public sealed class ActionProviderProbe
{
	private static readonly TimeSpan _providerTimeout = TimeSpan.FromSeconds(10);

	private readonly IIntegrationRegistry _integrations;
	private readonly RemoteIconProviderActionRegistry _remoteIconProviders;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public ActionProviderProbe(
		IIntegrationRegistry integrations,
		RemoteIconProviderActionRegistry remoteIconProviders,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_integrations = integrations;
		_remoteIconProviders = remoteIconProviders;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<ActionProviderProbe>();
	}

	public bool IsStateProvider(string integrationId, string actionId)
		=> ResolveStateProvider(integrationId, actionId) is not null;

	public bool IsIconProvider(string integrationId, string actionId)
		=> _integrations.IsEnabled(integrationId) &&
			_integrations.FindAction(integrationId, actionId) switch
			{
				IIconProviderActionDefinition => true,
				RemoteActionDefinition remote => remote.ProvidesIcon,
				_ => false,
			};

	public async Task<ActionProbeResult<ActionStateSnapshot>> ProbeStatesAsync(
		string integrationId,
		string actionId,
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (ResolveStateProvider(integrationId, actionId) is not { } provider)
		{
			return new(ActionProbeOutcome.NotFound, null);
		}

		var result = await ProbeAsync(integrationId,
				actionId,
				token => provider.GetActionStateAsync(parameters, token),
				cancellationToken)
			.ConfigureAwait(false);

		if (result is { Outcome: ActionProbeOutcome.Ok, Snapshot: { } snapshot } &&
			!(snapshot.States.All(s => ActionButtonStateJson.IsValidId(s.Id)) &&
				(snapshot.ActiveStateId is null || snapshot.States.Any(s => s.Id == snapshot.ActiveStateId))))
		{
			return new(ActionProbeOutcome.Failed, null);
		}

		return result;
	}

	public async Task<ActionProbeResult<ActionIconSnapshot>> ProbeIconAsync(
		string integrationId,
		string actionId,
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (ResolveIconProvider(integrationId, actionId) is not { } provider)
		{
			return new(ActionProbeOutcome.NotFound, null);
		}

		return await ProbeAsync(integrationId,
				actionId,
				token => provider.GetActionIconAsync(parameters, token),
				cancellationToken)
			.ConfigureAwait(false);
	}

	private IStateProviderActionDefinition? ResolveStateProvider(string integrationId, string actionId)
		=> _integrations.IsEnabled(integrationId)
			? _integrations.FindAction(integrationId, actionId) as IStateProviderActionDefinition
			: null;

	private IIconProviderActionDefinition? ResolveIconProvider(string integrationId, string actionId)
	{
		if (!_integrations.IsEnabled(integrationId))
		{
			return null;
		}

		return _integrations.FindAction(integrationId, actionId) switch
		{
			IIconProviderActionDefinition direct => direct,
			RemoteActionDefinition { ProvidesIcon: true } => _remoteIconProviders.Resolve(integrationId, actionId),
			_ => null,
		};
	}

	private async Task<ActionProbeResult<T>> ProbeAsync<T>(
		string integrationId,
		string actionId,
		Func<CancellationToken, Task<T?>> probe,
		CancellationToken cancellationToken)
		where T : class
	{
		using var timeout = new CancellationTokenSource(_providerTimeout, _timeProvider);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

		try
		{
			return new(ActionProbeOutcome.Ok, await probe(linked.Token).ConfigureAwait(false));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return new(ActionProbeOutcome.Failed, null);
		}
		catch (OperationCanceledException)
		{
			return new(ActionProbeOutcome.Timeout, null);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to probe provider action {Integration}.{Action}", integrationId, actionId);

			return new(ActionProbeOutcome.Failed, null);
		}
	}
}
