using System.Globalization;
using MacroDeckHost.Tests.InputProbe;
using Serilog;

Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(new StdoutLogSink()).CreateLogger();

var mode = args.FirstOrDefault();
var options = ProbeOptions.Parse(args.Skip(1).ToArray());

try
{
	return mode switch
	{
		"window" when OperatingSystem.IsWindows() => new Win32ProbeWindow(options.Title,
			options.X,
			options.Y,
			options.Width,
			options.Height).Run(),
		"window" when OperatingSystem.IsLinux() => new X11ProbeWindow(options.Title,
			options.X,
			options.Y,
			options.Width,
			options.Height).Run(),
		"idle" => ProbeModes.Idle(options),
		"launch" => await ProbeModes.LaunchAsync(options),
		"send" => await ProbeModes.SendAsync(options),
		"x11-stress" when OperatingSystem.IsLinux() => await X11StressMode.RunAsync(options),
		_ => Usage()
	};
}
catch (Exception exception)
{
	ProbeOutput.Error(exception.ToString());
	return 1;
}

static int Usage()
{
	Console.Error.WriteLine("usage: InputProbe window|idle|launch|send|x11-stress [--option value]...");
	return 2;
}

internal sealed record ProbeOptions(Dictionary<string, string> Values, string[] Rest)
{
	public string Title => Get("title") ?? "Macro Deck input probe";

	public int X => GetInt("x", 100);

	public int Y => GetInt("y", 100);

	public int Width => GetInt("width", 640);

	public int Height => GetInt("height", 480);

	public static ProbeOptions Parse(string[] arguments)
	{
		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		var rest = new List<string>();
		for (var i = 0; i < arguments.Length; i++)
		{
			if (arguments[i] == "--")
			{
				rest.AddRange(arguments.Skip(i + 1));
				break;
			}

			if (arguments[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < arguments.Length)
			{
				values[arguments[i][2..]] = arguments[++i];
			}
			else
			{
				rest.Add(arguments[i]);
			}
		}

		return new ProbeOptions(values, rest.ToArray());
	}

	public string? Get(string name)
	{
		return Values.TryGetValue(name, out var value) ? value : null;
	}

	public int GetInt(string name, int fallback)
	{
		return Get(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
	}
}
