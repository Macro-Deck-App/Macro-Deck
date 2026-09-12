using System.Collections.Concurrent;
using System.Reflection;
using MacroDeckHost.Application.Persistence;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Integrations;

public class IntegrationRegistry : IIntegrationRegistry
{
	private readonly ConcurrentDictionary<string, IIntegration> _integrations = new();
	private readonly ConcurrentDictionary<string, bool> _enabledState;
	private readonly ConcurrentDictionary<string, long> _disabledVersions = new();
	private readonly ConcurrentDictionary<string, IntegrationOrigin> _origins = new();
	private readonly ConcurrentDictionary<string, IntegrationMetadata> _metadata = new();
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly IIntegrationStateStore _stateStore;
	private readonly ILogger _logger;

	public IntegrationRegistry(
		IServiceScopeFactory serviceScopeFactory,
		IIntegrationStateStore stateStore,
		ILogger logger)
	{
		_serviceScopeFactory = serviceScopeFactory;
		_stateStore = stateStore;
		_logger = logger;
		_enabledState = new ConcurrentDictionary<string, bool>(stateStore.Load());
	}

	public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged;

	public IReadOnlyList<IIntegration> Integrations =>
		_integrations.Values.ToList().AsReadOnly();

	public IActionDefinition? FindAction(string integrationId, string actionId)
	{
		if (!_integrations.TryGetValue(integrationId, out var integration))
		{
			return null;
		}

		return integration.Actions.FirstOrDefault(a => a.Id == actionId && a.RunsHere());
	}

	public IActionDefinition? FindAction(QualifiedId id) => FindAction(id.OwnerId, id.LocalId);

	public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true)
	{
		var descriptors = new List<ActionDescriptor>();

		foreach (var integration in _integrations.Values)
		{
			if (enabledOnly && !IsEnabled(integration.Id))
			{
				continue;
			}

			foreach (var action in integration.Actions)
			{
				if (!action.RunsHere())
				{
					continue;
				}

				if (QualifiedId.TryCreate(integration.Id,
					action.Id,
					OwnerIdKind.Package,
					LocalIdKind.Declared,
					out var id))
				{
					descriptors.Add(new ActionDescriptor(id, integration, action));
				}
			}
		}

		return descriptors;
	}

	public bool IsEnabled(string integrationId)
	{
		if (_integrations.TryGetValue(integrationId, out var integration) && integration is ISystemIntegration system)
		{
			return system.IsActive;
		}

		if (_enabledState.TryGetValue(integrationId, out var enabled))
		{
			return enabled;
		}

		if (integration is null)
		{
			return true;
		}

		var metadata = _metadata.TryGetValue(integrationId, out var registered)
			? registered
			: IntegrationMetadata.Default;
		return metadata.EnabledByDefault && integration is not IConfigFlowProvider;
	}

	public IntegrationOrigin GetOrigin(string integrationId)
		=> _origins.TryGetValue(integrationId, out var origin) ? origin : IntegrationOrigin.BuiltIn;

	public void SetEnabled(string integrationId, bool enabled)
	{
		if (_integrations.TryGetValue(integrationId, out var integration) && integration is ISystemIntegration)
		{
			return;
		}

		_enabledState[integrationId] = enabled;
		if (!enabled)
		{
			_disabledVersions.AddOrUpdate(integrationId, 1, (_, version) => version + 1);
		}

		_stateStore.Save(_enabledState);

		_logger.Information("Integration '{IntegrationId}' {State}",
			integrationId,
			enabled ? "enabled" : "disabled");

		AvailabilityChanged?.Invoke(this,
			new IntegrationAvailabilityChangedEventArgs { IntegrationId = integrationId, IsAvailable = enabled });
	}

	public bool IsExplicitlyDisabled(string integrationId)
		=> _enabledState.TryGetValue(integrationId, out var enabled) && !enabled;

	public long DisabledVersion(string integrationId) => _disabledVersions.GetValueOrDefault(integrationId);

	// Both clears are compare-and-remove, so a choice a concurrent SetEnabled has just stored survives.
	public bool ClearDisabledChoice(string integrationId) => ClearChoice(integrationId, false);

	public void ClearEnabledChoice(string integrationId) => ClearChoice(integrationId, true);

	private bool ClearChoice(string integrationId, bool storedChoice)
	{
		if (!_enabledState.TryRemove(new KeyValuePair<string, bool>(integrationId, storedChoice)))
		{
			return false;
		}

		_stateStore.Save(_enabledState);
		_logger.Information("Integration '{IntegrationId}' no longer has a stored choice", integrationId);
		return true;
	}

	public Task<IntegrationRegistrationResult> RegisterAsync(
		IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null)
	{
		var conflicts = IntegrationCapabilityValidator.Validate(integration);
		if (conflicts.Count > 0)
		{
			var result = IntegrationRegistrationResult.Invalid(conflicts);
			_logger.Error("Rejecting integration '{IntegrationId}' ({IntegrationType}): {Conflicts}",
				integration.Id,
				integration.GetType().FullName,
				result.Describe());
			return Task.FromResult(result);
		}

		if (!_integrations.TryAdd(integration.Id, integration))
		{
			// A remote plugin reconnecting (inside the resume window, or an installed-but-stopped plugin
			// that then connects) re-registers under the same id its own prior adapter still holds - that
			// is a replacement, not a genuine collision, and must not be rejected: RegistrationRejected is
			// terminal (see ProtocolCloseCodes), so refusing it here would make reconnection impossible.
			// A different owner claiming this id - a built-in integration, or another plugin - is still a
			// real collision and stays rejected below. Enabled state deliberately survives: it lives in
			// _enabledState, which this branch never touches, same as UnregisterAsync's own guarantee.
			if (origin == IntegrationOrigin.Plugin &&
				_origins.TryGetValue(integration.Id, out var existingOrigin) &&
				existingOrigin == IntegrationOrigin.Plugin)
			{
				_integrations[integration.Id] = integration;
				_origins[integration.Id] = origin;
				_metadata[integration.Id] = metadata ?? MetadataFromAttribute(integration);

				_logger.Information(
					"Replacing integration '{IntegrationId}' ({IntegrationName} v{IntegrationVersion}) - plugin reconnected",
					integration.Id,
					// A LocalizedText would be destructured into its parts by Serilog; a log line wants the
					// diagnostic rendering instead.
					integration.Name.ToString(),
					integration.Version);

				return Task.FromResult(IntegrationRegistrationResult.Success);
			}

			_logger.Error(
				"Rejecting integration '{IntegrationId}' ({IntegrationType}): that id is already registered by " +
				"'{RegisteredType}'. Integration ids must be globally unique.",
				integration.Id,
				integration.GetType().FullName,
				_integrations[integration.Id].GetType().FullName);
			return Task.FromResult(IntegrationRegistrationResult.DuplicateOwner);
		}

		_origins[integration.Id] = origin;
		_metadata[integration.Id] = metadata ?? MetadataFromAttribute(integration);

		_logger.Information("Registering integration '{IntegrationId}' ({IntegrationName} v{IntegrationVersion})",
			integration.Id,
			integration.Name.ToString(),
			integration.Version);

		return Task.FromResult(IntegrationRegistrationResult.Success);
	}

	public Task<bool> UnregisterAsync(string integrationId)
	{
		var removed = _integrations.TryRemove(integrationId, out _);
		_origins.TryRemove(integrationId, out _);
		_metadata.TryRemove(integrationId, out _);

		if (removed)
		{
			_logger.Information("Unregistering integration '{IntegrationId}'", integrationId);

			AvailabilityChanged?.Invoke(this,
				new IntegrationAvailabilityChangedEventArgs { IntegrationId = integrationId, IsAvailable = false });
		}
		else
		{
			_logger.Warning("Cannot unregister integration '{IntegrationId}': it was not registered", integrationId);
		}

		return Task.FromResult(removed);
	}

	private static IntegrationMetadata MetadataFromAttribute(IIntegration integration)
	{
		var attribute = integration.GetType().GetCustomAttribute<MacroDeckIntegrationAttribute>();
		if (attribute is null)
		{
			return IntegrationMetadata.Default;
		}

		return new IntegrationMetadata
		{
			Platforms = attribute.Platforms,
			EnabledByDefault = attribute.EnabledByDefault
		};
	}
}
