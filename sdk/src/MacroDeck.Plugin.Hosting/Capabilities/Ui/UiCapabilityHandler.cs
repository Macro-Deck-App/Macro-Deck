using System.Reflection;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Model.Versioning;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

/// <summary>
/// Exposes every registered integration's <c>IUiProvider</c> as the <c>ui</c> capability, plus two more
/// entry points that are not an <c>IUiProvider</c> at all: an integration's config flow rendering itself
/// as a tree (<see cref="IUiConfigFlow" />), and one configured action instance doing the same
/// (<see cref="IUiConfigurableActionDefinition" />). All three share the one <c>provider</c> local id,
/// with each session addressed by the host-issued session id carried in the operation arguments and
/// routed by the surface attributes described on <see cref="UiConfigSurfaceAttributes" />.
/// </summary>
/// <remarks>
/// Only the host-to-plugin half lives here. Nothing a provider produces returns on a
/// <c>capability.result</c> - trees, patches and faults travel the other way as <c>host.invoke ui/*</c>,
/// driven by <see cref="UiSessionStore" />, so a tree is never held up behind the request that asked for
/// it.
/// </remarks>
internal sealed class UiCapabilityHandler : ICapabilityHandler, IAsyncDisposable
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	// The MacroDeck.Ui.Model version this SDK build speaks. A single integer by protocol contract, not
	// the package's own semantic version.
	private readonly IReadOnlyList<IUiProvider> _providers;
	private readonly IReadOnlyList<IActionDefinition> _actions;
	private readonly Lazy<UiPreviewScanResult> _previews;
	private readonly bool _configFlowServesUiTree;
	private readonly PluginConfigFlowSessions _configFlowSessions;
	private readonly UiSessionStore _sessions;
	private readonly ModalResultStore _modalResults;

	public UiCapabilityHandler(
		IEnumerable<IPluginIntegration> integrations,
		IHostInvoker hostInvoker,
		ILogger logger,
		PluginConfigFlowSessions configFlowSessions,
		ModalResultStore modalResults)
	{
		var integrationList = integrations as IReadOnlyCollection<IPluginIntegration> ?? [.. integrations];

		_providers = [.. integrationList.OfType<IUiProvider>()];
		_actions =
		[
			.. integrationList.SelectMany(integration => integration.Actions).Where(action => action.RunsHere())
		];
		_configFlowServesUiTree =
			integrationList.OfType<IConfigFlowProvider>().FirstOrDefault() is IUiConfigFlowProvider
			{
				ServesConfigUiTree: true
			};
		_configFlowSessions = configFlowSessions;
		_sessions = new UiSessionStore(hostInvoker, logger);
		_modalResults = modalResults;

		// Lazy and metadata-only: declaring previews must not run a scenario, and a plugin nobody ever
		// asks for previews never pays for the scan at all.
		var assemblies = integrationList
			.Select(integration => integration.GetType().Assembly)
			.Append(Assembly.GetEntryAssembly())
			.OfType<Assembly>()
			.Distinct()
			.ToArray();
		_previews = new Lazy<UiPreviewScanResult>(() => UiPreviewCatalog.Scan(assemblies));
	}

	public string Kind => CapabilityKinds.Ui;

	private bool ServesUi => _providers.Count > 0 ||
		_configFlowServesUiTree ||
		_actions.Any(action => action is IUiConfigurableActionDefinition) ||
		_previews.Value.Registrations.Count > 0;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> ServesUi
			?
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Ui, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			]
			: [];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation, CapabilityOperations.Ui.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No UI provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.Ui.SessionOpen => OpenAsync(invocation, cancellationToken),
			CapabilityOperations.Ui.SessionClose => CloseAsync(invocation),
			CapabilityOperations.Ui.SessionSnapshot => Task.FromResult(Snapshot(invocation)),
			CapabilityOperations.Ui.SessionEvent => Task.FromResult(Event(invocation)),
			CapabilityOperations.Ui.ModalResult => Task.FromResult(ModalResult(invocation)),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The ui capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult ModalResult(CapabilityInvocation invocation)
	{
		if (Read<UiModalResultArguments>(invocation) is not { } arguments)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"A modal result needs a modal id.");
		}

		// Accepted even when nothing is waiting: the host sends exactly one result per modal, and one that
		// arrives after its wait was cancelled is late, not a protocol error to report back.
		_modalResults.Complete(arguments);

		return CapabilityInvocationResult.Ok();
	}

	public ValueTask DisposeAsync() => _sessions.DisposeAsync();

	private CapabilityInvocationResult Describe()
	{
		var surfaces = new List<UiSurfaceDescriptorDto>([
			.. _providers.SelectMany(provider => provider.Surfaces)
				.Select(surface => new UiSurfaceDescriptorDto
					{ Kind = surface.Kind, SessionMode = surface.SessionMode })
		]);

		// A config entry point can serve a tree without any IUiProvider declaring a config surface -
		// the flow or the action, not an IUiProvider, is what renders it. Added only when nothing above
		// already said so, so the host's describe view is truthful either way.
		var servesConfigTree = _configFlowServesUiTree ||
			_actions.Any(action => action is IUiConfigurableActionDefinition);
		if (servesConfigTree &&
			!surfaces.Any(surface => string.Equals(surface.Kind, UiSurfaceKinds.Config, StringComparison.Ordinal)))
		{
			surfaces.Add(new UiSurfaceDescriptorDto
				{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive });
		}

		return CapabilityInvocationResult.Ok(new UiDescribePayload
		{
			Surfaces = surfaces,
			UiModelVersion = UiModelVersions.Current,
			Previews =
			[
				.. _previews.Value.Registrations.Select(preview => new UiPreviewDescriptorDto
				{
					Id = preview.Declaration.Id,
					View = preview.Declaration.View,
					Scenario = preview.Declaration.Scenario,
					Profile = preview.Declaration.Profile
				})
			]
		});
	}

	private async Task<CapabilityInvocationResult> OpenAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (Read<UiSessionOpenArguments>(invocation) is not { } arguments)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The session.open operation requires arguments.");
		}

		var attributes = ReadAttributes(arguments.SurfaceAttributes);
		var request = new UiSessionRequest
		{
			Surface = new UiSurface
				{ Kind = arguments.SurfaceKind, SessionMode = arguments.SessionMode, Attributes = attributes },
			UiModelVersion = arguments.UiModelVersion
		};

		// Ahead of every production path on purpose: a developer preview names one registered scenario and
		// must never fall through to a provider that happens to accept the surface.
		if (string.Equals(arguments.SurfaceKind, UiSurfaceKinds.DeveloperPreview, StringComparison.Ordinal))
		{
			return await PreviewSessionAsync(arguments, request).ConfigureAwait(false);
		}

		var configSession = await CreateConfigEntryPointSessionAsync(request, attributes, cancellationToken)
			.ConfigureAwait(false);
		if (configSession is not null)
		{
			return await AcceptSessionAsync(arguments.SessionId, configSession).ConfigureAwait(false);
		}

		foreach (var provider in _providers)
		{
			var session = await provider.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);

			if (session is null)
			{
				continue;
			}

			return await AcceptSessionAsync(arguments.SessionId, session).ConfigureAwait(false);
		}

		return CapabilityInvocationResult.Ok(new UiSessionOpenResult
		{
			Accepted = false,
			RejectionReason = $"No provider in this plugin serves a '{arguments.SurfaceKind}' surface."
		});
	}

	private async Task<CapabilityInvocationResult> PreviewSessionAsync(
		UiSessionOpenArguments arguments,
		UiSessionRequest request)
	{
		if (!request.Surface.Attributes.TryGetValue(UiDeveloperPreviewSurfaceAttributes.PreviewId, out var id) ||
			id.ValueKind != JsonValueKind.String ||
			_previews.Value.Registrations.FirstOrDefault(preview =>
				string.Equals(preview.Declaration.Id, id.GetString(), StringComparison.Ordinal)) is not { } match)
		{
			return CapabilityInvocationResult.Ok(new UiSessionOpenResult
			{
				Accepted = false, RejectionReason = "This plugin declares no preview with that id."
			});
		}

		// A scenario is ordinary authored code: it can throw, and when it does only this session fails.
		UiPreviewSession session;
		try
		{
			session = new UiPreviewSession(match.Create(request.Surface));
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return CapabilityInvocationResult.Ok(new UiSessionOpenResult
			{
				Accepted = false, RejectionReason = $"That preview could not be built: {exception.Message}"
			});
		}

		return await AcceptSessionAsync(arguments.SessionId, session).ConfigureAwait(false);
	}

	/// <summary>
	/// Routes an <c>integration-config</c> or <c>action-config</c> surface (see
	/// <see cref="UiConfigEntryPoints" />) to the flow or action instance it names. Returns <c>null</c>
	/// for any surface that is not a recognised config entry point, for an entry point whose target does
	/// not exist or does not serve a tree, and for a target that itself declines the session - every one
	/// of those falls back to the <see cref="_providers" /> loop, and from there to the "no provider"
	/// rejection, exactly like a surface kind nothing here recognises at all.
	/// </summary>
	private async Task<IUiSession?> CreateConfigEntryPointSessionAsync(
		UiSessionRequest request,
		Dictionary<string, JsonElement> attributes,
		CancellationToken cancellationToken)
	{
		if (!TryGetString(attributes, UiConfigSurfaceAttributes.EntryPoint, out var entryPoint))
		{
			return null;
		}

		if (string.Equals(entryPoint, UiConfigEntryPoints.IntegrationConfig, StringComparison.Ordinal))
		{
			if (!TryGetString(attributes, UiConfigSurfaceAttributes.ConfigFlowSessionId, out var flowSessionId) ||
				!_configFlowSessions.TryGetFlow(flowSessionId, out var flow) ||
				flow is not IUiConfigFlow uiFlow)
			{
				return null;
			}

			return await uiFlow.CreateUiSessionAsync(request, cancellationToken).ConfigureAwait(false);
		}

		if (string.Equals(entryPoint, UiConfigEntryPoints.ActionConfig, StringComparison.Ordinal))
		{
			if (!TryGetString(attributes, UiConfigSurfaceAttributes.ActionId, out var actionId) ||
				FindConfigurableAction(actionId) is not { } configurable)
			{
				return null;
			}

			var configurationRequest = new ActionConfigurationRequest
			{
				Session = request,
				Parameters = attributes.TryGetValue(UiConfigSurfaceAttributes.Parameters, out var parameters)
					? ReadAttributes(parameters)
					: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			};

			return await configurable.CreateConfigurationSessionAsync(configurationRequest, cancellationToken)
				.ConfigureAwait(false);
		}

		return null;
	}

	private IUiConfigurableActionDefinition? FindConfigurableAction(string actionId)
		=> _actions.FirstOrDefault(action => string.Equals(action.Id, actionId, StringComparison.Ordinal))
			as IUiConfigurableActionDefinition;

	private async Task<CapabilityInvocationResult> AcceptSessionAsync(string sessionId, IUiSession session)
	{
		if (_sessions.TryAdd(sessionId, session))
		{
			return CapabilityInvocationResult.Ok(new UiSessionOpenResult
			{
				Accepted = true, NegotiatedUiModelVersion = UiModelVersions.Current
			});
		}

		// The host issues session ids and never reuses a live one, so this only happens if it replays an
		// open. The session just built is nobody's, so it is disposed here rather than leaked; the one
		// already serving that id is left alone.
		await session.DisposeAsync().ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new UiSessionOpenResult
		{
			Accepted = false, RejectionReason = "A session with that id is already open."
		});
	}

	private static bool TryGetString(Dictionary<string, JsonElement> attributes, string key, out string value)
	{
		if (attributes.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
		{
			value = element.GetString() ?? string.Empty;
			return !string.IsNullOrEmpty(value);
		}

		value = string.Empty;
		return false;
	}

	private async Task<CapabilityInvocationResult> CloseAsync(CapabilityInvocation invocation)
	{
		if (Read<UiSessionCloseArguments>(invocation) is not { } arguments)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The session.close operation requires arguments.");
		}

		// Closing an unknown session succeeds: the host closes a session it has already given up on
		// after a fault, and answering an error there would only make a tidy teardown look failed.
		await _sessions.RemoveAsync(arguments.SessionId).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private CapabilityInvocationResult Snapshot(CapabilityInvocation invocation)
	{
		if (Read<UiSessionSnapshotArguments>(invocation) is not { } arguments)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The session.snapshot operation requires arguments.");
		}

		// The tree does not return here: it is pushed as host.invoke ui/snapshot once the session's pump
		// has built it, so one delivery path serves a first attach and a resync alike.
		return _sessions.RequestSnapshot(arguments.SessionId)
			? CapabilityInvocationResult.Ok()
			: UnknownSession(arguments.SessionId);
	}

	private CapabilityInvocationResult Event(CapabilityInvocation invocation)
	{
		if (Read<UiSessionEventArguments>(invocation) is not { } arguments)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The session.event operation requires arguments.");
		}

		var uiEvent = new UiEvent
		{
			NodeId = arguments.NodeId,
			Name = arguments.Name,
			Data = arguments.Data,
			Revision = arguments.Revision
		};

		return _sessions.Dispatch(arguments.SessionId, uiEvent)
			? CapabilityInvocationResult.Ok()
			: UnknownSession(arguments.SessionId);
	}

	private static CapabilityInvocationResult UnknownSession(string sessionId)
		=> CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
			$"No UI session '{sessionId}' is open in this plugin.");

	private static Dictionary<string, JsonElement> ReadAttributes(JsonElement? attributes)
	{
		var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

		if (attributes is { ValueKind: JsonValueKind.Object } element)
		{
			foreach (var property in element.EnumerateObject())
			{
				map[property.Name] = property.Value;
			}
		}

		return map;
	}

	private static T? Read<T>(CapabilityInvocation invocation)
		where T : class
		=> invocation.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
}
