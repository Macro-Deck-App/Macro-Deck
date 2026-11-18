using System.Reflection;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Windows.Native;

[Platform("Win")]
[SupportedOSPlatform("windows")]
[NonParallelizable]
public class Win32InputBlockedReportWindowsTests
{
	private readonly List<LogEvent> _events = [];
	private ILogger _previous = null!;

	[SetUp]
	public void CaptureTheLogAndForgetEarlierReports()
	{
		_previous = Log.Logger;
		Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose()
			.WriteTo.Sink(new Collector(_events))
			.CreateLogger();
		typeof(Win32Input).GetField("_lastBlockedReportTicks", BindingFlags.NonPublic | BindingFlags.Static)!
			.SetValue(null, long.MinValue);
	}

	[TearDown]
	public void RestoreTheLog()
	{
		Log.Logger = _previous;
	}

	[Test]
	public void Input_an_elevated_app_blocks_is_reported_once_naming_the_app()
	{
		var processId = (uint)Environment.ProcessId;

		Win32Input.ReportBlockedByIntegrity(processId);
		Win32Input.ReportBlockedByIntegrity(processId);

		var warnings = _events.Where(e => e.Level == LogEventLevel.Warning).ToList();
		Assert.That(warnings, Has.Count.EqualTo(1), "a second report right after the first is throttled");
		Assert.Multiple(() =>
		{
			Assert.That(((ScalarValue)warnings[0].Properties["Application"]).Value,
				Is.EqualTo(Path.GetFileName(Environment.ProcessPath)));
			Assert.That(warnings[0].MessageTemplate.Text, Does.Contain("elevated"));
		});
	}

	private sealed class Collector : ILogEventSink
	{
		private readonly List<LogEvent> _events;

		public Collector(List<LogEvent> events)
		{
			_events = events;
		}

		public void Emit(LogEvent logEvent)
		{
			_events.Add(logEvent);
		}
	}
}
