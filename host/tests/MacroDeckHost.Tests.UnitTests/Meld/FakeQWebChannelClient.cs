using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Meld;
using MacroDeckHost.Integrations.Meld.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Meld;

internal sealed class FakeQWebChannelClient : IQWebChannelClient
{
	private readonly Lock _lock = new();

	public event EventHandler<QWebChannelSignalMessage>? SignalReceived;

	public event EventHandler<QWebChannelPropertyUpdate>? PropertyUpdated;

	public event EventHandler<string?>? Disconnected;

	public Dictionary<string, QWebChannelObjectInfo> Objects { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

	public HashSet<string> FailingInvocations { get; } = new(StringComparer.Ordinal);

	public List<(string Object, string Method, IReadOnlyList<object?> Args)> Invocations { get; } = [];

	public HashSet<string> ConnectedSignals { get; } = new(StringComparer.Ordinal);

	public Exception? ConnectException { get; set; }

	public bool IsConnected { get; private set; }

	public bool Disposed { get; private set; }

	public Task<IReadOnlyDictionary<string, QWebChannelObjectInfo>> ConnectAsync(
		Uri uri,
		CancellationToken cancellationToken)
	{
		if (ConnectException is { } exception)
		{
			return Task.FromException<IReadOnlyDictionary<string, QWebChannelObjectInfo>>(exception);
		}

		IsConnected = true;
		return Task.FromResult<IReadOnlyDictionary<string, QWebChannelObjectInfo>>(Objects);
	}

	public Task<JsonElement> InvokeAsync(
		string objectName,
		string method,
		IReadOnlyList<object?> args,
		CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			Invocations.Add((objectName, method, args));
		}

		var key = $"{objectName}.{method}";
		if (FailingInvocations.Contains(key))
		{
			return Task.FromException<JsonElement>(new QWebChannelException($"Meld Studio refused '{key}'."));
		}

		var body = Responses.GetValueOrDefault(key, "null");
		return Task.FromResult(Parse(body));
	}

	public Task ConnectToSignalAsync(string objectName, string signal, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			ConnectedSignals.Add($"{objectName}.{signal}");
		}

		return Task.CompletedTask;
	}

	public Task DisconnectAsync()
	{
		IsConnected = false;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		Disposed = true;
		IsConnected = false;
	}

	public void RaiseSignal(string objectName, string signal, params JsonElement[] args)
		=> SignalReceived?.Invoke(this, new QWebChannelSignalMessage(objectName, signal, args));

	public void RaisePropertyUpdate(string objectName, IReadOnlyDictionary<string, JsonElement> properties)
		=> PropertyUpdated?.Invoke(this, new QWebChannelPropertyUpdate(objectName, properties));

	public void Drop(string reason = "dropped")
	{
		IsConnected = false;
		Disconnected?.Invoke(this, reason);
	}

	public IReadOnlyList<object?>? ArgsOf(string method)
	{
		lock (_lock)
		{
			return Invocations.LastOrDefault(entry => string.Equals(entry.Method, method, StringComparison.Ordinal))
				.Args;
		}
	}

	public IReadOnlyList<(string Object, string Method, IReadOnlyList<object?> Args)> InvocationSnapshot()
	{
		lock (_lock)
		{
			return Invocations.ToList();
		}
	}

	public IReadOnlySet<string> ConnectedSignalSnapshot()
	{
		lock (_lock)
		{
			return ConnectedSignals.ToHashSet(StringComparer.Ordinal);
		}
	}

	public static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	public static QWebChannelObjectInfo BuildMeldObjectInfo(
		int? version = 1,
		bool supportsSetMuted = true,
		bool supportsSetProperty = false,
		string? sessionJson = null,
		bool isStreaming = false,
		bool isRecording = false)
	{
		var methods = new Dictionary<string, int>(StringComparer.Ordinal)
		{
			[MeldObjects.ShowScene] = 0,
			[MeldObjects.SetStagedScene] = 1,
			[MeldObjects.ShowStagedScene] = 2,
			[MeldObjects.ToggleMute] = 3,
			[MeldObjects.ToggleMonitor] = 4,
			[MeldObjects.ToggleLayer] = 5,
			[MeldObjects.ToggleEffect] = 6,
			[MeldObjects.RegisterTrackObserver] = 7,
			[MeldObjects.UnregisterTrackObserver] = 8,
			[MeldObjects.SetGain] = 9,
			[MeldObjects.SendCommand] = 10
		};

		if (supportsSetMuted)
		{
			methods[MeldObjects.SetMuted] = 11;
		}

		if (supportsSetProperty)
		{
			methods[MeldObjects.SetProperty] = 12;
		}

		var signals = new Dictionary<string, int>(StringComparer.Ordinal) { [MeldObjects.GainUpdatedSignal] = 0 };
		var signalNames = new Dictionary<int, string> { [0] = MeldObjects.GainUpdatedSignal };

		var propertyNames = new Dictionary<int, string>
		{
			[0] = MeldObjects.VersionProperty,
			[1] = MeldObjects.SessionProperty,
			[2] = MeldObjects.IsStreamingProperty,
			[3] = MeldObjects.IsRecordingProperty
		};

		var initialProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (version is { } v)
		{
			initialProperties[MeldObjects.VersionProperty] = Parse(v.ToString(CultureInfo.InvariantCulture));
		}

		initialProperties[MeldObjects.SessionProperty] = Parse(sessionJson ?? """{"items":{}}""");
		initialProperties[MeldObjects.IsStreamingProperty] = Parse(isStreaming ? "true" : "false");
		initialProperties[MeldObjects.IsRecordingProperty] = Parse(isRecording ? "true" : "false");

		return new QWebChannelObjectInfo(MeldObjects.Object,
			methods,
			signals,
			signalNames,
			propertyNames,
			initialProperties);
	}
}
