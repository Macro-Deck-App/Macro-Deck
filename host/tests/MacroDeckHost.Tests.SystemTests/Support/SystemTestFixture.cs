using System.Diagnostics;
using MacroDeck.Plugin.Testing;
using NUnit.Framework.Interfaces;

namespace MacroDeckHost.Tests.SystemTests.Support;

// Every test owns its data, temp directory and hosts; a failed test keeps the full host output as an
// attachment, because a system test that only says "timed out" cannot be diagnosed.
[NonParallelizable]
internal abstract class SystemTestFixture
{
	private readonly List<HostInstance> _hosts = [];

	protected TempStateDirectory State { get; private set; } = null!;

	protected string ArtifactsDirectory => Path.Combine(State.Path, "artifacts");

	[SetUp]
	public void CreateState()
	{
		State = new TempStateDirectory();
	}

	[TearDown]
	public async Task DisposeHosts()
	{
		var failed = TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed;
		foreach (var host in _hosts)
		{
			if (failed)
			{
				await TestContext.Out.WriteLineAsync(host.DescribeOutput());
				TestContext.AddTestAttachment(host.SaveOutput(TestContext.CurrentContext.WorkDirectory));
			}

			await host.DisposeAsync();
		}

		_hosts.Clear();
		State.Dispose();
	}

	protected async Task<HostInstance> StartHostAsync(IReadOnlyDictionary<string, string?>? environment = null)
	{
		var host = await HostInstance.StartAsync(State.Path, environment);
		_hosts.Add(host);
		return host;
	}

	protected static bool HasExited(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			return process.HasExited;
		}
		catch (ArgumentException)
		{
			return true;
		}
	}
}
