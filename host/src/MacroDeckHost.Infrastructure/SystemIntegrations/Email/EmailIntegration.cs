using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Infrastructure.SystemIntegrations.Email;

public interface IEmailHostServicesConsumer
{
	void UseEmailServices(EmailHostServices services);
}

[MacroDeckIntegration]
public sealed class EmailIntegration : IIntegration, ISystemIntegration, IWidgetTypeProvider, IVariableProvider,
	IEmailHostServicesConsumer
{
	public const string IntegrationId = EmailWidgetTypes.OwnerId;

	private const string DataSchema
		= """{"type":"object","properties":{"inboxes":{"type":"array","items":{"type":"string"}},"mode":{"type":"string","enum":["count","list"]},"unreadOnly":{"type":"boolean"},"maxMessages":{"type":"integer","minimum":1,"maximum":10},"showSender":{"type":"boolean"},"showSubject":{"type":"boolean"},"showPreview":{"type":"boolean"},"showTimestamp":{"type":"boolean"},"flows":{"type":["string","array"]},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}""";

	private const string DefaultData
		= """{"inboxes":[],"mode":"count","unreadOnly":false,"maxMessages":5,"showSender":true,"showSubject":true,"showPreview":false,"showTimestamp":true}""";

	private static readonly ILogger _logger = IntegrationLog.For<EmailIntegration>(IntegrationId);

	private readonly Lock _gate = new();
	private readonly HashSet<string> _persisted = new(StringComparer.Ordinal);

	private EmailHostServices? _services;
	private IVariableApi? _variableApi;
	private volatile EmailInboxVariableSet _variables = EmailInboxVariableSet.Initial;
	private bool _capReported;

	public EmailIntegration()
	{
		Actions = [new RefreshInboxesActionDefinition(() => _services)];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.Email.Name();

	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;

	public bool IsInitialized => true;

	public IReadOnlyList<VariableDefinition> Variables => _variables.Definitions;

	public void UseEmailServices(EmailHostServices services)
	{
		_services = services;
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		_variableApi = context.Variables;
		if (_services is not { } services)
		{
			return;
		}

		await LoadPersistedAsync(context.Variables, services).ConfigureAwait(false);

		services.Inboxes.Changed -= Refresh;
		services.Inboxes.Changed += Refresh;
		Refresh();
	}

	public Task ShutdownAsync()
	{
		if (_services is { } services)
		{
			services.Inboxes.Changed -= Refresh;
		}

		return Task.CompletedTask;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(_services is { } services
			? EmailInboxVariables.Read(services.Inboxes.Snapshot, _variables, localId)
			: VariableReading.Unavailable);
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		foreach (var widgetType in GetWidgetTypes())
		{
			await context.RegisterWidgetTypeAsync(widgetType, cancellationToken);
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return [InboxWidgetType()];
	}

	private static WidgetTypeDescriptor InboxWidgetType()
	{
		return new WidgetTypeDescriptor(EmailWidgetTypes.LocalId,
			AppStrings.Widgets.EmailInbox.Name(),
			AppStrings.Widgets.EmailInbox.Description(),
			DefaultData,
			DataSchema,
			true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
			SupportsFlows = true
		};
	}

	private void Refresh()
	{
		if (_services is not { } services || _variableApi is not { } api)
		{
			return;
		}

		EmailInboxVariableSet previous;
		EmailInboxVariableSet next;
		var reportCap = false;

		lock (_gate)
		{
			previous = _variables;
			next = EmailInboxVariables.Compose(services.Inboxes.Snapshot,
				services.Keys.Keys,
				services.Integrations,
				previous,
				_persisted);
			_persisted.ExceptWith(next.Removed.Keys);
			_variables = next;

			if (next.IsCapped && !_capReported)
			{
				_capReported = true;
				reportCap = true;
			}
		}

		if (reportCap)
		{
			_logger.Warning("{Count} email inboxes are connected; only the first {Limit} get variables",
				next.Candidates,
				EmailWidgetTypes.MaxInboxesWithVariables);
		}

		if (next.Removed.Count > 0)
		{
			_ = DeleteAsync(api, next.Removed.Values);
		}

		if (!next.SameDeclarationsAs(previous))
		{
			_ = RunAsync(api.NotifyDeclarationsChangedAsync(), "announce the changed email variables");
		}

		_ = RunAsync(api.RequestRefreshAsync(), "refresh the email variables");
	}

	private async Task LoadPersistedAsync(IVariableApi api, EmailHostServices services)
	{
		try
		{
			var existing = (await api.GetAllAsync().ConfigureAwait(false))
				.Select(handle => handle.DefinitionId)
				.OfType<string>()
				.ToHashSet(StringComparer.Ordinal);

			lock (_gate)
			{
				foreach (var key in services.Keys.Keys)
				{
					if (EmailInboxVariables.DefinitionIds(key.Slug).Any(existing.Contains))
					{
						_persisted.Add(key.Ref);
					}
				}
			}
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Could not read the existing email variables");
		}
	}

	private static async Task DeleteAsync(IVariableApi api, IEnumerable<string> slugs)
	{
		try
		{
			var ids = slugs.SelectMany(EmailInboxVariables.DefinitionIds).ToHashSet(StringComparer.Ordinal);
			foreach (var handle in await api.GetAllAsync().ConfigureAwait(false))
			{
				if (handle.DefinitionId is { } definitionId && ids.Contains(definitionId))
				{
					await api.DeleteAsync(handle.Id).ConfigureAwait(false);
				}
			}
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Could not delete the variables of a removed email inbox");
		}
	}

	private static async Task RunAsync(Task task, string what)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Could not {What}", what);
		}
	}
}
