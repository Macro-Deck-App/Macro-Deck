using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class FakeIntegrationHostIssueStore : IIntegrationHostIssueStore
{
	private readonly ConcurrentDictionary<string, IntegrationIssue> _issues = new(StringComparer.Ordinal);

	public int RefreshRequests { get; private set; }

	public void RaiseStartupFailure(string integrationId, string details)
	{
		_issues[integrationId] = new IntegrationIssue
		{
			Id = IntegrationHostIssueIds.Startup,
			Title = "Integration is not running",
			Description = details,
			Severity = IntegrationIssueSeverity.Error,
			ActionLabel = "Try again"
		};
		RefreshRequests++;
	}

	public void RaiseStartupTimeout(string integrationId)
	{
		_issues[integrationId] = new IntegrationIssue
		{
			Id = IntegrationHostIssueIds.Startup,
			Title = "Integration is not running",
			Description = "Timed out",
			Severity = IntegrationIssueSeverity.Error,
			ActionLabel = "Try again"
		};
		RefreshRequests++;
	}

	public void Clear(string integrationId)
	{
		if (_issues.TryRemove(integrationId, out _))
		{
			RefreshRequests++;
		}
	}

	public IReadOnlyList<IntegrationIssue> IssuesFor(string integrationId)
		=> _issues.TryGetValue(integrationId, out var issue) ? [issue] : [];

	public bool Has(string integrationId) => _issues.ContainsKey(integrationId);
}

internal sealed class FakeIntegrationLifecycle : IIntegrationLifecycle
{
	public List<string> ReinitializeCalls { get; } = [];

	public Func<string, Task>? OnReinitialize { get; set; }

	public Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
	{
		ReinitializeCalls.Add(integrationId);
		return OnReinitialize?.Invoke(integrationId) ?? Task.CompletedTask;
	}

	public Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}

internal sealed class ThreadRecordingIntegrationRegistry : IIntegrationRegistry
{
	public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
	{
		add { }
		remove { }
	}

	private readonly List<IIntegration> _integrations = [];
	private readonly HashSet<string> _disabled;

	public ThreadRecordingIntegrationRegistry(IEnumerable<IIntegration>? integrations = null,
		IEnumerable<string>? disabled = null)
	{
		_integrations = (integrations ?? []).ToList();
		_disabled = new HashSet<string>(disabled ?? [], StringComparer.Ordinal);
	}

	private readonly object _eventsLock = new();

	public List<string> RegisteredIds { get; } = [];

	/// <summary>Ordered log of "register:{id}" / "initialize:{id}" markers, shared with the caller's
	/// integration doubles via <see cref="RecordEvent"/> so a test can assert that no registration begins
	/// after any initialization has begun.</summary>
	public List<string> Events { get; } = [];

	public void RecordEvent(string entry)
	{
		lock (_eventsLock)
		{
			Events.Add(entry);
		}
	}

	public IReadOnlyList<IIntegration> Integrations => _integrations;

	public IActionDefinition? FindAction(string integrationId, string actionId) => null;

	public IActionDefinition? FindAction(QualifiedId id) => null;

	public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

	public bool IsEnabled(string integrationId) => !_disabled.Contains(integrationId);

	public void SetEnabled(string integrationId, bool enabled)
	{
		if (enabled)
		{
			_disabled.Remove(integrationId);
		}
		else
		{
			_disabled.Add(integrationId);
		}
	}

	public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

	public Task<IntegrationRegistrationResult> RegisterAsync(
		IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null)
	{
		RegisteredIds.Add(integration.Id);
		RecordEvent($"register:{integration.Id}");
		_integrations.Add(integration);
		return Task.FromResult(IntegrationRegistrationResult.Success);
	}

	public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(false);
}

internal sealed class CountingUserNotificationStore : IUserNotificationStore
{
	private readonly ConcurrentDictionary<string, int> _callsByDedupeKey = new(StringComparer.Ordinal);

	public int Capacity => 1000;

	public int CallCount(string dedupeKey) => _callsByDedupeKey.GetValueOrDefault(dedupeKey);

	public UserNotification? Raise(UserNotificationDraft draft)
	{
		if (draft.DedupeKey is not null)
		{
			_callsByDedupeKey.AddOrUpdate(draft.DedupeKey, 1, (_, count) => count + 1);
		}

		return null;
	}

	public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => Raise(draft);

	public IReadOnlyList<UserNotification> Snapshot() => [];

	public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress) => false;

	public bool DismissByKey(string dedupeKey) => false;

	public bool Dismiss(string id) => false;

	public bool Retire(string dedupeKey) => false;

	public bool DismissAll() => false;

	public event Action? Changed
	{
		add { }
		remove { }
	}
}

internal sealed class FakeIntegrationConfigStore : IIntegrationConfigStore
{
	private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

	public void SetConfiguredCount(string integrationId, int count) => _counts[integrationId] = count;

	public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
	{
		var count = _counts.GetValueOrDefault(integrationId);
		IReadOnlyList<ConfigEntrySummary> entries = Enumerable.Range(0, count)
			.Select(i => new ConfigEntrySummary(Guid.NewGuid(), integrationId, $"Entry {i}", DateTime.UtcNow))
			.ToList();
		return Task.FromResult(entries);
	}

	public Task<ConfigEntryRecord?> Find(Guid entryId) => Task.FromResult<ConfigEntryRecord?>(null);

	public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
		=> throw new NotSupportedException();

	public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
		=> throw new NotSupportedException();

	public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
		=> throw new NotSupportedException();

	public Task Delete(Guid entryId) => throw new NotSupportedException();
}

internal sealed class ConfigurableIntegrationRegistry : IIntegrationRegistry
{
	public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
	{
		add { }
		remove { }
	}

	private readonly List<IIntegration> _integrations;
	private readonly HashSet<string> _disabled;

	public ConfigurableIntegrationRegistry(IEnumerable<IIntegration> integrations, IEnumerable<string>? disabled = null)
	{
		_integrations = integrations.ToList();
		_disabled = new HashSet<string>(disabled ?? []);
	}

	public IReadOnlyList<IIntegration> Integrations => _integrations;

	public IActionDefinition? FindAction(string integrationId, string actionId) => null;

	public IActionDefinition? FindAction(QualifiedId id) => null;

	public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true)
	{
		var descriptors = new List<ActionDescriptor>();
		foreach (var integration in _integrations)
		{
			if (enabledOnly && !IsEnabled(integration.Id))
			{
				continue;
			}

			foreach (var action in integration.Actions)
			{
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

	public bool IsEnabled(string integrationId) => !_disabled.Contains(integrationId);

	public void SetEnabled(string integrationId, bool enabled)
	{
		if (enabled)
		{
			_disabled.Remove(integrationId);
		}
		else
		{
			_disabled.Add(integrationId);
		}
	}

	public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

	public Task<IntegrationRegistrationResult> RegisterAsync(
		IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null)
	{
		_integrations.Add(integration);
		return Task.FromResult(IntegrationRegistrationResult.Success);
	}

	public Task<bool> UnregisterAsync(string integrationId)
	{
		var integration = _integrations.FirstOrDefault(i => i.Id == integrationId);
		if (integration is null)
		{
			return Task.FromResult(false);
		}

		_integrations.Remove(integration);
		return Task.FromResult(true);
	}
}

internal static class StubIntegration
{
	public static IIntegration Create(string id, string? name = null, bool configFlow = false, bool system = false)
		=> (configFlow, system) switch
		{
			(false, false) => new PlainIntegration(id, name ?? id),
			(true, false) => new ConfigurableIntegration(id, name ?? id),
			(false, true) => new SystemIntegration(id, name ?? id),
			(true, true) => new ConfigurableSystemIntegration(id, name ?? id)
		};

	private abstract class IntegrationBase : IIntegration
	{
		protected IntegrationBase(string id, string name)
		{
			Id = id;
			Name = name;
		}

		public string Id { get; }
		public LocalizedText Name { get; }
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class PlainIntegration : IntegrationBase
	{
		public PlainIntegration(string id, string name)
			: base(id, name)
		{
		}
	}

	private sealed class ConfigurableIntegration : IntegrationBase, IConfigFlowProvider
	{
		public ConfigurableIntegration(string id, string name)
			: base(id, name)
		{
		}

		public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
	}

	private sealed class SystemIntegration : IntegrationBase, ISystemIntegration
	{
		public SystemIntegration(string id, string name)
			: base(id, name)
		{
		}

		public bool IsActive => true;
	}

	private sealed class ConfigurableSystemIntegration : IntegrationBase, IConfigFlowProvider, ISystemIntegration
	{
		public ConfigurableSystemIntegration(string id, string name)
			: base(id, name)
		{
		}

		public bool IsActive => true;

		public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
	}
}

internal class FakeVariableProviderIntegration : IIntegration, IVariableProvider
{
	private IReadOnlyList<VariableDefinition>? _declaredVariablesOverride;

	public string Id { get; init; } = "fake-variable-provider";
	public LocalizedText Name { get; init; } = "Fake Variable Provider";
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions { get; init; } = [];
	public bool IsInitialized { get; set; }
	public int InitializeCallCount { get; private set; }

	public IReadOnlyList<VariableDefinition> Variables { get; set; } = [];

	public IReadOnlyList<VariableDefinition>? DeclaredVariablesOverride
	{
		get => _declaredVariablesOverride;
		set => _declaredVariablesOverride = value;
	}

	public IReadOnlyList<VariableDefinition> DeclaredVariables => _declaredVariablesOverride ?? Variables;

	public bool VariablesDependOnConfiguration { get; set; }

	public virtual ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableReading.Unavailable);

	public virtual ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableWriteResult.NotWritable());

	public virtual Task InitializeAsync(IIntegrationContext context)
	{
		InitializeCallCount++;
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public virtual Task ShutdownAsync()
	{
		IsInitialized = false;
		return Task.CompletedTask;
	}
}

internal sealed class FakeConfigurableVariableProviderIntegration : FakeVariableProviderIntegration, IConfigFlowProvider
{
	public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
}

/// <summary>A provider that records every write dispatched to it and answers with whatever a test set,
/// so a test can see what crossed the one boundary a variable write leaves the host through.</summary>
internal sealed class FakeWritableVariableProviderIntegration : FakeVariableProviderIntegration
{
	public List<(string LocalId, object? Value)> Writes { get; } = [];

	public VariableWriteResult Answer { get; set; } = VariableWriteResult.Applied();

	public override ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		Writes.Add((localId, value));
		return ValueTask.FromResult(Answer);
	}
}

internal sealed class FakeProfileProviderIntegration : IIntegration, IProfileProvider
{
	private readonly IReadOnlyList<VirtualProfileDescriptor> _profiles;

	public FakeProfileProviderIntegration(string id, params VirtualProfileDescriptor[] profiles)
	{
		Id = id;
		_profiles = profiles;
	}

	public string Id { get; }
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public string ProviderName => Id;

	public List<(string FolderId, string WidgetId, WidgetInteraction Interaction)> Interactions { get; } = new();

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;
	public bool IsInitialized => true;

	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => _profiles;

	public Task HandleWidgetInteractionAsync(
		string profileId,
		string folderId,
		string widgetId,
		WidgetInteraction interaction)
	{
		Interactions.Add((folderId, widgetId, interaction));
		return Task.CompletedTask;
	}
}

internal sealed class FakeEventOnlyIntegration : IIntegration, IEventProvider
{
	public string Id { get; init; } = "fake-event-only";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;
	public IReadOnlyList<EventDefinition> EventDefinitions => [];
}

internal sealed class FakeMusicPlayerOnlyIntegration : IIntegration, IMusicPlayerProvider
{
	public string Id { get; init; } = "fake-music-player-only";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;
	public IReadOnlyList<MusicPlayerInstance> GetInstances() => [];
	public IMusicPlayer? GetPlayer(string instanceId) => null;
}

internal sealed class FakeWeatherOnlyIntegration : IIntegration, IWeatherProvider
{
	public string Id { get; init; } = "fake-weather-only";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;
	public IReadOnlyList<WeatherStationInstance> GetInstances() => [];
	public IWeatherStation? GetStation(string instanceId) => null;
}

/// <summary>Declares one migration, which is what makes the capability worth showing at all - an
/// integration whose list is empty offers nothing to take a setup over from.</summary>
internal sealed class FakeMigrationOnlyIntegration : IIntegration, IMigrationProvider
{
	public string Id { get; init; } = "fake-migration-only";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; init; } = [new FakeIntegrationMigration()];
}

internal sealed class FakeIntegrationMigration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	public IReadOnlyList<string> ClaimedActionSources => ["Some Plugin"];

	public IReadOnlyList<string> ClaimedSettingsSources => ["some_plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult<ActionMigrationResult?>(null);

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<MigratedConfiguration>>([]);
}

internal sealed class FakeHiddenCapabilitiesIntegration :
	IIntegration,
	IEventProvider,
	IDynamicEventOptionsProvider,
	IMusicPlayerProvider,
	IMusicPlayerCatalogProvider,
	IMusicPlayerDeviceProvider,
	IIntegrationIconProvider,
	IConfigFlowProvider,
	IIntegrationIssueProvider
{
	public string Id { get; init; } = "fake-hidden-capabilities";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;
	public IReadOnlyList<EventDefinition> EventDefinitions => [];

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(new DynamicOptionsResult { Options = [] });

	public IReadOnlyList<MusicPlayerInstance> GetInstances() => [];
	public IMusicPlayer? GetPlayer(string instanceId) => null;

	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<MusicPlayerCatalogItem>>([]);

	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<MusicPlayerDevice>>([]);

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	public string IconMimeType => "image/png";

	public byte[] GetIcon() => [];

	public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<IntegrationIssue>>([
			new IntegrationIssue { Id = "problem", Title = "A problem" }
		]);

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();
}

internal sealed class FakeStateProviderActionIntegration : IIntegration
{
	public string Id { get; init; } = "fake-state-provider-actions";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; } =
		[new CapturingActionDefinition { Id = "plain" }, new StateProvidingActionDefinition()];

	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	private sealed class StateProvidingActionDefinition : IActionDefinition, IStateProviderActionDefinition
	{
		public string Id => "toggle";
		public LocalizedText Name => "Toggle";
		public LocalizedText Description => "Toggles something";
		public IReadOnlyList<ActionParameter> Parameters => [];

		public IActionExecutor CreateExecutor() => new CapturingActionDefinition().CreateExecutor();

		public Task<ActionStateSnapshot?> GetActionStateAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
			=> Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot([new ActionStateDefinition("on", "On")],
				"on"));
	}
}

internal sealed class FakeIconProviderActionIntegration : IIntegration
{
	public string Id { get; init; } = "fake-icon-provider-actions";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; } =
		[new CapturingActionDefinition { Id = "plain" }, new IconProvidingActionDefinition()];

	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	private sealed class IconProvidingActionDefinition : IActionDefinition, IIconProviderActionDefinition
	{
		public string Id => "show-icon";
		public LocalizedText Name => "Show icon";
		public LocalizedText Description => "Shows something";
		public IReadOnlyList<ActionParameter> Parameters => [];

		public IActionExecutor CreateExecutor() => new CapturingActionDefinition().CreateExecutor();

		public Task<ActionIconSnapshot?> GetActionIconAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
			=> Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot { Version = "v1", MediaType = "image/png" });
	}
}

internal sealed class FakeConfigurableMusicPlayerIntegration : IIntegration, IVariableProvider, IMusicPlayerProvider,
	IConfigFlowProvider
{
	public string Id { get; init; } = "fake-configurable-music-player";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions { get; init; } = [new CapturingActionDefinition { Id = "a1" }];
	public bool IsInitialized { get; set; }

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables => [];

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; init; } =
	[
		VariableDefinition.Eager("v1", MacroDeck.Sdk.Variables.VariableType.Text),
		VariableDefinition.Eager("v2", MacroDeck.Sdk.Variables.VariableType.Text)
	];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableReading.Unavailable);

	public string ProviderName => Id;
	public IReadOnlyList<MusicPlayerInstance> GetInstances() => [];
	public IMusicPlayer? GetPlayer(string instanceId) => null;

	public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
}

internal sealed class FakeThrowingProviderCatalogsIntegration :
	IIntegration,
	IVariableProvider,
	IEventProvider,
	IMusicPlayerProvider,
	IWeatherProvider,
	IProfileProvider
{
	public string Id { get; init; } = "fake-throwing-provider-catalogs";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;
	public int InitializeCallCount { get; private set; }

	public Task InitializeAsync(IIntegrationContext context)
	{
		InitializeCallCount++;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; init; } = [];

	public IReadOnlyList<VariableDefinition> Variables => throw new InvalidOperationException();

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> throw new InvalidOperationException();

	public IReadOnlyList<EventDefinition> EventDefinitions => throw new InvalidOperationException();

	public IReadOnlyList<MusicPlayerInstance> GetInstances() => throw new InvalidOperationException();
	public IMusicPlayer? GetPlayer(string instanceId) => throw new InvalidOperationException();

	IReadOnlyList<WeatherStationInstance> IWeatherProvider.GetInstances() => throw new InvalidOperationException();
	public IWeatherStation? GetStation(string instanceId) => throw new InvalidOperationException();

	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => throw new InvalidOperationException();
}

internal sealed class FakeOrderIntegrationA : IMusicPlayerProvider, IVariableProvider, IEventProvider, IIntegration
{
	public string Id { get; init; } = "order-a";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [new CapturingActionDefinition { Id = "a1" }];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;
	public IReadOnlyList<EventDefinition> EventDefinitions => [];
	public IReadOnlyList<VariableDefinition> Variables => [];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableReading.Unavailable);

	public IReadOnlyList<MusicPlayerInstance> GetInstances() => [];
	public IMusicPlayer? GetPlayer(string instanceId) => null;
}

internal sealed class FakeOrderIntegrationB : IProfileProvider, IWeatherProvider, IVariableProvider, IEventProvider,
	IIntegration
{
	public string Id { get; init; } = "order-b";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public string ProviderName => Id;
	public IReadOnlyList<EventDefinition> EventDefinitions => [];
	public IReadOnlyList<VariableDefinition> Variables => [];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableReading.Unavailable);

	IReadOnlyList<WeatherStationInstance> IWeatherProvider.GetInstances() => [];
	public IWeatherStation? GetStation(string instanceId) => null;
	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => [];
}

internal sealed class FakeOrderIntegrationC : IVariableProvider, IProfileProvider, IWeatherProvider, IIntegration
{
	public string Id { get; init; } = "order-c";
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [new CapturingActionDefinition { Id = "a1" }];
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public IReadOnlyList<VariableDefinition> Variables => [];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableReading.Unavailable);

	public string ProviderName => Id;
	IReadOnlyList<WeatherStationInstance> IWeatherProvider.GetInstances() => [];
	public IWeatherStation? GetStation(string instanceId) => null;
	public IReadOnlyList<VirtualProfileDescriptor> GetProfiles() => [];
}

internal sealed class FakeDeckNavigator : IDeckNavigator
{
	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public IReadOnlyList<DeckFolder> GetFolders() => [];

	public IReadOnlyList<DeckProfile> GetProfiles() => [];
}

internal sealed class FakeScriptApi : IScriptApi
{
	public IReadOnlyList<Script> GetScripts() => [];

	public Task<ActionResult> RunAsync(string scriptId,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? originClientId = null,
		string? ownerWidgetId = null,
		CancellationToken cancellationToken = default) => ActionResult.SucceededTask;
}

internal sealed class FakeWidgetApi : IWidgetApi
{
	public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

	public bool Exists(string widgetId) => false;

	public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
		=> Task.FromResult(false);

	public Task<WidgetStateWriteResult> SetStateAsync(
		string widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

	public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
}

internal sealed class FakeUserVariableApi : IUserVariableApi
{
	public Task<UserVariableWriteResult> ApplyAsync(string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(UserVariableWriteResult.Applied());
}

internal sealed class ThrowingVariableService : IVariableService
{
	public Task<IReadOnlyList<VariableEntity>> GetAll() => throw new NotSupportedException();

	public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
		=> throw new NotSupportedException();

	public Task<VariableEntity?> GetById(Guid id) => throw new NotSupportedException();

	public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
		=> throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> CreateUserVariable(string name,
		VariableScope scope,
		string? scopeRefId,
		DomainVariableType type,
		object? initialValue,
		int? decimalPlaces) => throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
		object? value,
		CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id,
		string? name,
		int? decimalPlaces) => throw new NotSupportedException();

	public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(string integrationId,
		string name,
		VariableScope scope,
		string? scopeRefId,
		DomainVariableType type,
		object? initialValue,
		int? decimalPlaces,
		string? definitionId = null,
		VariableDeclaration? declaration = null,
		VariableUpdateMode updateMode = VariableUpdateMode.Polled) => throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
		string resourceId,
		string name,
		Domain.Enums.VariableType type,
		int? decimalPlaces,
		VariableDeclaration? declaration = null) =>
		throw new NotSupportedException();

	public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId) =>
		Task.FromResult<VariableEntity?>(null);

	public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(string integrationId,
		Guid id,
		object? value,
		VariableBounds? bounds = null) => throw new NotSupportedException();

	public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
		Guid id,
		bool available) => throw new NotSupportedException();

	public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
		=> throw new NotSupportedException();

	public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
		=> throw new NotSupportedException();

	public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => throw new NotSupportedException();

	public Task UpsertWidgetVariable(VariableScope scope,
		string scopeRefId,
		string name,
		DomainVariableType type,
		object? value) => throw new NotSupportedException();

	public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
		=> throw new NotSupportedException();
}
