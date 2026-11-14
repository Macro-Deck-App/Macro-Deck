using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using MacroDeckHost.Tests.InputProbe;

namespace MacroDeckHost.Tests.Support;

internal sealed class ProbeProcess : IDisposable
{
	private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(20);

	private readonly Process _process;
	private readonly List<ProbeEvent> _events = [];
	private readonly List<string> _stderr = [];
	private readonly Lock _gate = new();
	private DateTime _lastEvent = DateTime.UtcNow;

	private ProbeProcess(Process process)
	{
		_process = process;
	}

	public string Name { get; private init; } = string.Empty;

	public int Pid => _process.Id;

	public bool HasExited => _process.HasExited;

	public ProbeEvent Ready { get; private set; } = new() { Type = "ready" };

	public bool IsFocused { get; private set; }

	public int CenterX => Ready.X!.Value + Ready.Width!.Value / 2;

	public int CenterY => Ready.Y!.Value + Ready.Height!.Value / 2;

	public static ProbeProcess StartWindow(string name, int x = 100, int y = 100, int width = 640, int height = 480)
	{
		var probe = Start(name,
			"window",
			"--title",
			name,
			"--x",
			$"{x}",
			"--y",
			$"{y}",
			"--width",
			$"{width}",
			"--height",
			$"{height}");
		probe.Ready = probe.WaitFor(e => e.Type == "ready", _startTimeout, "the probe window to be ready");
		return probe;
	}

	public static ProbeProcess Start(string name, params string[] arguments)
	{
		return Start(name, new Dictionary<string, string>(), arguments);
	}

	public static ProbeProcess Start(
		string name,
		IReadOnlyDictionary<string, string> environment,
		params string[] arguments)
	{
		var startInfo = new ProcessStartInfo(ProbeLocator.Executable(name))
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		foreach (var (key, value) in environment)
		{
			startInfo.Environment[key] = value;
		}

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The probe did not start.");
		var probe = new ProbeProcess(process) { Name = name };
		process.OutputDataReceived += (_, line) => probe.Receive(line.Data);
		process.ErrorDataReceived += (_, line) => probe.ReceiveError(line.Data);
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return probe;
	}

	public static ProbeProcess Attach(string name, int pid, nint standardOutput)
	{
		var probe = new ProbeProcess(Process.GetProcessById(pid)) { Name = name };
		var reader = new StreamReader(new FileStream(new SafeFileHandle(standardOutput, true), FileAccess.Read));
		var thread = new Thread(() =>
		{
			using (reader)
			{
				while (reader.ReadLine() is { } line)
				{
					probe.Receive(line);
				}
			}
		})
		{
			IsBackground = true
		};
		thread.Start();
		return probe;
	}

	public IReadOnlyList<ProbeEvent> Events
	{
		get
		{
			lock (_gate)
			{
				return _events.ToArray();
			}
		}
	}

	public IReadOnlyList<ProbeEvent> Keys => Events.Where(e => e.Type == "key").ToArray();

	public IReadOnlyList<ProbeEvent> Buttons => Events.Where(e => e.Type == "button").ToArray();

	public string Text => string.Concat(Events.Where(e => e.Type == "text" && !IsControl(e.Text)).Select(e => e.Text));

	public string Diagnostics
	{
		get
		{
			lock (_gate)
			{
				return string.Join(Environment.NewLine,
					_events.TakeLast(40)
						.Select(e => JsonSerializer.Serialize(e, ProbeEvent.JsonOptions))
						.Concat(_stderr.TakeLast(20)));
			}
		}
	}

	public void Send(string command)
	{
		_process.StandardInput.WriteLine(command);
		_process.StandardInput.Flush();
	}

	public void Clear()
	{
		lock (_gate)
		{
			_events.Clear();
		}
	}

	public ProbeEvent WaitFor(Func<ProbeEvent, bool> predicate, TimeSpan timeout, string description)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var match = Events.FirstOrDefault(predicate);
			if (match is not null)
			{
				return match;
			}

			if (_process.HasExited)
			{
				break;
			}

			Thread.Sleep(10);
		}

		Assert.Fail($"{Name}: timed out waiting for {description}.{Environment.NewLine}{Diagnostics}");
		return null!;
	}

	// Input arrives asynchronously, so a case reads the probe only once nothing new came in for a while.
	public void Settle(int quietMs = 300, int maxMs = 10000)
	{
		var start = DateTime.UtcNow;
		var deadline = start.AddMilliseconds(maxMs);
		while (DateTime.UtcNow < deadline)
		{
			DateTime last;
			lock (_gate)
			{
				last = _lastEvent > start ? _lastEvent : start;
			}

			if ((DateTime.UtcNow - last).TotalMilliseconds >= quietMs)
			{
				return;
			}

			Thread.Sleep(20);
		}
	}

	public void Dispose()
	{
		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(true);
				_process.WaitForExit(5000);
			}
		}
		catch (InvalidOperationException)
		{
		}

		_process.Dispose();
	}

	private void Receive(string? line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return;
		}

		ProbeEvent? probeEvent;
		try
		{
			probeEvent = JsonSerializer.Deserialize<ProbeEvent>(line, ProbeEvent.JsonOptions);
		}
		catch (JsonException)
		{
			probeEvent = new ProbeEvent { Type = "stdout", Message = line };
		}

		if (probeEvent is null)
		{
			return;
		}

		lock (_gate)
		{
			_events.Add(probeEvent);
			_lastEvent = DateTime.UtcNow;
			if (probeEvent.Type == "focus")
			{
				IsFocused = probeEvent.Down == true;
			}
		}
	}

	private void ReceiveError(string? line)
	{
		if (line is null)
		{
			return;
		}

		lock (_gate)
		{
			_stderr.Add(line);
		}
	}

	public IReadOnlyList<string> StandardError
	{
		get
		{
			lock (_gate)
			{
				return _stderr.ToArray();
			}
		}
	}

	private static bool IsControl(string? text)
	{
		return string.IsNullOrEmpty(text) || text.All(char.IsControl);
	}
}
