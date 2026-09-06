using System.Globalization;
using MacroDeckHost.Integrations.Voicemeeter.Native;
using MacroDeck.Localization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter;

internal sealed class VoicemeeterConnection : IDisposable
{
	internal const int MacroButtonCount = 80;

	private static readonly ILogger _logger =
		IntegrationLog.For<VoicemeeterConnection>(VoicemeeterIntegration.IntegrationId);

	private readonly IVoicemeeterRemote _remote;
	private readonly VoicemeeterEventEmitter? _events;
	private readonly TimeSpan _connectedInterval;
	private readonly TimeSpan _idleInterval;
	private readonly CancellationTokenSource _cts = new();

	private volatile VoicemeeterState _state = VoicemeeterState.Disconnected;
	private volatile VoicemeeterChannelCatalog _catalog = VoicemeeterChannelCatalog.Unknown;

	private Task? _loop;
	private bool _loggedIn;
	private bool _disposed;

	internal VoicemeeterConnection(
		IVoicemeeterRemote remote,
		VoicemeeterEventEmitter? events = null,
		TimeSpan? connectedInterval = null,
		TimeSpan? idleInterval = null)
	{
		_remote = remote;
		_events = events;
		_connectedInterval = connectedInterval ?? TimeSpan.FromMilliseconds(200);
		_idleInterval = idleInterval ?? TimeSpan.FromSeconds(2);
	}

	public VoicemeeterState State => _state;

	public VoicemeeterChannelCatalog Catalog => _catalog;

	public bool IsAvailable => _remote.IsAvailable;

	public LocalizedText? UnavailableReason => _remote.UnavailableReason;

	public void Start()
	{
		if (!_remote.IsAvailable)
		{
			_logger.Information("Voicemeeter is unavailable: {Reason}", _remote.UnavailableReason);
			return;
		}

		var login = _remote.Login();
		if (!login.IsOk())
		{
			_logger.Warning("Voicemeeter login failed with {Result}", login);
			return;
		}

		_loggedIn = true;
		_loop = Task.Run(() => RunLoop(_cts.Token), CancellationToken.None);
	}

	internal void Poll()
	{
		if (_remote.GetVoicemeeterType(out var rawType) != VoicemeeterResult.Ok)
		{
			Disconnect();
			return;
		}

		var edition = VoicemeeterEditions.FromRawType(rawType);
		if (edition == VoicemeeterEdition.None)
		{
			Disconnect();
			return;
		}

		var previous = _state;
		if (previous.IsConnected && previous.Edition != edition)
		{
			Disconnect();
			previous = _state;
		}

		if (!previous.IsConnected)
		{
			Connect(edition);
			return;
		}

		if (_remote.IsParametersDirty())
		{
			Publish(BuildState(edition, previous.MacroButtons));
		}

		if (_remote.IsMacroButtonDirty())
		{
			Publish(_state with { MacroButtons = ReadMacroButtons() });
		}
	}

	public void SetParameter(string name, float value) => Write(name, () => _remote.SetParameter(name, value));

	public void SetParameter(string name, string value) => Write(name, () => _remote.SetParameter(name, value));

	public float? GetParameter(string name)
	{
		var result = _remote.GetParameter(name, out float value);
		if (result != VoicemeeterResult.Ok)
		{
			LogFailure(name, result);
			return null;
		}

		return value;
	}

	public string? GetTextParameter(string name)
	{
		var result = _remote.GetParameter(name, out string value);
		if (result != VoicemeeterResult.Ok)
		{
			LogFailure(name, result);
			return null;
		}

		return value;
	}

	public bool RunScript(string script)
	{
		var result = _remote.RunScript(script);
		if (result != VoicemeeterResult.Ok)
		{
			_logger.Warning("Voicemeeter script failed: {Reason}", Describe(result));
			return false;
		}

		return true;
	}

	public bool? GetMacroButton(int button, VoicemeeterMacroButtonMode mode)
	{
		var result = _remote.GetMacroButton(button, mode, out var state);
		if (result != VoicemeeterResult.Ok)
		{
			LogFailure($"macro button {button}", result);
			return null;
		}

		return state;
	}

	public void SetMacroButton(int button, VoicemeeterMacroButtonMode mode, bool state)
	{
		var result = _remote.SetMacroButton(button, mode, state);
		if (result != VoicemeeterResult.Ok)
		{
			LogFailure($"macro button {button}", result);
		}
	}

	public bool RunVoicemeeter(VoicemeeterEdition edition)
	{
		var result = _remote.RunVoicemeeter(VoicemeeterEditions.ToRunType(edition));
		if (result != VoicemeeterResult.Ok)
		{
			_logger.Warning("Launching {Edition} failed with {Result}",
				VoicemeeterEditions.DisplayName(edition),
				result);
			return false;
		}

		return true;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();

		try
		{
			_loop?.Wait(TimeSpan.FromSeconds(1));
		}
		catch (AggregateException)
		{
		}

		if (_loggedIn)
		{
			_remote.Logout();
			_loggedIn = false;
		}

		_remote.Dispose();
		_state = VoicemeeterState.Disconnected;
		_cts.Dispose();
	}

	internal static string FormatVersion(int packed) => string.Create(CultureInfo.InvariantCulture,
		$"{(packed >> 24) & 0xFF}.{(packed >> 16) & 0xFF}.{(packed >> 8) & 0xFF}.{packed & 0xFF}");

	private async Task RunLoop(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				Poll();
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Voicemeeter poll tick failed");
			}

			try
			{
				await Task.Delay(_state.IsConnected ? _connectedInterval : _idleInterval, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private void Connect(VoicemeeterEdition edition)
	{
		_logger.Information("Connected to {Edition}", VoicemeeterEditions.DisplayName(edition));

		_ = _remote.IsParametersDirty();
		_ = _remote.IsMacroButtonDirty();

		Publish(BuildState(edition, ReadMacroButtons()));
	}

	private void Disconnect()
	{
		if (!_state.IsConnected)
		{
			return;
		}

		_logger.Information("Voicemeeter is no longer running");
		_state = VoicemeeterState.Disconnected;

		// The catalogue is deliberately left alone: the pickers must keep working while Voicemeeter
		// restarts, which is exactly when a user is most likely to be editing a widget.
		_events?.Observe(_state);
	}

	private void Publish(VoicemeeterState state)
	{
		_state = state;
		_catalog = VoicemeeterChannelCatalog.FromState(state);
		_events?.Observe(state);
	}

	private VoicemeeterState BuildState(VoicemeeterEdition edition, IReadOnlyDictionary<int, bool> macroButtons)
	{
		var layout = VoicemeeterLayout.For(edition);
		var assignmentNames = layout.BusAssignmentNames();

		var strips = new List<VoicemeeterChannel>(layout.Strips);
		for (var index = 0; index < layout.Strips; index++)
		{
			var isPhysical = index < layout.PhysicalStrips;
			var assignments = new Dictionary<string, bool>(assignmentNames.Count, StringComparer.Ordinal);
			foreach (var bus in assignmentNames)
			{
				assignments[bus] = ReadFlag(VoicemeeterParameters.StripBusAssignment(index, bus));
			}

			strips.Add(new VoicemeeterChannel(index,
				VoicemeeterChannelKind.Strip,
				isPhysical,
				ReadLabel(VoicemeeterParameters.Strip(index, VoicemeeterParameters.Label)),
				ReadFloat(VoicemeeterParameters.Strip(index, VoicemeeterParameters.Gain)),
				ReadFlag(VoicemeeterParameters.Strip(index, VoicemeeterParameters.Mute)),
				ReadFlag(VoicemeeterParameters.Strip(index, VoicemeeterParameters.Mono)),
				isPhysical && ReadFlag(VoicemeeterParameters.Strip(index, VoicemeeterParameters.Solo)),
				assignments));
		}

		var buses = new List<VoicemeeterChannel>(layout.Buses);
		for (var index = 0; index < layout.Buses; index++)
		{
			buses.Add(new VoicemeeterChannel(index,
				VoicemeeterChannelKind.Bus,
				index < layout.PhysicalBuses,
				ReadLabel(VoicemeeterParameters.Bus(index, VoicemeeterParameters.Label)),
				ReadFloat(VoicemeeterParameters.Bus(index, VoicemeeterParameters.Gain)),
				ReadFlag(VoicemeeterParameters.Bus(index, VoicemeeterParameters.Mute)),
				ReadFlag(VoicemeeterParameters.Bus(index, VoicemeeterParameters.Mono)),
				Solo: false,
				new Dictionary<string, bool>(StringComparer.Ordinal)));
		}

		return new VoicemeeterState
		{
			IsConnected = true,
			Edition = edition,
			Version = _remote.GetVoicemeeterVersion(out var packed) == VoicemeeterResult.Ok
				? FormatVersion(packed)
				: null,
			Strips = strips,
			Buses = buses,
			MacroButtons = macroButtons
		};
	}

	private Dictionary<int, bool> ReadMacroButtons()
	{
		var buttons = new Dictionary<int, bool>(MacroButtonCount);
		for (var button = 0; button < MacroButtonCount; button++)
		{
			buttons[button] = _remote.GetMacroButton(button, VoicemeeterMacroButtonMode.StateOnly, out var state) ==
				VoicemeeterResult.Ok &&
				state;
		}

		return buttons;
	}

	private float ReadFloat(string parameter)
		=> _remote.GetParameter(parameter, out float value) == VoicemeeterResult.Ok ? value : 0f;

	private bool ReadFlag(string parameter)
		=> _remote.GetParameter(parameter, out float value) == VoicemeeterResult.Ok && value > 0.5f;

	private string ReadLabel(string parameter)
		=> _remote.GetParameter(parameter, out string value) == VoicemeeterResult.Ok ? value.Trim() : string.Empty;

	private static void Write(string parameter, Func<VoicemeeterResult> write)
	{
		var result = write();
		if (result != VoicemeeterResult.Ok)
		{
			LogFailure(parameter, result);
		}
	}

	private static void LogFailure(string parameter, VoicemeeterResult result)
		=> _logger.Warning("Voicemeeter parameter '{Parameter}' failed: {Reason}", parameter, Describe(result));

	private static string Describe(VoicemeeterResult result) => result switch
	{
		VoicemeeterResult.NoServer => "Voicemeeter is not running",
		VoicemeeterResult.Unavailable => "the Voicemeeter remote API is unavailable",
		VoicemeeterResult.UnknownParameter => "this Voicemeeter edition does not have that parameter",
		_ => result.ToString()
	};
}
