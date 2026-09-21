using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAdbInvokeRunnerTests
{
	private const string PluginId = "com.example.android";

	[Test]
	public async Task A_slow_adb_call_does_not_hold_up_the_caller_and_answers_once_it_finishes()
	{
		var world = new World();

		await world.Runner.StartAsync(world.Connection, PluginId, "c1", Shell(), CancellationToken.None);
		var sentWhileRunning = world.Connection.Sent.Count;
		world.Finish(new AdbShellOutput(0, "done", string.Empty, false));
		var result = await world.ResultFor("c1");

		Assert.Multiple(() =>
		{
			Assert.That(sentWhileRunning, Is.Zero);
			Assert.That(result.Error, Is.Null);
		});
	}

	[Test]
	public async Task A_cancelled_call_gets_exactly_one_CANCELLED_result()
	{
		var world = new World();
		await world.Runner.StartAsync(world.Connection, PluginId, "c1", Shell(), CancellationToken.None);

		world.Runner.Cancel(world.Connection, "c1");
		var result = await world.ResultFor("c1");
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.Cancelled));
			Assert.That(world.Connection.Sent.Count(envelope => envelope.CorrelationId == "c1"), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_connection_ending_cancels_its_calls()
	{
		var world = new World();
		using var connectionLifetime = new CancellationTokenSource();
		await world.Runner.StartAsync(world.Connection, PluginId, "c1", Shell(), connectionLifetime.Token);

		await connectionLifetime.CancelAsync();

		Assert.That((await world.ResultFor("c1")).Error?.Code, Is.EqualTo(ProtocolErrorCodes.Cancelled));
	}

	[Test]
	public async Task A_call_beyond_the_per_plugin_limit_is_refused_at_once_as_rate_limited()
	{
		var world = new World();
		for (var index = 0; index < PluginAdbInvokeRunner.MaxInFlightPerPlugin; index++)
		{
			await world.Runner.StartAsync(world.Connection, PluginId, $"c{index}", Shell(), CancellationToken.None);
		}

		await world.Runner.StartAsync(world.Connection, PluginId, "one-too-many", Shell(), CancellationToken.None);

		var refusal = await world.ResultFor("one-too-many");
		Assert.Multiple(() =>
		{
			Assert.That(refusal.Error?.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
			Assert.That(refusal.Error?.Retryable, Is.True);
		});
	}

	[Test]
	public async Task A_refused_call_never_takes_one_of_the_plugins_slots()
	{
		var world = new World(PluginAdbAccess.NotAllowed);
		for (var index = 0; index < PluginAdbInvokeRunner.MaxInFlightPerPlugin + 2; index++)
		{
			await world.Runner.StartAsync(world.Connection, PluginId, $"c{index}", Shell(), CancellationToken.None);
		}

		Assert.That(world.Connection.Sent.Select(envelope => envelope.Error?.Code).Distinct(),
			Is.EqualTo(new[] { ProtocolErrorCodes.AdbNotAllowed }));
	}

	[Test]
	public async Task Each_call_is_admitted_by_the_router_exactly_once()
	{
		var world = new World();
		world.Finish(new AdbShellOutput(0, string.Empty, string.Empty, false));

		await world.Runner.StartAsync(world.Connection, PluginId, "c1", Shell(), CancellationToken.None);
		await world.ResultFor("c1");

		Assert.That(world.Router.Admissions, Is.EqualTo(1));
	}

	[Test]
	public async Task A_correlation_id_that_is_still_running_is_not_started_twice()
	{
		var world = new World();
		await world.Runner.StartAsync(world.Connection, PluginId, "c1", Shell(), CancellationToken.None);

		await world.Runner.StartAsync(world.Connection, PluginId, "c1", Shell(), CancellationToken.None);

		Assert.That((await world.ResultFor("c1")).Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public async Task A_failure_while_deciding_access_is_answered_as_an_internal_error_instead_of_ending_the_session()
	{
		var connection = new FakePluginConnection();
		var runner = new PluginAdbInvokeRunner(new CountingRouter(),
			new PluginAdbCallbacks(new FakeAdbDeviceOperations(), new ThrowingAccessPolicy(), new FakeHostLockState()),
			Serilog.Core.Logger.None);

		await runner.StartAsync(connection, PluginId, "c1", Shell(), CancellationToken.None);

		Assert.That(connection.Sent.Single().Error?.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
	}

	private static HostInvokePayload Shell() => PluginAdbCallbacksTests.Payload(HostOperations.Adb.Shell);

	private sealed class World
	{
		private readonly TaskCompletionSource<AdbShellOutput> _shell = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public World(PluginAdbAccess access = PluginAdbAccess.Available)
		{
			var operations = new FakeAdbDeviceOperations
			{
				Shell = async cancellationToken =>
					Result.Ok<AdbShellOutput, AdbFailureCode>(await _shell.Task.WaitAsync(cancellationToken))
			};
			Runner = new PluginAdbInvokeRunner(Router,
				new PluginAdbCallbacks(operations, new FixedAdbAccessPolicy(access), new FakeHostLockState()),
				Serilog.Core.Logger.None);
		}

		public CountingRouter Router { get; } = new();

		public FakePluginConnection Connection { get; } = new();

		public PluginAdbInvokeRunner Runner { get; }

		public void Finish(AdbShellOutput output) => _shell.TrySetResult(output);

		public async Task<ProtocolEnvelope> ResultFor(string correlationId)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (DateTime.UtcNow < deadline)
			{
				var sent = Connection.Sent.ToList().FirstOrDefault(envelope =>
					envelope.CorrelationId == correlationId && envelope.Type == MessageTypes.HostResult);
				if (sent is not null)
				{
					return sent;
				}

				await Task.Delay(10);
			}

			throw new TimeoutException($"No host.result for {correlationId}.");
		}
	}

	private sealed class CountingRouter : IPluginCallbackRouter
	{
		public int Admissions { get; private set; }

		public Task<HostCallbackResult> RouteAsync(string pluginId,
			string correlationId,
			HostInvokePayload payload,
			CancellationToken cancellationToken) => throw new NotSupportedException();

		public HostCallbackResult? Admit(string pluginId, HostInvokePayload payload)
		{
			Admissions++;
			return null;
		}
	}
}

internal sealed class ThrowingAccessPolicy : IPluginAdbAccessPolicy
{
	public Task<PluginAdbAccess> EvaluateAsync(string pluginId, CancellationToken cancellationToken = default)
		=> throw new IOException("The preferences could not be read.");

	public void Refresh(MacroDeckHost.Application.Services.AdbSettings settings)
	{
	}

	public bool CanBeGranted(string pluginId) => false;
}
