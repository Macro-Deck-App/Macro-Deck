using System.Collections.Concurrent;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class LabelRenderFailureLogTests
{
	private const string Template = "{{ vars.cpu | round: 1 }}";

	private ConcurrentQueue<LogEvent> _warnings = null!;
	private LabelRenderFailureLog _log = null!;

	[SetUp]
	public void SetUp()
	{
		_warnings = new ConcurrentQueue<LogEvent>();
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(new DelegatingLogSink(logEvent =>
			{
				if (logEvent.Level == LogEventLevel.Warning)
				{
					_warnings.Enqueue(logEvent);
				}
			}))
			.CreateLogger();
		_log = new LabelRenderFailureLog(logger);
	}

	[Test]
	public void A_label_that_keeps_failing_the_same_way_is_logged_once()
	{
		var widgetId = Guid.NewGuid();

		for (var attempt = 0; attempt < 10; attempt++)
		{
			_log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert"));
		}

		Assert.That(_warnings, Has.Count.EqualTo(1));
	}

	[Test]
	public void A_new_failure_after_a_different_one_or_a_success_is_logged_again()
	{
		var widgetId = Guid.NewGuid();

		_log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert"));
		_log.Failed(widgetId, "off", "{{ vars.cpu }", new InvalidOperationException("Parse error"));
		_log.Succeeded(widgetId, "off");
		_log.Failed(widgetId, "off", "{{ vars.cpu }", new InvalidOperationException("Parse error"));

		Assert.That(_warnings, Has.Count.EqualTo(3));
	}

	[Test]
	public void Concurrent_renders_of_one_failing_label_log_it_once()
	{
		var widgetId = Guid.NewGuid();

		Parallel.For(0,
			2000,
			new ParallelOptions { MaxDegreeOfParallelism = 8 },
			_ => _log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert")));

		Assert.That(_warnings, Has.Count.EqualTo(1));
	}

	[Test]
	public void A_label_racing_between_failure_and_recovery_still_logs_its_next_failure()
	{
		var widgetId = Guid.NewGuid();

		Parallel.For(0,
			2000,
			new ParallelOptions { MaxDegreeOfParallelism = 8 },
			attempt =>
			{
				if (attempt % 2 == 0)
				{
					_log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert"));
				}
				else
				{
					_log.Succeeded(widgetId, "off");
				}
			});
		_log.Succeeded(widgetId, "off");
		var before = _warnings.Count;

		_log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert"));
		_log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert"));

		Assert.That(_warnings, Has.Count.EqualTo(before + 1));
	}

	[Test]
	public void Concurrent_failures_and_successes_beyond_the_cap_keep_logging_every_new_label()
	{
		var widgetIds = Enumerable.Range(0, LabelRenderFailureLog.MaxTrackedLabels * 3)
			.Select(_ => Guid.NewGuid())
			.ToArray();

		Parallel.ForEach(widgetIds,
			new ParallelOptions { MaxDegreeOfParallelism = 8 },
			widgetId =>
			{
				_log.Failed(widgetId, "off", Template, new InvalidOperationException("Unable to convert"));
				_log.Succeeded(Guid.NewGuid(), "off");
			});

		var logged = _warnings.Select(warning => warning.Properties["WidgetId"].ToString()).ToHashSet();
		Assert.That(logged, Has.Count.EqualTo(widgetIds.Length));
	}
}
