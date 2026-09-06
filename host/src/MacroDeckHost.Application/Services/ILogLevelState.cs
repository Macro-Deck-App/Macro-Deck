using MacroDeckHost.Application.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Application.Services;

public interface ILogLevelState
{
	LogEntryLevel Minimum { get; set; }
}

public sealed class LogLevelState : ILogLevelState
{
	private readonly object _lock = new();
	private readonly List<LevelFloor> _overrides = [];

	private volatile LogEntryLevel _minimum;

	public LogLevelState(LogEntryLevel minimum)
	{
		_minimum = minimum;
		Global = new LoggingLevelSwitch(ToSerilog(minimum));
	}

	public LoggingLevelSwitch Global { get; }

	public LogEntryLevel Minimum
	{
		get => _minimum;
		set
		{
			lock (_lock)
			{
				_minimum = value;
				var level = ToSerilog(value);
				Global.MinimumLevel = level;

				foreach (var registered in _overrides)
				{
					registered.Apply(level);
				}
			}
		}
	}

	public LoggingLevelSwitch RegisterOverride(LogEventLevel baseline) => Register(new HardFloor(baseline));

	public LoggingLevelSwitch RegisterNoiseFloor(LogEventLevel quiet) => Register(new NoiseFloor(quiet));

	private LoggingLevelSwitch Register(LevelFloor floor)
	{
		lock (_lock)
		{
			floor.Apply(ToSerilog(_minimum));
			_overrides.Add(floor);

			return floor.Switch;
		}
	}

	private static LogEventLevel ToSerilog(LogEntryLevel level)
		=> level switch
		{
			LogEntryLevel.Verbose => LogEventLevel.Verbose,
			LogEntryLevel.Debug => LogEventLevel.Debug,
			LogEntryLevel.Information => LogEventLevel.Information,
			LogEntryLevel.Warning => LogEventLevel.Warning,
			LogEntryLevel.Error => LogEventLevel.Error,
			LogEntryLevel.Fatal => LogEventLevel.Fatal,
			_ => LogEventLevel.Information
		};

	private abstract class LevelFloor
	{
		protected LevelFloor(LogEventLevel baseline)
		{
			Baseline = baseline;
			Switch = new LoggingLevelSwitch(baseline);
		}

		public LoggingLevelSwitch Switch { get; }

		protected LogEventLevel Baseline { get; }

		public abstract void Apply(LogEventLevel minimum);
	}

	private sealed class HardFloor : LevelFloor
	{
		public HardFloor(LogEventLevel baseline)
			: base(baseline)
		{
		}

		public override void Apply(LogEventLevel minimum)
			=> Switch.MinimumLevel = minimum > Baseline ? minimum : Baseline;
	}

	private sealed class NoiseFloor : LevelFloor
	{
		public NoiseFloor(LogEventLevel quiet)
			: base(quiet)
		{
		}

		// A Serilog override switch replaces the global minimum for its category in both directions, so
		// without the upper clamp a quiet category would become more verbose than the rest of the log
		// as soon as the user raises the minimum above the quiet level.
		public override void Apply(LogEventLevel minimum)
			=> Switch.MinimumLevel = minimum <= LogEventLevel.Debug
				? minimum
				: minimum > Baseline
					? minimum
					: Baseline;
	}
}
