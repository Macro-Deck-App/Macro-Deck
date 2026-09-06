using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal sealed class FakeConnectCredentialStore : IConnectCredentialStore
{
	private readonly Lock _sync = new();

	private ConnectCredential? _credential;
	private int _saves;
	private int _loads;
	private int _clears;

	public int SaveCount => Volatile.Read(ref _saves);

	public int LoadCount => Volatile.Read(ref _loads);

	public int ClearCount => Volatile.Read(ref _clears);

	public List<ConnectCredential> Saved { get; } = [];

	/// <summary>Set by a test to hold the next <see cref="Save"/> open.</summary>
	public TaskCompletionSource? Gate { get; set; }

	public bool FailNextSave { get; set; }

	public void Seed(ConnectCredential credential)
	{
		lock (_sync)
		{
			_credential = credential;
		}
	}

	public Task<ConnectCredential?> Load(CancellationToken cancellationToken = default)
	{
		Interlocked.Increment(ref _loads);
		lock (_sync)
		{
			return Task.FromResult(_credential);
		}
	}

	public async Task Save(ConnectCredential credential, CancellationToken cancellationToken = default)
	{
		TaskCompletionSource? gate;
		lock (_sync)
		{
			gate = Gate;
			Gate = null;
		}

		if (gate is not null)
		{
			await gate.Task;
		}

		Interlocked.Increment(ref _saves);

		lock (_sync)
		{
			if (FailNextSave)
			{
				FailNextSave = false;
				throw new IOException("The credential store is unavailable.");
			}

			_credential = credential;
			Saved.Add(credential);
		}
	}

	public Task Clear(CancellationToken cancellationToken = default)
	{
		Interlocked.Increment(ref _clears);
		lock (_sync)
		{
			_credential = null;
		}

		return Task.CompletedTask;
	}
}
