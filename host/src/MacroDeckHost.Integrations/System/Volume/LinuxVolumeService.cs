using System.Globalization;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Volume;

[SupportedOSPlatform("linux")]
internal sealed class LinuxVolumeService : IVolumeService
{
	private const long SnapshotLifetimeMs = 1000;

	private static readonly TimeSpan _pactlTimeout = TimeSpan.FromSeconds(3);

	private readonly bool _hasPactl = ProcessRunner.CommandExists("pactl");
	private readonly bool _hasAmixer = ProcessRunner.CommandExists("amixer");

	private readonly IReadOnlyDictionary<string, string?> _pactlEnvironment =
		PactlOutputParser.UnlocalizedEnvironment(Environment.GetEnvironmentVariable("LC_ALL"));

	private readonly object _snapshotGate = new();
	private Task<PactlSnapshot>? _snapshot;

	public bool IsSupported => _hasPactl || _hasAmixer;

	public event Action? Changed
	{
		add { }
		remove { }
	}

	public async Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
	{
		if (!_hasPactl)
		{
			return [];
		}

		var snapshot = await SnapshotAsync(cancellationToken);
		return
		[
			.. snapshot.Sinks.Select(sink =>
				new AudioDevice(sink.Name, sink.Description, AudioFlow.Output, sink.Name == snapshot.Defaults.Sink)),
			.. snapshot.Sources.Select(source =>
				new AudioDevice(source.Name, source.Description, AudioFlow.Input, source.Name == snapshot.Defaults.Source))
		];
	}

	public async Task<float?> GetVolumeAsync(AudioTarget target, CancellationToken cancellationToken = default)
	{
		if (_hasPactl)
		{
			return (await SnapshotAsync(cancellationToken)).Find(target)?.Volume;
		}

		return await AmixerAsync(target, ["get"], cancellationToken) is { } mixer
			? PactlOutputParser.ParsePercent(mixer)
			: null;
	}

	public async Task<bool> SetVolumeAsync(AudioTarget target, float level, CancellationToken cancellationToken = default)
	{
		var percent = (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100);
		var value = $"{percent.ToString(CultureInfo.InvariantCulture)}%";

		if (_hasPactl)
		{
			return await PactlSetAsync(target, "volume", value, cancellationToken);
		}

		return await AmixerAsync(target, ["set"], cancellationToken, value) is not null;
	}

	public async Task<bool?> GetMuteAsync(AudioTarget target, CancellationToken cancellationToken = default)
	{
		if (_hasPactl)
		{
			return (await SnapshotAsync(cancellationToken)).Find(target)?.Muted;
		}

		return await AmixerAsync(target, ["get"], cancellationToken) is { } mixer
			? mixer.Contains("[off]", StringComparison.OrdinalIgnoreCase)
			: null;
	}

	public async Task<bool> SetMuteAsync(AudioTarget target, bool mute, CancellationToken cancellationToken = default)
	{
		if (_hasPactl)
		{
			return await PactlSetAsync(target, "mute", mute ? "1" : "0", cancellationToken);
		}

		var switchValue = target.Flow == AudioFlow.Output ? mute ? "mute" : "unmute" : mute ? "nocap" : "cap";
		return await AmixerAsync(target, ["set"], cancellationToken, switchValue) is not null;
	}

	// Every device variable is polled on its own, so reads share one listing instead of each spawning
	// pactl; a write drops the listing so the next read sees its effect.
	private Task<PactlSnapshot> SnapshotAsync(CancellationToken cancellationToken)
	{
		lock (_snapshotGate)
		{
			if (_snapshot is { } current &&
				(!current.IsCompleted ||
					(current.IsCompletedSuccessfully &&
						Environment.TickCount64 - current.Result.TakenAt < SnapshotLifetimeMs)))
			{
				return current.WaitAsync(cancellationToken);
			}

			_snapshot = LoadSnapshotAsync();
			return _snapshot.WaitAsync(cancellationToken);
		}
	}

	private async Task<PactlSnapshot> LoadSnapshotAsync()
	{
		var listing = await PactlAsync(["list"], CancellationToken.None);
		var info = await PactlAsync(["info"], CancellationToken.None);
		var parsed = listing is null ? new PactlListing([], []) : PactlOutputParser.ParseListing(listing);

		return new PactlSnapshot(parsed.Sinks,
			parsed.Sources,
			info is null ? default : PactlOutputParser.ParseDefaults(info),
			Environment.TickCount64);
	}

	private async Task<bool> PactlSetAsync(AudioTarget target, string property, string value, CancellationToken cancellationToken)
	{
		var kind = target.Flow == AudioFlow.Output ? "sink" : "source";
		var device = target.DeviceId ?? (target.Flow == AudioFlow.Output ? "@DEFAULT_SINK@" : "@DEFAULT_SOURCE@");
		var applied = await PactlAsync([$"set-{kind}-{property}", device, value], cancellationToken) is not null;
		lock (_snapshotGate)
		{
			_snapshot = null;
		}

		return applied;
	}

	// pactl reports a missing sink or source with a non-zero exit, and a hung audio server must not block
	// every volume read or the integration's startup, so a failed or timed-out run reads as nothing.
	private async Task<string?> PactlAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_pactlTimeout);
		try
		{
			var result = await ProcessRunner.RunWithResultAsync("pactl", arguments, _pactlEnvironment, timeout.Token);
			return result.Succeeded ? result.StandardOutput : null;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
	}

	private async Task<string?> AmixerAsync(
		AudioTarget target,
		IReadOnlyList<string> verb,
		CancellationToken cancellationToken,
		string? value = null)
	{
		if (!_hasAmixer || !target.IsDefault)
		{
			return null;
		}

		var control = target.Flow == AudioFlow.Output ? "Master" : "Capture";
		string[] arguments = value is null ? [.. verb, control] : [.. verb, control, value];
		var result = await ProcessRunner.RunWithResultAsync("amixer", arguments, cancellationToken);
		return result.Succeeded ? result.StandardOutput : null;
	}

	private sealed record PactlSnapshot(
		IReadOnlyList<PactlDevice> Sinks,
		IReadOnlyList<PactlDevice> Sources,
		PactlDefaults Defaults,
		long TakenAt)
	{
		public PactlDevice? Find(AudioTarget target)
		{
			var name = target.DeviceId ?? (target.Flow == AudioFlow.Output ? Defaults.Sink : Defaults.Source);
			var devices = target.Flow == AudioFlow.Output ? Sinks : Sources;
			return name is null ? null : devices.FirstOrDefault(device => device.Name == name);
		}
	}
}
