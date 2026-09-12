using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Ui.Model.Versioning;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public class ConfigFlowManager : IConfigFlowManager
{
	private readonly IIntegrationRegistry _registry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IIntegrationLifecycle _lifecycle;
	private readonly IOAuthCallbackCoordinator _oauth;
	private readonly IHostListenerState _listenerState;
	private readonly IRemotePluginSnapshotStore _snapshots;
	private readonly ConfigFlowUiProviderRegistry _uiProviders;
	private readonly UiSessionRegistry _uiSessions;
	private readonly IUiSessionBroker _uiSessionBroker;
	private readonly ILogger _logger;
	private readonly IIntegrationConfigMutationCoordinator? _mutations;
	private readonly ConcurrentDictionary<Guid, ActiveFlow> _flows = new();

	public ConfigFlowManager(
		IIntegrationRegistry registry,
		IServiceScopeFactory scopeFactory,
		IIntegrationLifecycle lifecycle,
		IOAuthCallbackCoordinator oauth,
		IHostListenerState listenerState,
		IRemotePluginSnapshotStore snapshots,
		ConfigFlowUiProviderRegistry uiProviders,
		UiSessionRegistry uiSessions,
		IUiSessionBroker uiSessionBroker,
		ILogger logger,
		IIntegrationConfigMutationCoordinator? mutations = null)
	{
		_registry = registry;
		_scopeFactory = scopeFactory;
		_lifecycle = lifecycle;
		_oauth = oauth;
		_listenerState = listenerState;
		_snapshots = snapshots;
		_uiProviders = uiProviders;
		_uiSessions = uiSessions;
		_uiSessionBroker = uiSessionBroker;
		_logger = logger;
		_mutations = mutations;
	}

	public Task<ConfigFlowStartOutcome> StartAsync(string integrationId, CancellationToken cancellationToken)
		=> StartAsync(integrationId, null, null, cancellationToken);

	public async Task<ConfigFlowStartOutcome> StartAsync(
		string integrationId,
		string? title,
		Guid? entryId,
		CancellationToken cancellationToken)
	{
		var integration = _registry.Integrations.FirstOrDefault(i => i.Id == integrationId);
		if (integration is not IConfigFlowProvider provider)
		{
			return new ConfigFlowStartOutcome(false, Guid.Empty, null);
		}

		ConfigEntryRecord? existingEntry = null;
		if (entryId is { } requestedEntryId)
		{
			await using var editScope = _scopeFactory.CreateAsyncScope();
			var configStore = editScope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
			existingEntry = await configStore.Find(requestedEntryId);
			if (existingEntry is null ||
				!string.Equals(existingEntry.IntegrationId, integrationId, StringComparison.Ordinal))
			{
				return new ConfigFlowStartOutcome(true,
					Guid.Empty,
					null,
					await ActiveLocalization.Resolve(editScope.ServiceProvider,
						AppStrings.Errors.Config.EntryNotFound()));
			}
		}
		else if (!provider.AllowsMultipleConfigurations)
		{
			await using var checkScope = _scopeFactory.CreateAsyncScope();
			var configStore = checkScope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
			var existing = await configStore.List(integrationId);
			if (existing.Count > 0)
			{
				existingEntry = await configStore.Find(existing[0].Id);
				_logger.Information(
					"Config flow for single-configuration integration '{IntegrationId}' reconfigures entry {EntryId}",
					integrationId,
					existingEntry?.Id);
			}
		}

		var flowId = Guid.NewGuid();
		var registration = _oauth.Register(flowId);
		var context = new ConfigFlowContext(_oauth,
			registration,
			_listenerState.PublicListenerAvailable,
			existingEntry?.Title ?? title);
		var flow = provider.CreateConfigFlow();

		ConfigFlowResult result;
		try
		{
			result = await flow.StartAsync(context, cancellationToken);
		}
		catch (PublicListenerUnavailableException ex)
		{
			// The flow read context.OAuth before returning anything to keep; nothing was stored, so
			// just release the registration this attempt reserved.
			_oauth.Release(registration.State);
			return new ConfigFlowStartOutcome(true, Guid.Empty, null, ex.Message);
		}

		var active = new ActiveFlow(integrationId,
			integration.Name,
			flow,
			context,
			registration.State,
			existingEntry?.Id ?? Guid.NewGuid(),
			existingEntry,
			title);
		if (existingEntry is not null)
		{
			foreach (var (key, value) in existingEntry.Values)
			{
				active.Values[key] = value;
			}
		}

		RememberSecretFields(active, result.NextStep);
		_flows[flowId] = active;

		var (supportsConfigUi, configUiModelVersion) = DescribeConfigUi(integrationId, provider);

		if (flow is IUiConfigFlow uiFlow)
		{
			_uiProviders.Register(flowId, uiFlow);
		}

		var initialValues = active.Values
			.Where(value => !SecretReferenceJson.TryGet(value.Value, out _))
			.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
		var storedSecrets = active.Values
			.Where(value => SecretReferenceJson.TryGet(value.Value, out _))
			.Select(value => value.Key)
			.ToHashSet(StringComparer.Ordinal);

		return new ConfigFlowStartOutcome(true,
			flowId,
			result.NextStep,
			null,
			supportsConfigUi,
			configUiModelVersion,
			initialValues,
			storedSecrets);
	}

	/// <summary>The legacy field list is served either way - this only tells a client whether it may
	/// also try to open a UI session for the flow, never whether one already exists.</summary>
	private (bool SupportsConfigUi, int ConfigUiModelVersion) DescribeConfigUi(
		string integrationId,
		IConfigFlowProvider provider)
	{
		if (provider is IUiConfigFlowProvider { ServesConfigUiTree: true })
		{
			return (true, UiModelVersions.Current);
		}

		if (_registry.GetOrigin(integrationId) == IntegrationOrigin.Plugin && _snapshots.Has(integrationId))
		{
			var snapshot = _snapshots.GetSnapshot(integrationId);
			return (snapshot.ServesConfigUiTree, snapshot.UiModelVersion);
		}

		return (false, 0);
	}

	public bool TryGetActiveFlow(Guid flowId, out ConfigFlowActiveFlowInfo? info)
	{
		if (_flows.TryGetValue(flowId, out var active))
		{
			info = new ConfigFlowActiveFlowInfo(active.IntegrationId, active.Flow);
			return true;
		}

		info = null;
		return false;
	}

	public async Task<ConfigFlowSubmitOutcome> SubmitAsync(
		Guid flowId,
		string stepId,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken)
		=> await SubmitAsync(flowId, stepId, values, [], cancellationToken);

	public async Task<ConfigFlowSubmitOutcome> SubmitAsync(
		Guid flowId,
		string stepId,
		IReadOnlyDictionary<string, JsonElement> values,
		IReadOnlyCollection<string> clearedSecretFields,
		CancellationToken cancellationToken)
	{
		if (!_flows.TryGetValue(flowId, out var active))
		{
			return new ConfigFlowSubmitOutcome(false, ConfigFlowResultKind.Error, null, null, null, null);
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var secretService = scope.ServiceProvider.GetRequiredService<ISecretService>();
		var valuesBeforeSubmit = new Dictionary<string, JsonElement>(active.Values, StringComparer.Ordinal);
		var replacedSecretsBeforeSubmit = new HashSet<string>(active.ReplacedSecretFields, StringComparer.Ordinal);

		void RollBackSubmittedValues()
		{
			active.Values.Clear();
			foreach (var (key, value) in valuesBeforeSubmit)
			{
				active.Values[key] = value;
			}

			active.ReplacedSecretFields.Clear();
			active.ReplacedSecretFields.UnionWith(replacedSecretsBeforeSubmit);
		}

		foreach (var (key, value) in values)
		{
			if (active.SecretFields.Contains(key) && value.ValueKind == JsonValueKind.String)
			{
				if (string.IsNullOrEmpty(value.GetString()) &&
					active.Values.TryGetValue(key, out var retained) &&
					SecretReferenceJson.TryGet(retained, out _))
				{
					continue;
				}

				if (!string.IsNullOrEmpty(value.GetString()))
				{
					active.ReplacedSecretFields.Add(key);
				}
			}

			active.Values[key] = value;
		}

		foreach (var name in clearedSecretFields.Where(active.SecretFields.Contains))
		{
			active.Values.Remove(name);
			active.ReplacedSecretFields.Remove(name);
		}

		ConfigFlowResult result;
		try
		{
			var input = await ResolveInput(active.Values, secretService);
			result = await active.Flow.SubmitAsync(stepId, input, active.Context, cancellationToken);
		}
		catch (PublicListenerUnavailableException ex)
		{
			RollBackSubmittedValues();
			return new ConfigFlowSubmitOutcome(true, ConfigFlowResultKind.Error, null, ex.Message, null, null);
		}
		catch
		{
			RollBackSubmittedValues();
			throw;
		}

		RememberSecretFields(active, result.NextStep);

		switch (result.Kind)
		{
			case ConfigFlowResultKind.Step:
				return new ConfigFlowSubmitOutcome(true, result.Kind, result.NextStep, null, null, null);

			case ConfigFlowResultKind.Error:
				RollBackSubmittedValues();
				return new ConfigFlowSubmitOutcome(true,
					result.Kind,
					result.NextStep,
					result.ErrorMessage,
					result.FieldErrors,
					null);

			case ConfigFlowResultKind.External:
				return new ConfigFlowSubmitOutcome(true,
					result.Kind,
					null,
					null,
					null,
					null,
					result.ExternalUrl,
					result.ResumeStepId);

			case ConfigFlowResultKind.Complete when _mutations is not null &&
				!_mutations.CreatesEntriesFromFlow(active.IntegrationId):
				// An integration that creates its entries itself: completing only closes the flow, so no
				// entry without the thing it is keyed by can come out of the dialog.
				_flows.TryRemove(flowId, out _);
				_oauth.Release(active.OAuthState);
				await CloseConfigUiSessionsAsync(flowId, cancellationToken);
				return new ConfigFlowSubmitOutcome(true, result.Kind, null, null, null, null);

			case ConfigFlowResultKind.Complete:
				await MergeCompletionValues(result.Values,
					active.Values,
					active.SecretFields,
					active.ReplacedSecretFields,
					secretService);
				await EncryptSecretFields(active, secretService);

				// The entry title is stored, so it is resolved once here rather than kept as a reference:
				// stored state must not change shape, and a title the user can rename is theirs from then on.
				var title = active.ExistingEntry?.Title ??
					active.RequestedTitle ??
					result.EntryTitle ??
					await ActiveLocalization.Resolve(scope.ServiceProvider, active.IntegrationName);

				Guid entryId;
				var reused = active.ExistingEntry is not null;
				if (_mutations is not null)
				{
					var mutation = await _mutations.CompleteAsync(active.IntegrationId,
						active.EntryId,
						title,
						active.Values,
						cancellationToken);
					if (!mutation.Success)
					{
						RollBackSubmittedValues();
						return new ConfigFlowSubmitOutcome(true,
							ConfigFlowResultKind.Error,
							result.NextStep,
							mutation.Error,
							null,
							null);
					}

					entryId = mutation.EntryId!.Value;
				}
				else
				{
					var store = scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
					if (reused)
					{
						await _lifecycle.ShutdownAsync(active.IntegrationId, cancellationToken);
						entryId = await store.Replace(active.EntryId, title, active.Values)
							? active.EntryId
							: await store.Create(active.IntegrationId, title, active.Values);
					}
					else
					{
						entryId = await store.Create(active.IntegrationId, title, active.Values);
					}
				}

				_flows.TryRemove(flowId, out _);
				_oauth.Release(active.OAuthState);
				await CloseConfigUiSessionsAsync(flowId, cancellationToken);

				if (_mutations is null)
				{
					_registry.SetEnabled(active.IntegrationId, true);
					await _lifecycle.ReinitializeAsync(active.IntegrationId, cancellationToken);
				}

				_logger.Information("Config flow for '{IntegrationId}' completed; entry {EntryId} {Outcome}",
					active.IntegrationId,
					entryId,
					reused ? "reconfigured" : "created");

				return new ConfigFlowSubmitOutcome(true, result.Kind, null, null, null, entryId);

			default:
				return new ConfigFlowSubmitOutcome(true, ConfigFlowResultKind.Error, null, null, null, null);
		}
	}

	public async Task AbandonAsync(Guid flowId, CancellationToken cancellationToken)
	{
		if (!_flows.TryRemove(flowId, out var active))
		{
			return;
		}

		_oauth.Release(active.OAuthState);
		await CloseConfigUiSessionsAsync(flowId, cancellationToken);

		switch (active.Flow)
		{
			case IAsyncDisposable asyncDisposable:
				await asyncDisposable.DisposeAsync().ConfigureAwait(false);
				break;
			case IDisposable disposable:
				disposable.Dispose();
				break;
		}

		_logger.Information("Config flow for '{IntegrationId}' abandoned", active.IntegrationId);
	}

	/// <summary>Closes whatever UI session this flow's tree is rendering in, on both ways a flow ends -
	/// the tree renders a transaction it does not own, so nothing here persists anything; it only stops
	/// a session that has nothing left to talk to.</summary>
	private async Task CloseConfigUiSessionsAsync(Guid flowId, CancellationToken cancellationToken)
	{
		_uiProviders.Unregister(flowId);

		var providerId = ConfigFlowUiProviderRegistry.ProviderIdFor(flowId);
		foreach (var session in _uiSessions.SessionsForProvider(providerId))
		{
			await _uiSessionBroker.CloseAsync(session.SessionId, "The configuration flow ended.", cancellationToken)
				.ConfigureAwait(false);
		}
	}

	private static void RememberSecretFields(ActiveFlow active, ConfigFlowStep? step)
	{
		if (step is null)
		{
			return;
		}

		foreach (var field in step.Fields.Concat(step.AdvancedFields))
		{
			if (field.Type is ActionParameterType.Secret or ActionParameterType.Password)
			{
				active.SecretFields.Add(field.Name);
			}
		}
	}

	private static async Task EncryptSecretFields(ActiveFlow active, ISecretService secretService)
	{
		foreach (var name in active.SecretFields)
		{
			if (!active.Values.TryGetValue(name, out var element) ||
				SecretReferenceJson.TryGet(element, out _) ||
				element.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			var plaintext = element.GetString();
			if (string.IsNullOrEmpty(plaintext))
			{
				continue;
			}

			var secretId = await secretService.Create(plaintext, SecretKind.Secret);
			active.Values[name] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{ [SecretReferenceJson.PropertyName] = secretId.ToString() });
		}
	}

	private static async Task MergeCompletionValues(
		IReadOnlyDictionary<string, ConfigFlowValue>? values,
		Dictionary<string, JsonElement> target,
		HashSet<string> declaredSecretFields,
		HashSet<string> replacedSecretFields,
		ISecretService secretService)
	{
		if (values is null)
		{
			return;
		}

		foreach (var (key, value) in values)
		{
			if (value.IsSecret)
			{
				if (declaredSecretFields.Contains(key) &&
					!replacedSecretFields.Contains(key) &&
					target.TryGetValue(key, out var retained) &&
					SecretReferenceJson.TryGet(retained, out _))
				{
					continue;
				}

				if (string.IsNullOrEmpty(value.Value))
				{
					continue;
				}

				var secretId = await secretService.Create(value.Value, SecretKind.Secret);
				target[key] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
					{ [SecretReferenceJson.PropertyName] = secretId.ToString() });
			}
			else
			{
				target[key] = JsonSerializer.SerializeToElement(value.Value);
			}
		}
	}

	private static async Task<IReadOnlyDictionary<string, object?>> ResolveInput(
		Dictionary<string, JsonElement> values,
		ISecretService secretService)
	{
		var resolved = new Dictionary<string, object?>(values.Count);

		foreach (var (key, element) in values)
		{
			if (SecretReferenceJson.TryGet(element, out var secretId))
			{
				resolved[key] = await secretService.Resolve(secretId);
				continue;
			}

			resolved[key] = ToClrValue(element);
		}

		return resolved;
	}

	private static object? ToClrValue(JsonElement element)
		=> element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => element.GetRawText()
		};

	private sealed class ActiveFlow
	{
		public ActiveFlow(
			string integrationId,
			LocalizedText integrationName,
			IConfigFlow flow,
			IConfigFlowContext context,
			string oauthState,
			Guid entryId,
			ConfigEntryRecord? existingEntry,
			string? requestedTitle)
		{
			IntegrationId = integrationId;
			IntegrationName = integrationName;
			Flow = flow;
			Context = context;
			OAuthState = oauthState;
			EntryId = entryId;
			ExistingEntry = existingEntry;
			RequestedTitle = requestedTitle;
		}

		public string IntegrationId { get; }

		public LocalizedText IntegrationName { get; }

		public IConfigFlow Flow { get; }

		public IConfigFlowContext Context { get; }

		public string OAuthState { get; }

		public Guid EntryId { get; }

		public ConfigEntryRecord? ExistingEntry { get; }

		public string? RequestedTitle { get; }

		public Dictionary<string, JsonElement> Values { get; } = new();

		public HashSet<string> SecretFields { get; } = new();

		public HashSet<string> ReplacedSecretFields { get; } = new();
	}

	private sealed class PublicListenerUnavailableException()
		: Exception("The public network port could not be opened, so this OAuth login cannot complete. " +
			"Choose a different port in Settings > Network and try again.");

	private sealed class ConfigFlowContext : IConfigFlowEntryContext
	{
		private readonly IOAuthCallbackCoordinator _coordinator;
		private readonly OAuthRegistration _registration;
		private readonly bool _publicListenerAvailable;
		private IOAuthSession? _oauth;

		public ConfigFlowContext(IOAuthCallbackCoordinator coordinator,
			OAuthRegistration registration,
			bool publicListenerAvailable,
			string? entryTitle)
		{
			_coordinator = coordinator;
			_registration = registration;
			_publicListenerAvailable = publicListenerAvailable;
			EntryTitle = entryTitle;
		}

		public string? EntryTitle { get; }

		public IOAuthSession OAuth => _oauth ??= _publicListenerAvailable
			? new OAuthSession(_coordinator, _registration)
			: throw new PublicListenerUnavailableException();
	}

	private sealed class OAuthSession : IOAuthSession
	{
		private readonly IOAuthCallbackCoordinator _coordinator;
		private readonly string _state;

		public OAuthSession(IOAuthCallbackCoordinator coordinator, OAuthRegistration registration)
		{
			_coordinator = coordinator;
			_state = registration.State;
			RedirectUri = registration.RedirectUri;
		}

		public string RedirectUri { get; }

		public string State => _state;

		public string? AuthorizationCode => _coordinator.GetCode(_state);
	}
}
