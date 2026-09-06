using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal sealed class FakeConnectSessionService : IConnectSessionService
{
	public ConnectSessionSnapshot Current { get; set; } = ConnectSessionSnapshot.SignedOut;

	public event EventHandler<ConnectSessionSnapshot>? SessionChanged;

	public int StartSignInCount { get; private set; }

	public int CancelSignInCount { get; private set; }

	public int SignOutCount { get; private set; }

	public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<ConnectSignInStart> StartSignIn(CancellationToken cancellationToken = default)
	{
		StartSignInCount++;
		SessionChanged?.Invoke(this, Current);

		return Task.FromResult(new ConnectSignInStart(new Uri("https://accounts.macro-deck.app/device"),
			new Uri("https://accounts.macro-deck.app/device?user_code=1234-5678"),
			"1234-5678",
			DateTimeOffset.UnixEpoch));
	}

	public Task CancelSignIn(CancellationToken cancellationToken = default)
	{
		CancelSignInCount++;
		return Task.CompletedTask;
	}

	public Task SignOut(CancellationToken cancellationToken = default)
	{
		SignOutCount++;
		return Task.CompletedTask;
	}

	public Task<string> GetAccessToken(CancellationToken cancellationToken = default)
		=> Task.FromResult("access-token");
}
