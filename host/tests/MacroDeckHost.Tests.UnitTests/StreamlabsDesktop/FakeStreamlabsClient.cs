using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

internal readonly record struct FakeInvocation(string Resource, string Method, IReadOnlyList<object?> Args)
{
	public string Key => $"{Resource}.{Method}";

	public override string ToString() => Args.Count == 0
		? Key
		: $"{Key}:{string.Join(',', Args.Select(Format))}";

	private static string Format(object? argument) => argument switch
	{
		null => "null",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => argument.ToString() ?? "null"
	};
}

internal sealed class FakeStreamlabsClient : IStreamlabsClient
{
	private readonly List<FakeInvocation> _invocations = [];

	public bool IsConnected { get; set; } = true;

	public List<string> Calls { get; } = [];

	public List<string> Subscriptions { get; } = [];

	public IReadOnlyList<FakeInvocation> Invocations => _invocations;

	public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

	public Exception? ConnectFailure { get; set; }

	public bool Disposed { get; private set; }

	public bool DisconnectCalled { get; private set; }

	public event EventHandler<StreamlabsEvent>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		Calls.Add($"{StreamlabsRpcFrames.AuthResource}.{StreamlabsRpcFrames.AuthMethod}");
		if (ConnectFailure is { } failure)
		{
			IsConnected = false;
			return Task.FromException(failure);
		}

		IsConnected = true;
		return Task.CompletedTask;
	}

	public Task<JsonElement> InvokeAsync(
		string resource,
		string method,
		IReadOnlyList<object?>? args,
		CancellationToken cancellationToken)
	{
		var invocation = new FakeInvocation(resource, method, args ?? []);
		_invocations.Add(invocation);
		Calls.Add(invocation.ToString());

		if (Failures.TryGetValue(invocation.Key, out var failure))
		{
			return Task.FromException<JsonElement>(failure);
		}

		var json = Responses.GetValueOrDefault(invocation.Key, "null");
		return Task.FromResult(Parse(json));
	}

	public Task<string> SubscribeAsync(string service, string observable, CancellationToken cancellationToken)
	{
		var resourceId = StreamlabsServices.SubscriptionId(service, observable);
		Subscriptions.Add(resourceId);
		return Task.FromResult(resourceId);
	}

	public Task DisconnectAsync()
	{
		DisconnectCalled = true;
		IsConnected = false;
		return Task.CompletedTask;
	}

	public void Dispose() => Disposed = true;

	public void RaiseEvent(string resourceId, string json)
		=> EventReceived?.Invoke(this, new StreamlabsEvent(resourceId, Parse(json)));

	public void RaiseDisconnected(string? reason = "closed")
	{
		IsConnected = false;
		Disconnected?.Invoke(this, reason);
	}

	private static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}
