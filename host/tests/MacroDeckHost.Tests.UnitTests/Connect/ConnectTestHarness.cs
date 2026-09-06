using MacroDeckHost.Infrastructure.Connect;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal sealed class ConnectTestHarness : IAsyncDisposable
{
	private readonly List<ConnectSessionService> _services = [];
	private readonly CancellationTokenSource _parked = new();

	public ConnectTestHarness()
	{
		Time = new ManualTimeProvider();
		Identity = new FakeConnectIdentityClient(Time);
		Store = new FakeConnectCredentialStore();
		Floor = new FakeConnectSuspensionFloor();
		Persister = new ConnectTokenPersister(Store, Log.Logger);
		// Parked like DelayHandler below: a session-service test must not have a device authorization poll
		// spinning behind it.
		Flow = new ConnectSignInFlow(Identity,
			Time,
			Log.Logger,
			(_, ct) => Task.Delay(Timeout.Infinite,
				CancellationTokenSource.CreateLinkedTokenSource(ct, _parked.Token).Token));

		// Parked by default: a test that cares about the retry schedule replaces this, everything else must
		// not have a backoff loop spinning behind it.
		DelayHandler = (_, ct) => Task.Delay(Timeout.Infinite,
			CancellationTokenSource
				.CreateLinkedTokenSource(ct, _parked.Token)
				.Token);

		Service = CreateService();
	}

	public ManualTimeProvider Time { get; }

	public FakeConnectIdentityClient Identity { get; }

	public FakeConnectCredentialStore Store { get; }

	public FakeConnectSuspensionFloor Floor { get; }

	public ConnectTokenPersister Persister { get; }

	public ConnectSignInFlow Flow { get; }

	public ConnectSessionService Service { get; }

	public List<TimeSpan> Delays { get; } = [];

	public Func<TimeSpan, CancellationToken, Task> DelayHandler { get; set; }

	public ConnectSessionService CreateService()
	{
		var service = new ConnectSessionService(Identity,
			Store,
			Persister,
			Flow,
			Floor,
			Time,
			Log.Logger,
			(span, ct) => DelayHandler(span, ct),
			span => span);

		_services.Add(service);
		return service;
	}

	public async ValueTask DisposeAsync()
	{
		await _parked.CancelAsync();
		Identity.StallGate.TrySetResult();

		foreach (var service in _services)
		{
			await service.DisposeAsync();
		}

		await Flow.DisposeAsync();
		_parked.Dispose();
	}

	public static Task WaitUntil(Func<Task<bool>> condition, string because)
		=> WaitUntilCore(condition, because);

	public static Task WaitUntil(Func<bool> condition, string because)
		=> WaitUntilCore(() => Task.FromResult(condition()), because);

	private static async Task WaitUntilCore(Func<Task<bool>> condition, string because)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (await condition())
			{
				return;
			}

			await Task.Delay(10);
		}

		Assert.Fail(because);
	}
}
