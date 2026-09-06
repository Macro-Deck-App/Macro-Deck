using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeck.Localization;
using MacroDeck.Sdk.Issues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class IntegrationIssueBroadcastBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

	private readonly IIntegrationRegistry _registry;
	private readonly IIntegrationIssueService _issueService;
	private readonly IIntegrationIssueBroadcastTrigger _trigger;
	private readonly IUiTransport _transport;
	private readonly IUserNotificationStore _userNotificationStore;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	private readonly Dictionary<string, string> _lastByIntegration = new(StringComparer.Ordinal);

	private readonly Dictionary<string, HashSet<string>> _notifiedErrorIssueIdsByIntegration =
		new(StringComparer.Ordinal);

	public IntegrationIssueBroadcastBackgroundService(
		IHostApplicationLifetime lifetime,
		IIntegrationRegistry registry,
		IIntegrationIssueService issueService,
		IIntegrationIssueBroadcastTrigger trigger,
		IUiTransport transport,
		IUserNotificationStore userNotificationStore,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
		: base(lifetime)
	{
		_registry = registry;
		_issueService = issueService;
		_trigger = trigger;
		_transport = transport;
		_userNotificationStore = userNotificationStore;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<IntegrationIssueBroadcastBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await Tick(stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Integration issue broadcast tick failed");
				}

				await _trigger.WaitAsync(_interval, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	internal async Task Tick(CancellationToken ct)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var integration in _registry.Integrations)
		{
			if (!_registry.IsEnabled(integration.Id))
			{
				continue;
			}

			seen.Add(integration.Id);
			// IIntegrationIssueService.GetIssuesAsync already catches a misbehaving provider and
			// answers empty, so no per-integration try/catch is needed here; one provider throwing
			// cannot take the rest of this tick down.
			var issues = await _issueService.GetIssuesAsync(integration.Id, ct);
			await BroadcastIfChanged(integration.Id, issues, ct);
		}

		foreach (var goneId in _lastByIntegration.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			await BroadcastIfChanged(goneId, [], ct);
		}
	}

	private async Task BroadcastIfChanged(
		string integrationId,
		IReadOnlyList<IntegrationIssue> issues,
		CancellationToken ct)
	{
		await NotifyNewErrorIssues(integrationId, issues);

		if (issues.Count == 0)
		{
			if (_lastByIntegration.Remove(integrationId))
			{
				await _transport.Send(new IntegrationIssuesChangedEvent
					{
						IntegrationId = integrationId,
						Issues = [],
						IssueCount = 0,
						Severity = null
					},
					ct);
			}

			return;
		}

		var dtos = issues.Select(IntegrationIssueDto.From).ToList();
		var json = JsonSerializer.Serialize(dtos);
		if (_lastByIntegration.TryGetValue(integrationId, out var previous) && previous == json)
		{
			return;
		}

		_lastByIntegration[integrationId] = json;
		await _transport.Send(new IntegrationIssuesChangedEvent
			{
				IntegrationId = integrationId,
				Issues = dtos,
				IssueCount = dtos.Count,
				Severity = IntegrationIssueDto.MaxSeverityName(issues)
			},
			ct);
	}

	private async Task NotifyNewErrorIssues(string integrationId, IReadOnlyList<IntegrationIssue> issues)
	{
		var currentErrorIds = issues
			.Where(issue => issue.Severity == IntegrationIssueSeverity.Error)
			.Select(issue => issue.Id)
			.ToHashSet(StringComparer.Ordinal);

		if (!_notifiedErrorIssueIdsByIntegration.TryGetValue(integrationId, out var alreadyNotified))
		{
			alreadyNotified = new HashSet<string>(StringComparer.Ordinal);
		}

		var integration = _registry.Integrations.FirstOrDefault(i => i.Id == integrationId);

		// The notification pipeline stores finished text, so the issue's own strings are resolved here
		// rather than handed on as references.
		await using var scope = _scopeFactory.CreateAsyncScope();
		var culture = await ActiveLocalization.Culture(scope.ServiceProvider);
		var localization = scope.ServiceProvider.GetRequiredService<ILocalizationResolver>();
		var integrationName = integration is null ? null : localization.Resolve(integration.Name, culture);

		foreach (var issue in issues)
		{
			if (issue.Severity != IntegrationIssueSeverity.Error || alreadyNotified.Contains(issue.Id))
			{
				continue;
			}

			_userNotificationStore.Raise(new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Error,
				Kind = UserNotificationKind.Integration,
				Title = localization.Resolve(issue.Title, culture) ?? issue.Id,
				Message = localization.Resolve(issue.Description, culture),
				SourceId = integrationId,
				SourceName = integrationName,
				Action = new UserNotificationAction(UserNotificationActionKind.OpenIntegration, integrationId),
				DedupeKey = $"integration-issue:{integrationId}:{issue.Id}"
			});
		}

		if (currentErrorIds.Count == 0)
		{
			_notifiedErrorIssueIdsByIntegration.Remove(integrationId);
		}
		else
		{
			_notifiedErrorIssueIdsByIntegration[integrationId] = currentErrorIds;
		}
	}
}
