using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class FakeAdbProcessRunner : IAdbProcessRunner
{
	private readonly
		List<(Func<IReadOnlyList<string>, bool> Matches, Func<IReadOnlyList<string>, AdbProcessResult> Respond)>
		_scripts = [];

	public List<IReadOnlyList<string>> Invocations { get; } = [];

	public AdbProcessResult DefaultResult { get; set; } = new(true, 0, string.Empty, string.Empty, false);

	public AdbBinaryResult DefaultBinaryResult { get; set; } = new(true, 0, [], string.Empty, false);

	public TaskCompletionSource<AdbProcessResult>? HangForever { get; set; }

	public int DrainAsyncCallCount { get; private set; }

	public void When(Func<IReadOnlyList<string>, bool> matches, AdbProcessResult result) =>
		_scripts.Add((matches, _ => result));

	public void When(Func<IReadOnlyList<string>, bool> matches, Func<IReadOnlyList<string>, AdbProcessResult> respond)
		=> _scripts.Add((matches, respond));

	public Task<AdbProcessResult> RunAsync(
		string executablePath,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		Invocations.Add(arguments);

		if (HangForever is not null)
		{
			return HangForever.Task;
		}

		foreach (var (matches, respond) in _scripts)
		{
			if (matches(arguments))
			{
				return Task.FromResult(respond(arguments));
			}
		}

		return Task.FromResult(DefaultResult);
	}

	public Task<AdbBinaryResult> RunBinaryAsync(
		string executablePath,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		Invocations.Add(arguments);
		return Task.FromResult(DefaultBinaryResult);
	}

	public Task DrainAsync(TimeSpan budget)
	{
		DrainAsyncCallCount++;
		return Task.CompletedTask;
	}
}
