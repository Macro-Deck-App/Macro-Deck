using System.Text.Json;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Timers;
using MacroDeckHost.Tests.UnitTests.Variables;
using Microsoft.Extensions.DependencyInjection;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

internal sealed class CalendarWidgetAutomationHarness : IAsyncDisposable
{
	private readonly ServiceProvider _services;

	public CalendarWidgetAutomationHarness()
	{
		Provider = new FakeCalendarIntegration("app.google", "Google Calendar").WithAccount("work", "main", "team");
		Cache = CalendarTesting.Cache(Time, TimeZoneInfo.Utc, Provider);

		var services = new ServiceCollection();
		services.AddSingleton(Registry);
		services.AddSingleton<Mediator.IMediator>(Mediator);
		services.AddSingleton<IUserVariableStore, NullUserVariableStore>();
		services.AddTestVariableService();
		services.AddScoped<IFlowExecutor>(_ => Executor);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		_services = services.BuildServiceProvider();

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		Triggers = new CalendarWidgetTriggerScheduler(Folders, Cache, scopeFactory, Time, Logger());
		Variables = new CalendarWidgetVariableWriter(Folders, Cache, scopeFactory, Time, Logger());
		Lifecycle = new CalendarWidgetLifecycle(Variables, Triggers, new CalendarWidgetChanges());
	}

	public FakeTimeProvider Time { get; } = new() { Now = Noon };

	public FakeCalendarIntegration Provider { get; }

	public CalendarEventCache Cache { get; }

	public MutableFolderCache Folders { get; } = new();

	public VariableRegistry Registry { get; } = new();

	public RecordingMediator Mediator { get; } = new();

	public RecordingFlowExecutor Executor { get; } = new();

	public CalendarWidgetTriggerScheduler Triggers { get; }

	public CalendarWidgetVariableWriter Variables { get; }

	public CalendarWidgetLifecycle Lifecycle { get; }

	public static string MainCalendar => CalendarKeys.Calendar("app.google::work", "main");

	public static string TeamCalendar => CalendarKeys.Calendar("app.google::work", "team");

	public static string Data(object? settings = null, params string[] triggers)
	{
		var data = settings is null
			? new Dictionary<string, object?>()
			: JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(settings))!;
		data["flows"] = triggers.Select(trigger => new
		{
			triggerType = trigger,
			children = new object[] { new { type = "action" } },
		});

		return JsonSerializer.Serialize(data);
	}

	public CalendarWidgetAutomationHarness With(CalendarEvent calendarEvent)
	{
		Provider.WithEvent("work", calendarEvent);
		return this;
	}

	public async Task RunAtAsync(DateTimeOffset now)
	{
		Time.Now = now;
		await Cache.SyncAsync(CancellationToken.None);
		await Triggers.RunAsync(CancellationToken.None);
	}

	public CalendarWidgetBackgroundService BackgroundService()
	{
		var readiness = new StartupReadiness();
		readiness.MarkEventDispatchReady();

		return new CalendarWidgetBackgroundService(new StartedHostLifetime(),
			Triggers,
			Variables,
			Cache,
			readiness,
			Time,
			Logger());
	}

	public string? Variable(WidgetEntity widget, string name)
		=> Registry.FindByName(VariableScope.Widget, widget.Id.ToString(), name)?.Value;

	public IReadOnlyList<string> VariableNames(WidgetEntity widget)
		=> [.. Registry.GetByScope(VariableScope.Widget, widget.Id.ToString()).Select(variable => variable.Name)];

	public async ValueTask DisposeAsync()
	{
		Triggers.Dispose();
		Cache.Dispose();
		await _services.DisposeAsync();
	}

	internal sealed class RecordingFlowExecutor : IFlowExecutor
	{
		private readonly List<FlowExecutionRequest> _requests = [];

		public IReadOnlyList<FlowExecutionRequest> Requests
		{
			get
			{
				lock (_requests)
				{
					return [.. _requests];
				}
			}
		}

		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
		{
			lock (_requests)
			{
				_requests.Add(request);
			}

			return Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
			});
		}
	}
}
