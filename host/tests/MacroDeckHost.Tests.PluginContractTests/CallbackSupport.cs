using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Events;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.PluginContractTests;

// ---------------------------------------------------------------------------------------------
// Host-side fakes backing a real PluginCallbackRouter for CallbackContractTests. Hand-rolled to
// match the style already established by PluginCallbackRouterTests (host/tests/MacroDeckHost.Tests.UnitTests) -
// that project cannot be depended on from here (it is a sibling test assembly, not a library), so
// these are separate copies rather than a shared reference.
// ---------------------------------------------------------------------------------------------

internal sealed class CallbackFakeVariableService : IVariableService
{
	private readonly Dictionary<Guid, VariableEntity> _byId = [];

	public string? ValueOf(Guid id) => _byId.TryGetValue(id, out var entity) ? entity.Value : null;

	public Task<IReadOnlyList<VariableEntity>> GetAll() =>
		Task.FromResult<IReadOnlyList<VariableEntity>>([.. _byId.Values]);

	public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
		=> throw new NotSupportedException();

	public Task<VariableEntity?> GetById(Guid id) => Task.FromResult(_byId.GetValueOrDefault(id));

	public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
		=> Task.FromResult(_byId.Values.FirstOrDefault(entity => entity.Name == name));

	public Task<Result<VariableEntity, VariableError>> CreateUserVariable(string name,
		VariableScope scope,
		string? scopeRefId,
		DomainVariableType type,
		object? initialValue,
		int? decimalPlaces)
		=> throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
		object? value,
		CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id,
		string? name,
		int? decimalPlaces)
		=> throw new NotSupportedException();

	public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(
		string integrationId,
		string name,
		VariableScope scope,
		string? scopeRefId,
		DomainVariableType type,
		object? initialValue,
		int? decimalPlaces,
		string? definitionId = null,
		VariableDeclaration? declaration = null,
		VariableUpdateMode updateMode = VariableUpdateMode.Polled)
	{
		var entity = new VariableEntity
		{
			Id = Guid.CreateVersion7(),
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = type,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = integrationId,
			DefinitionId = definitionId,
			Value = initialValue?.ToString() ?? string.Empty,
			DecimalPlaces = decimalPlaces,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
			Presentation = declaration?.Presentation
		};

		_byId[entity.Id] = entity;
		return Task.FromResult(Result.Ok<VariableEntity, VariableError>(entity));
	}

	public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
		string resourceId,
		string name,
		Domain.Enums.VariableType type,
		int? decimalPlaces,
		VariableDeclaration? declaration = null) =>
		throw new NotSupportedException();

	public Task<VariableEntity?> GetByDefinition(MacroDeck.Sdk.Identity.QualifiedId definitionId)
		=> throw new NotSupportedException();

	public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(string integrationId,
		Guid id,
		object? value,
		VariableBounds? bounds = null)
	{
		if (!_byId.TryGetValue(id, out var entity))
		{
			return Task.FromResult(Result.Fail<VariableEntity, VariableError>(VariableError.NotFound));
		}

		if (entity.OwnerIntegrationId != integrationId)
		{
			return Task.FromResult(Result.Fail<VariableEntity, VariableError>(VariableError.NotOwnedByIntegration));
		}

		entity.Value = value?.ToString() ?? string.Empty;
		return Task.FromResult(Result.Ok<VariableEntity, VariableError>(entity));
	}

	public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId, Guid id, bool available)
		=> throw new NotSupportedException();

	public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
	{
		if (!_byId.TryGetValue(id, out var entity))
		{
			return Task.FromResult(Result.Fail<VariableError>(VariableError.NotFound));
		}

		if (entity.OwnerIntegrationId != integrationId)
		{
			return Task.FromResult(Result.Fail<VariableError>(VariableError.NotOwnedByIntegration));
		}

		_byId.Remove(id);
		return Task.FromResult(Result.Ok<VariableError>());
	}

	public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
		=> Task.FromResult<IReadOnlyList<VariableEntity>>([
			.. _byId.Values.Where(entity => entity.OwnerIntegrationId == integrationId)
		]);

	public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => throw new NotSupportedException();

	public Task UpsertWidgetVariable(VariableScope scope,
		string scopeRefId,
		string name,
		DomainVariableType type,
		object? value) => throw new NotSupportedException();

	public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
		=> throw new NotSupportedException();
}

internal sealed class CallbackFakeUserVariableApi : IUserVariableApi
{
	public List<(string Name, string? OwnerWidgetId, UserVariableOperation Operation, string? Value)> Applied { get; } =
		[];

	public UserVariableWriteResult ResultToReturn { get; set; } = UserVariableWriteResult.Applied();

	public List<(string Name, string? OwnerWidgetId, SdkVariableType Type, string? InitialValue, int? DecimalPlaces)>
		Created { get; } = [];

	public UserVariableCreateResult CreateResultToReturn { get; set; } = UserVariableCreateResult.Created();

	public Task<UserVariableWriteResult> ApplyAsync(string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		Applied.Add((name, ownerWidgetId, operation, value));
		return Task.FromResult(ResultToReturn);
	}

	public Task<UserVariableCreateResult> CreateAsync(string name,
		string? ownerWidgetId,
		SdkVariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		Created.Add((name, ownerWidgetId, type, initialValue, decimalPlaces));
		return Task.FromResult(CreateResultToReturn);
	}
}

internal sealed class CallbackFakeHostLockState : IHostLockState
{
	public bool IsSupported => true;

	public bool IsLocked => false;
}

internal sealed class CallbackFakeDeckNavigator : IDeckNavigator
{
	public List<string> ChangedFolders { get; } = [];

	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
	{
		ChangedFolders.Add(folderId);
		return Task.CompletedTask;
	}

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

internal sealed class CallbackFakeScriptApi : IScriptApi
{
	public List<string> RanScripts { get; } = [];

	public ActionResult ResultToReturn { get; set; } = ActionResult.Success();

	public IReadOnlyList<Script> GetScripts() => [];

	public Task<ActionResult> RunAsync(string scriptId,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? ownerWidgetId = null,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
	{
		RanScripts.Add(scriptId);
		return Task.FromResult(ResultToReturn);
	}
}

internal sealed class CallbackFakeWidgetApi : IWidgetApi
{
	public List<string> AppliedWidgetIds { get; } = [];

	public bool ResultToReturn { get; set; } = true;

	public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

	public bool Exists(string widgetId) => false;

	public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
	{
		AppliedWidgetIds.Add(request.WidgetId);
		return Task.FromResult(ResultToReturn);
	}

	public Task<WidgetStateWriteResult> SetStateAsync(
		string widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

	public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
}

internal sealed class CallbackFakeWidgetIconInvalidator : IWidgetIconInvalidator
{
	public List<(string IntegrationId, string ActionId)> Invalidations { get; } = [];

	public void Invalidate(string integrationId, string actionId) => Invalidations.Add((integrationId, actionId));
}

internal sealed class CallbackFakeNotificationStore : IUserNotificationStore
{
	public List<UserNotificationDraft> Raised { get; } = [];

	public List<string> DismissedKeys { get; } = [];

	public int Capacity => 100;

	public UserNotification? Raise(UserNotificationDraft draft)
	{
		Raised.Add(draft);
		return null;
	}

	public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => Raise(draft);

	public IReadOnlyList<UserNotification> Snapshot() => [];

	public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress) => false;

	public bool DismissByKey(string dedupeKey)
	{
		DismissedKeys.Add(dedupeKey);
		return true;
	}

	public bool Dismiss(string id) => false;

	public bool Retire(string dedupeKey) => false;

	public bool DismissAll() => false;

	public event Action? Changed
	{
		add { }
		remove { }
	}
}

internal sealed class CallbackFakeActionInteractions : IActionInteractions
{
	public List<(string? OriginClientId, string InstanceId, MusicPlayerCatalogItemKind Kind, string? Prompt)>
		ItemPickerRequests { get; } = [];

	public List<(string? OriginClientId, string InstanceId, bool StartPlayback, string? Prompt)> DevicePickerRequests
	{
		get;
	} = [];

	public void RequestItemPicker(string? originClientId,
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? prompt = null)
		=> ItemPickerRequests.Add((originClientId, instanceId, kind, prompt));

	public void RequestDevicePicker(string? originClientId,
		string instanceId,
		bool startPlayback,
		string? prompt = null)
		=> DevicePickerRequests.Add((originClientId, instanceId, startPlayback, prompt));
}

internal sealed class CallbackFakeCapabilityInvoker : IPluginCapabilityInvoker
{
	public HashSet<(string PluginId, string CorrelationId)> LiveExecuteCorrelations { get; } = [];

	public Task<JsonElement?> InvokeAsync(string pluginId,
		CapabilityInvokeRequest request,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException("Not exercised by the callback direction.");

	public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

	public void AbortAll(string pluginId, ProtocolError reason)
	{
	}

	public bool IsLiveActionExecute(string pluginId, string correlationId)
		=> LiveExecuteCorrelations.Contains((pluginId, correlationId));
}

internal sealed class CallbackFakeEventBus : IEventBus
{
	private readonly Channel<EventOccurrence> _channel = Channel.CreateUnbounded<EventOccurrence>();

	public List<EventOccurrence> Published { get; } = [];

	public void Publish(EventOccurrence occurrence)
	{
		Published.Add(occurrence);
		_channel.Writer.TryWrite(occurrence);
	}

	public ChannelReader<EventOccurrence> Reader => _channel.Reader;
}

internal sealed class CallbackFakeConfigStore : IIntegrationConfigStore
{
	private readonly Dictionary<Guid, ConfigEntryRecord> _byId = [];

	public Guid Seed(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
	{
		var record = new ConfigEntryRecord(Guid.CreateVersion7(), integrationId, title, DateTime.UtcNow, values);
		_byId[record.Id] = record;
		return record.Id;
	}

	public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySummary>>([
			.. _byId.Values.Where(e => e.IntegrationId == integrationId)
				.Select(e => new ConfigEntrySummary(e.Id, e.IntegrationId, e.Title, e.CreatedAt))
		]);

	public Task<ConfigEntryRecord?> Find(Guid entryId) => Task.FromResult(_byId.GetValueOrDefault(entryId));

	public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
		=> Task.FromResult(Seed(integrationId, title, values));

	public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
	{
		if (!_byId.TryGetValue(entryId, out var record))
		{
			return Task.FromResult(false);
		}

		_byId[entryId] = record with { Values = values };
		return Task.FromResult(true);
	}

	public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
		=> throw new NotSupportedException();

	public Task Delete(Guid entryId) => throw new NotSupportedException();
}

internal sealed class CallbackFakeSecretService : ISecretService
{
	private readonly Dictionary<Guid, string> _byId = [];

	public Task<Guid> Create(string value, SecretKind kind)
	{
		var id = Guid.CreateVersion7();
		_byId[id] = value;
		return Task.FromResult(id);
	}

	public Task<bool> Replace(Guid id, string value)
	{
		if (!_byId.ContainsKey(id))
		{
			return Task.FromResult(false);
		}

		_byId[id] = value;
		return Task.FromResult(true);
	}

	public Task<bool> Delete(Guid id) => Task.FromResult(_byId.Remove(id));

	public Task<Guid?> Clone(Guid id)
	{
		if (!_byId.TryGetValue(id, out var value))
		{
			return Task.FromResult<Guid?>(null);
		}

		var clone = Guid.CreateVersion7();
		_byId[clone] = value;
		return Task.FromResult<Guid?>(clone);
	}

	public Task<string?> Reveal(Guid id) => Task.FromResult(_byId.GetValueOrDefault(id));

	public Task<string?> Resolve(Guid id) => Task.FromResult(_byId.GetValueOrDefault(id));

	public Task<SecretMaterial?> ExportForArchive(Guid id) => throw new NotSupportedException();
}

internal sealed class CallbackHostLink(
	string pluginId,
	string sessionId,
	IPluginCallbackRouter router,
	IEventBus eventBus)
	: IPluginSocket
{
	private readonly Channel<byte[]> _toPlugin = Channel.CreateUnbounded<byte[]>();

	public int? CloseCode { get; private set; }

	public string? CloseDescription { get; private set; }

	public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _toPlugin.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (ChannelClosedException)
		{
			return null;
		}
	}

	public Task SendAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken)
	{
		var read = ProtocolEnvelopeReader.Read(utf8.Span);
		if (read.Envelope is not { } envelope)
		{
			return Task.CompletedTask;
		}

		switch (envelope.Type)
		{
			case MessageTypes.SessionHello:
				RespondWelcome(envelope);
				break;

			case MessageTypes.SessionPing:
				Push(new ProtocolEnvelope
				{
					Type = MessageTypes.SessionPong, Id = Guid.CreateVersion7().ToString(), CorrelationId = envelope.Id
				});
				break;

			case MessageTypes.HostInvoke:
				_ = HandleHostInvokeAsync(envelope);
				break;

			case MessageTypes.EventPublish:
				HandleEventPublish(envelope);
				break;

			default:
				break;
		}

		return Task.CompletedTask;
	}

	public void PushHostState(string api, object? data)
		=> Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = api,
					Data = data is null ? null : JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});

	public Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellationToken)
	{
		CloseCode ??= (int)status;
		CloseDescription = description;
		_toPlugin.Writer.TryComplete();
		return Task.CompletedTask;
	}

	public Task AbortAsync()
	{
		_toPlugin.Writer.TryComplete();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		_toPlugin.Writer.TryComplete();
		return ValueTask.CompletedTask;
	}

	private async Task HandleHostInvokeAsync(ProtocolEnvelope envelope)
	{
		HostInvokePayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<HostInvokePayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null)
		{
			return;
		}

		var result = await router.RouteAsync(pluginId, envelope.Id, payload, CancellationToken.None)
			.ConfigureAwait(false);

		Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = envelope.Id,
			Error = result.Error,
			Payload = result.Error is null
				? JsonSerializer.SerializeToElement(new HostResultPayload { Data = result.Data },
					PluginProtocolJson.Options)
				: null
		});
	}

	private void HandleEventPublish(ProtocolEnvelope envelope)
	{
		EventPublishPayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<EventPublishPayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null || string.IsNullOrEmpty(payload.EventId))
		{
			return;
		}

		var parameters = ToParameterDictionary(payload.Parameters);
		new IntegrationEventPublisher(pluginId, eventBus, Serilog.Log.Logger).Publish(payload.EventId, parameters);
	}

	private static Dictionary<string, object?>? ToParameterDictionary(JsonElement? element)
	{
		if (element is not { ValueKind: JsonValueKind.Object } value)
		{
			return null;
		}

		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
		{
			result[property.Name] = property.Value.ValueKind switch
			{
				JsonValueKind.String => property.Value.GetString(),
				JsonValueKind.Number => property.Value.TryGetInt64(out var i) ? i : property.Value.GetDouble(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				_ => null
			};
		}

		return result;
	}

	private void RespondWelcome(ProtocolEnvelope hello)
	{
		var payload = hello.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);

		Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = hello.Id,
			Payload = JsonSerializer.SerializeToElement(new SessionWelcomePayload
				{
					SessionId = payload?.SessionId ?? sessionId,
					Resumed = !string.IsNullOrEmpty(payload?.ResumeSessionId)
				},
				PluginProtocolJson.Options)
		});
	}

	private void Push(ProtocolEnvelope envelope) =>
		_toPlugin.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope));
}
