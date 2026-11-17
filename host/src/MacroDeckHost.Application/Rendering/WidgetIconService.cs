using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Rendering;

public sealed class WidgetIconService : IWidgetIconService
{
	private static readonly TimeSpan _providerReadTimeout = TimeSpan.FromSeconds(3);

	private readonly IFolderCache _folderCache;
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly RemoteIconProviderActionRegistry _remoteIconProviders;
	private readonly IWidgetIconResources _iconResources;
	private readonly IWidgetIconProviderResources _providerResources;
	private readonly StartupReadiness _readiness;
	private readonly IVariableTemplateRenderer _templateRenderer;

	public WidgetIconService(
		IFolderCache folderCache,
		IIntegrationRegistry integrationRegistry,
		RemoteIconProviderActionRegistry remoteIconProviders,
		IWidgetIconResources iconResources,
		IWidgetIconProviderResources providerResources,
		StartupReadiness readiness,
		IVariableTemplateRenderer templateRenderer)
	{
		_folderCache = folderCache;
		_integrationRegistry = integrationRegistry;
		_remoteIconProviders = remoteIconProviders;
		_iconResources = iconResources;
		_providerResources = providerResources;
		_readiness = readiness;
		_templateRenderer = templateRenderer;
	}

	public Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
	{
		return Resolve(widgetId, WidgetIconLimit.Host, cancellationToken);
	}

	public async Task<WidgetIconResolution> Resolve(
		Guid widgetId,
		WidgetIconLimit limit,
		CancellationToken cancellationToken = default)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var widget = FindWidget(widgetId);
		if (widget is null || widget.Type != WidgetTypeIds.ActionButton)
		{
			return WidgetIconResolution.Inactive;
		}

		return await ResolveData(widget.Data, widgetId.ToString("N"), widgetId, limit, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<WidgetIconResolution> ResolveDraft(
		string? data,
		string resourceKey,
		CancellationToken cancellationToken = default)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		return await ResolveData(data, resourceKey, null, WidgetIconLimit.Host, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<WidgetIconResolution> ResolveData(
		string? data,
		string resourceKey,
		Guid? widgetId,
		WidgetIconLimit limit,
		CancellationToken cancellationToken)
	{
		var model = ActionButtonStateModel.Read(data);
		if (model.IconProvider is not { } provider)
		{
			return WidgetIconResolution.Inactive;
		}

		var resolved = FindProviderAction(data, provider);
		if (resolved is null)
		{
			return WidgetIconResolution.Inactive;
		}

		if (widgetId is { } id && ProviderParameters.HasVariableReference(resolved.Value.Block))
		{
			resolved = await WithResolvedReferencesAsync(resolved.Value, id).ConfigureAwait(false);
		}

		var snapshot = await TryReadSnapshot(resolved.Value, cancellationToken).ConfigureAwait(false);

		// Cannot answer at all: null collapses every unavailability reason (unreachable, timed out,
		// threw) onto the same predictable outcome - fall back to the configured icon and never render a
		// stale provider image (issue #425 decision 1). This is the opposite of the state provider, which
		// deliberately holds its last known state: an icon has a meaningful default to fall back to, a
		// state does not.
		if (snapshot is null)
		{
			return WidgetIconResolution.Inactive;
		}

		if (snapshot.NoIcon)
		{
			return WidgetIconResolution.Blank;
		}

		if (snapshot.Reference is { } reference)
		{
			// Resolved through the very same registry a configured icon uses - an icon-pack reference
			// shares that cache, and a type nothing is registered for (notably a provider naming a URL)
			// renders as no icon without this host ever fetching it (issue #425 decision on request
			// forgery, mirroring issue #748 for widget trees).
			var image = await _iconResources
				.ResolveAsync(new WidgetIconReference(reference.Type, reference.Reference), limit, cancellationToken)
				.ConfigureAwait(false);
			return image is null ? WidgetIconResolution.Blank : WidgetIconResolution.Active(image);
		}

		if (string.IsNullOrEmpty(snapshot.Version))
		{
			return WidgetIconResolution.Blank;
		}

		var bytesResource = await _providerResources.ResolveAsync(resourceKey,
				resolved.Value.Fingerprint,
				snapshot.Version,
				fetchToken => resolved.Value.Action
					.GetActionIconContentAsync(resolved.Value.Parameters, snapshot.Version, fetchToken),
				cancellationToken)
			.ConfigureAwait(false);

		return bytesResource is null ? WidgetIconResolution.Blank : WidgetIconResolution.Active(bytesResource);
	}

	public TimeSpan? GetProviderPollInterval(Guid widgetId)
	{
		var widget = FindWidget(widgetId);
		if (widget is null || widget.Type != WidgetTypeIds.ActionButton)
		{
			return null;
		}

		var model = ActionButtonStateModel.Read(widget.Data);
		if (model.IconProvider is not { } provider)
		{
			return null;
		}

		return FindProviderAction(widget.Data, provider)?.Action.IconPollInterval;
	}

	private static async Task<ActionIconSnapshot?> TryReadSnapshot(
		ResolvedProvider resolved,
		CancellationToken cancellationToken)
	{
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(_providerReadTimeout);

		try
		{
			return await resolved.Action.GetActionIconAsync(resolved.Parameters, timeoutSource.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
#pragma warning disable CA1031 // A misbehaving provider must leave a usable widget behind, never fault the resolve loop.
		catch (Exception)
#pragma warning restore CA1031
		{
			return null;
		}
	}

	private readonly record struct ResolvedProvider(
		IIconProviderActionDefinition Action,
		ActionBlock Block,
		IReadOnlyDictionary<string, object?> Parameters,
		string Fingerprint);

	private async Task<ResolvedProvider> WithResolvedReferencesAsync(ResolvedProvider provider, Guid widgetId)
	{
		var parameters = await ProviderParameters.ResolveAsync(provider.Block, widgetId, _templateRenderer)
			.ConfigureAwait(false);
		var values = parameters
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => pair.Key + "->" + JsonSerializer.Serialize(pair.Value));

		return provider with
		{
			Parameters = parameters,
			Fingerprint = string.Join('\n', new[] { provider.Fingerprint }.Concat(values))
		};
	}

	/// <summary>
	/// The guard chain an icon-provider block must pass before it is ever read: found and enabled, its
	/// integration registered and enabled, and its action really an icon provider - shared by the snapshot
	/// read and by the poll-interval lookup so the two can never disagree. Unlike the state provider's
	/// equivalent, a remote action never implements <see cref="IIconProviderActionDefinition" /> directly
	/// (ADR 0056/0032's closed eight-leaf family does not grow a ninth leaf for it) - a plugin action whose
	/// descriptor said <c>ProvidesIcon</c> is instead reached through <see cref="RemoteIconProviderActionRegistry" />.
	/// </summary>
	private ResolvedProvider? FindProviderAction(string? data, ActionButtonIconProvider provider)
	{
		if (!ActionFlowJson.TryFindBlock(ActionFlowJson.ParseFlows(data), provider.BlockId, out var block)
			|| block is null
			|| block.Disabled)
		{
			return null;
		}

		var integrationId = provider.IntegrationId ?? block.IntegrationId;
		var actionId = provider.ActionId ?? block.ActionId;
		if (string.IsNullOrWhiteSpace(integrationId) || string.IsNullOrWhiteSpace(actionId))
		{
			return null;
		}

		var integrationExists = _integrationRegistry.Integrations
			.Any(integration => string.Equals(integration.Id, integrationId, StringComparison.Ordinal));
		if (!integrationExists || !_integrationRegistry.IsEnabled(integrationId))
		{
			return null;
		}

		var action = _integrationRegistry.FindAction(integrationId, actionId);
		var iconProvider = action switch
		{
			IIconProviderActionDefinition direct => direct,
			RemoteActionDefinition { ProvidesIcon: true } => _remoteIconProviders.Resolve(integrationId, actionId),
			_ => null
		};

		if (iconProvider is null)
		{
			return null;
		}

		var parameters = ActionParameterConverter.ToNullable(block.Parameters.ToDictionary(p => p.Name, p => p.Value));
		return new ResolvedProvider(iconProvider, block, parameters, Fingerprint(integrationId, actionId, block));
	}

	private static string Fingerprint(string integrationId, string actionId, ActionBlock block)
	{
		var parameters = block.Parameters
			.OrderBy(p => p.Name, StringComparer.Ordinal)
			.Select(p => p.Name + "=" + RawText(p.Value));

		return string.Join('\n', new[] { integrationId, actionId, block.Id }.Concat(parameters));

		static string RawText(JsonElement value)
		{
			return value.ValueKind == JsonValueKind.Undefined ? string.Empty : value.GetRawText();
		}
	}

	private WidgetEntity? FindWidget(Guid widgetId)
	{
		foreach (var folder in _folderCache.GetAllFolders())
		{
			var widget = folder.Widgets.FirstOrDefault(w => w.Id == widgetId);
			if (widget is not null)
			{
				return widget;
			}
		}

		return null;
	}
}
