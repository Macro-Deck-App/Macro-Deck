using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MacroDeckHost.Tests.SystemTests.Support;

// Starts the real host process against its own data directory, temp directory, ports and loopback secret,
// and plays the bootstrapper's part of relaunching it when it exits asking for a restart.
internal sealed class HostInstance : IAsyncDisposable
{
	public const string LoopbackSecretHeader = "X-MacroDeck-Loopback-Secret";

	private const int RestartRequestedExitCode = 86;
	private const int StartAttempts = 3;

	private static readonly TimeSpan _readyTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(45);

	private readonly ConcurrentQueue<string> _output = new();
	private readonly string _root;
	private readonly IReadOnlyDictionary<string, string?> _extraEnvironment;
	private readonly string _loopbackSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
	private readonly Lock _gate = new();

	private Process? _process;
	private Task? _supervision;
	private int _starts;
	private bool _stopping;

	private HostInstance(string root, IReadOnlyDictionary<string, string?> extraEnvironment)
	{
		_root = root;
		_extraEnvironment = extraEnvironment;
		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(TempDirectory);
	}

	public string DataDirectory => Path.Combine(_root, "data");

	public string TempDirectory => Path.Combine(_root, "tmp");

	public string PluginsDirectory => Path.Combine(DataDirectory, "plugins");

	public int LoopbackPort { get; private set; }

	public int PublicPort { get; private set; }

	public HttpClient Api { get; private set; } = null!;

	public int Starts => Volatile.Read(ref _starts);

	public int? ExitCode { get; private set; }

	public bool ExitedOnItsOwn { get; private set; }

	public IReadOnlyList<string> Output => _output.ToArray();

	public int ProcessId
	{
		get
		{
			lock (_gate)
			{
				return _process?.Id ?? 0;
			}
		}
	}

	public static async Task<HostInstance> StartAsync(
		string root,
		IReadOnlyDictionary<string, string?>? environment = null)
	{
		var host = new HostInstance(root, environment ?? new Dictionary<string, string?>());
		await host.StartWithFreshPortsAsync();
		return host;
	}

	public async Task<HttpResponseMessage> PostAsync(string path, object? body = null)
	{
		return await Api.PostAsJsonAsync(path, body ?? new { });
	}

	public async Task<JsonNode> GetJsonAsync(string path)
	{
		using var response = await Api.GetAsync(path);
		response.EnsureSuccessStatusCode();
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	public async Task RestartAsync()
	{
		var startsBefore = Starts;
		using (var response = await PostAsync("/api/host/restart", new { reason = "system-test" }))
		{
			response.EnsureSuccessStatusCode();
		}

		await Poll.UntilAsync(() => Task.FromResult(Starts > startsBefore), _readyTimeout, "the host relaunched");
		await WaitUntilReadyAsync();
	}

	public async Task StopAsync()
	{
		lock (_gate)
		{
			_stopping = true;
		}

		var process = CurrentProcess();
		if (process is null || process.HasExited)
		{
			return;
		}

		try
		{
			using var response = await Api.PostAsync("/api/host/shutdown", null);
		}
		catch (HttpRequestException)
		{
		}

		using var timeout = new CancellationTokenSource(_stopTimeout);
		try
		{
			await process.WaitForExitAsync(timeout.Token);
			ExitedOnItsOwn = true;
		}
		catch (OperationCanceledException)
		{
			process.Kill(true);
			await process.WaitForExitAsync();
		}

		if (_supervision is not null)
		{
			await _supervision;
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await StopAsync();
		}
		finally
		{
			Api?.Dispose();
			lock (_gate)
			{
				_process?.Dispose();
			}
		}
	}

	// Request tracing at debug level would bury what went wrong, so only warnings, errors and the
	// supervisor's and installer's own information lines are kept.
	public string DescribeOutput(int lines = 120)
	{
		var relevant = Output.Where(IsDiagnostic).ToList();
		return string.Join(Environment.NewLine, relevant.Skip(Math.Max(0, relevant.Count - lines)));
	}

	public string SaveOutput(string directory)
	{
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, $"host-output-{Guid.NewGuid():N}.log");
		File.WriteAllLines(path, Output);
		return path;
	}

	private static bool IsDiagnostic(string line)
	{
		if (line.StartsWith("[system-test]", StringComparison.Ordinal)
			|| line.Contains(" WRN]", StringComparison.Ordinal)
			|| line.Contains(" ERR]", StringComparison.Ordinal)
			|| line.Contains(" FTL]", StringComparison.Ordinal))
		{
			return true;
		}

		return line.Contains(" INF]", StringComparison.Ordinal)
			&& line.Contains("lugin '", StringComparison.Ordinal);
	}

	private Process? CurrentProcess()
	{
		lock (_gate)
		{
			return _process;
		}
	}

	// The host binds its listeners itself and takes no socket handed over, so a port can only be chosen
	// free and passed by number; a lost race shows as an early exit and is retried on new ports.
	private async Task StartWithFreshPortsAsync()
	{
		for (var attempt = 1; ; attempt++)
		{
			LoopbackPort = FreePort();
			PublicPort = FreePort();
			Api?.Dispose();
			Api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{LoopbackPort}") };
			Api.DefaultRequestHeaders.Add(LoopbackSecretHeader, _loopbackSecret);

			_supervision = SuperviseAsync();
			try
			{
				await WaitUntilReadyAsync();
				return;
			}
			catch (TimeoutException) when (attempt < StartAttempts && ExitCode is not null)
			{
				_output.Enqueue($"[system-test] start attempt {attempt} exited with {ExitCode}; retrying");
			}
		}
	}

	private async Task SuperviseAsync()
	{
		while (true)
		{
			var process = Launch();
			await process.WaitForExitAsync();
			ExitCode = process.ExitCode;

			bool stopping;
			lock (_gate)
			{
				stopping = _stopping;
			}

			if (ExitCode != RestartRequestedExitCode || stopping)
			{
				return;
			}
		}
	}

	private Process Launch()
	{
		var executable = BuildOutputs.HostExecutable();
		var start = new ProcessStartInfo(executable)
		{
			WorkingDirectory = Path.GetDirectoryName(executable)!,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		start.Environment["MACRO_DECK_DATA_DIRECTORY"] = DataDirectory;
		start.Environment["MACRODECK_HOST_PORT"] = LoopbackPort.ToString(CultureInfo.InvariantCulture);
		start.Environment["MACRO_DECK_PORT"] = PublicPort.ToString(CultureInfo.InvariantCulture);
		start.Environment["MACRODECK_LOOPBACK_SECRET"] = _loopbackSecret;
		start.Environment["MACRODECK_SHELL_EXECUTABLE"] = executable;
		start.Environment["TMP"] = TempDirectory;
		start.Environment["TEMP"] = TempDirectory;
		start.Environment["TMPDIR"] = TempDirectory;
		foreach (var (name, value) in _extraEnvironment)
		{
			start.Environment[name] = value;
		}

		var process = new Process { StartInfo = start };
		process.OutputDataReceived += (_, line) => Record(line.Data);
		process.ErrorDataReceived += (_, line) => Record(line.Data);
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		lock (_gate)
		{
			_process?.Dispose();
			_process = process;
		}

		Interlocked.Increment(ref _starts);
		return process;
	}

	private void Record(string? line)
	{
		if (line is not null)
		{
			_output.Enqueue(line);
		}
	}

	// The port file is what the bootstrapper waits for: the host writes it once it has started, and a
	// request answered before that can reach a host whose listener state is not published yet.
	private bool PortFilePublished()
	{
		var expected = LoopbackPort.ToString(CultureInfo.InvariantCulture);
		return Directory.EnumerateFiles(TempDirectory, "macro-deck-host*.port")
			.Any(file =>
			{
				try
				{
					return File.ReadAllText(file).Trim() == expected;
				}
				catch (IOException)
				{
					return false;
				}
			});
	}

	private async Task WaitUntilReadyAsync()
	{
		await Poll.UntilAsync(async () =>
			{
				if (_supervision is { IsCompleted: true })
				{
					throw new TimeoutException($"The host exited with {ExitCode} before it was ready.");
				}

				if (!PortFilePublished())
				{
					return false;
				}

				try
				{
					using var response = await Api.GetAsync("/api/auth/status");
					return response.StatusCode == HttpStatusCode.OK;
				}
				catch (HttpRequestException)
				{
					return false;
				}
			},
			_readyTimeout,
			"the host answered /api/auth/status");
	}

	private static int FreePort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}
}
