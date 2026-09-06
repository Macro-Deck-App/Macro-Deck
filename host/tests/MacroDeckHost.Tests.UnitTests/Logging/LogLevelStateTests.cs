using System.Globalization;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class LogLevelStateTests
{
	private const string MicrosoftContext = "Microsoft.AspNetCore.Hosting.Diagnostics";
	private const string AppContext = "MacroDeckHost.Application.Logging.Something";

	private CollectingSink _sink = null!;

	[SetUp]
	public void SetUp()
	{
		_sink = new CollectingSink();
	}

	[Test]
	public void Lowering_the_minimum_lets_a_running_pipeline_emit_more()
	{
		var state = new LogLevelState(LogEntryLevel.Information);
		using var logger = CreateLogger(state);

		logger.Debug("Before");
		state.Minimum = LogEntryLevel.Debug;
		logger.Debug("After");

		string[] expected = ["After"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	[Test]
	public void Raising_the_minimum_suppresses_a_running_pipeline()
	{
		var state = new LogLevelState(LogEntryLevel.Debug);
		using var logger = CreateLogger(state);

		logger.Debug("Before");
		state.Minimum = LogEntryLevel.Warning;
		logger.Debug("After");

		string[] expected = ["Before"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	[Test]
	public void Raising_the_minimum_suppresses_an_overridden_source_too()
	{
		var state = new LogLevelState(LogEntryLevel.Debug);
		var microsoft = state.RegisterOverride(LogEventLevel.Information);
		using var logger = CreateLogger(state, microsoft);

		logger.ForContext("SourceContext", MicrosoftContext).Information("Before");
		state.Minimum = LogEntryLevel.Warning;
		logger.ForContext("SourceContext", MicrosoftContext).Information("After");

		string[] expected = ["Before"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	[Test]
	public void An_override_never_becomes_more_verbose_than_its_baseline()
	{
		var state = new LogLevelState(LogEntryLevel.Warning);
		var microsoft = state.RegisterOverride(LogEventLevel.Information);
		using var logger = CreateLogger(state, microsoft);

		state.Minimum = LogEntryLevel.Verbose;
		logger.ForContext("SourceContext", MicrosoftContext).Debug("Chatter");
		logger.ForContext("SourceContext", MicrosoftContext).Information("Kept");

		string[] expected = ["Kept"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	[Test]
	public void An_override_registered_after_a_change_starts_clamped()
	{
		var state = new LogLevelState(LogEntryLevel.Debug);
		state.Minimum = LogEntryLevel.Error;

		var registered = state.RegisterOverride(LogEventLevel.Information);

		Assert.That(registered.MinimumLevel, Is.EqualTo(LogEventLevel.Error));
	}

	[Test]
	public void The_minimum_reads_back_what_was_set()
	{
		var state = new LogLevelState(LogEntryLevel.Information);

		state.Minimum = LogEntryLevel.Fatal;

		Assert.Multiple(() =>
		{
			Assert.That(state.Minimum, Is.EqualTo(LogEntryLevel.Fatal));
			Assert.That(state.Global.MinimumLevel, Is.EqualTo(LogEventLevel.Fatal));
		});
	}

	[Test]
	public void Routine_Framework_Request_Logs_Are_Silent_At_The_Information_Default()
	{
		var state = new LogLevelState(LogEntryLevel.Information);
		using var logger = CreateNoiseFloorLogger(state);

		foreach (var category in LogNoiseCategories.RequestPipeline)
		{
			logger.ForContext("SourceContext", category).Information("chatter from {Category}", category);
		}

		logger.ForContext("SourceContext", MicrosoftContext).Warning("framework warning");
		logger.ForContext("SourceContext", MicrosoftContext).Error("framework error");
		logger.ForContext("SourceContext", AppContext).Information("our own line");

		string[] expected = ["framework warning", "framework error", "our own line"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	[Test]
	public void Selecting_Debug_Brings_The_Framework_Categories_Back_In_Full()
	{
		var state = new LogLevelState(LogEntryLevel.Information);
		using var logger = CreateNoiseFloorLogger(state);

		state.Minimum = LogEntryLevel.Debug;
		logger.ForContext("SourceContext", LogNoiseCategories.RequestPipeline[0]).Information("request start");
		logger.ForContext("SourceContext", LogNoiseCategories.RequestPipeline[1]).Information("endpoint matched");
		logger.ForContext("SourceContext", LogNoiseCategories.RequestPipeline[2]).Information("action executed");
		logger.ForContext("SourceContext", LogNoiseCategories.RequestPipeline[0]).Debug("request detail");

		string[] expected = ["request start", "endpoint matched", "action executed", "request detail"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	[Test]
	public void Raising_The_Minimum_To_Error_Re_Silences_Without_Making_A_Quiet_Category_Louder()
	{
		var state = new LogLevelState(LogEntryLevel.Debug);
		using var logger = CreateNoiseFloorLogger(state);

		state.Minimum = LogEntryLevel.Error;
		logger.ForContext("SourceContext", MicrosoftContext).Information("chatter");
		logger.ForContext("SourceContext", MicrosoftContext).Warning("quiet category warning");
		logger.ForContext("SourceContext", AppContext).Warning("app warning");
		logger.ForContext("SourceContext", MicrosoftContext).Error("framework error");

		string[] expected = ["framework error"];
		Assert.That(Messages(), Is.EqualTo(expected));
	}

	private Logger CreateNoiseFloorLogger(LogLevelState state)
	{
		var configuration = new LoggerConfiguration().MinimumLevel.ControlledBy(state.Global);
		foreach (var category in LogNoiseCategories.RequestPipeline)
		{
			configuration.MinimumLevel.Override(category, state.RegisterNoiseFloor(LogEventLevel.Warning));
		}

		return configuration.WriteTo.Sink(_sink).CreateLogger();
	}

	private Logger CreateLogger(LogLevelState state, LoggingLevelSwitch? microsoft = null)
	{
		var configuration = new LoggerConfiguration().MinimumLevel.ControlledBy(state.Global);
		if (microsoft is not null)
		{
			configuration = configuration.MinimumLevel.Override("Microsoft", microsoft);
		}

		return configuration.WriteTo.Sink(_sink).CreateLogger();
	}

	private string[] Messages() => _sink.Messages.ToArray();

	private sealed class CollectingSink : ILogEventSink
	{
		public List<string> Messages { get; } = [];

		public void Emit(LogEvent logEvent)
			=> Messages.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
	}
}
