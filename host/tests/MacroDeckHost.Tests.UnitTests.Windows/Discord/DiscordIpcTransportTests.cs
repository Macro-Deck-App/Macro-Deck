using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using MacroDeckHost.Integrations.Discord.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Windows.Discord;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class DiscordIpcTransportTests
{
	private static readonly TimeSpan _connectTimeout = TimeSpan.FromMilliseconds(300);

	private readonly List<IDisposable> _resources = [];

	[TearDown]
	public void TearDown()
	{
		foreach (var resource in Enumerable.Reverse(_resources))
		{
			resource.Dispose();
		}

		_resources.Clear();
	}

	[Test]
	public void A_pipe_that_refuses_access_reports_who_owns_it()
	{
		var name = UniqueName();
		ServeRestricted(name);
		using var transport = new DiscordIpcTransport([name], _connectTimeout);
		using var self = Process.GetCurrentProcess();
		using var identity = WindowsIdentity.GetCurrent();

		var exception = Assert.ThrowsAsync<DiscordIpcUnavailableException>(async () =>
			await transport.ConnectAsync(new HashSet<string>(StringComparer.Ordinal), CancellationToken.None));

		var denial = exception!.Denial;
		Assert.Multiple(() =>
		{
			Assert.That(denial?.Endpoint, Is.EqualTo($@"\\.\pipe\{name}"));
			Assert.That(denial?.Server?.ProcessId, Is.EqualTo(Environment.ProcessId));
			Assert.That(denial?.Server?.ProcessName, Is.EqualTo(self.ProcessName));
			Assert.That(denial?.Server?.SessionId, Is.EqualTo(self.SessionId));
			Assert.That(denial?.Server?.UserSid, Is.EqualTo(identity.User?.Value));
			Assert.That(denial?.Server?.Elevated, Is.Not.Null.And.EqualTo(denial?.Self?.Elevated));
			Assert.That(denial?.Cause, Is.EqualTo(DiscordIpcDenialCause.OtherProgram));
		});
	}

	[Test]
	public void A_busy_pipe_does_not_stop_the_scan()
	{
		var busy = UniqueName();
		var busyServer = new NamedPipeServerStream(busy,
			PipeDirection.InOut,
			1,
			PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous);
		_resources.Add(busyServer);
		var occupant = new NamedPipeClientStream(".", busy, PipeDirection.InOut);
		_resources.Add(occupant);
		var accepted = busyServer.WaitForConnectionAsync();
		occupant.Connect(1000);
		accepted.Wait();

		var restricted = UniqueName();
		ServeRestricted(restricted);
		using var transport = new DiscordIpcTransport([busy, restricted], _connectTimeout);

		var exception = Assert.ThrowsAsync<DiscordIpcUnavailableException>(async () =>
			await transport.ConnectAsync(new HashSet<string>(StringComparer.Ordinal), CancellationToken.None));

		Assert.That(exception!.Denial?.Endpoint, Is.EqualTo($@"\\.\pipe\{restricted}"));
	}

	private static string UniqueName()
	{
		return "mdtest-ipc-" + Guid.NewGuid().ToString("N");
	}

	// Everyone may only read, like the default DACL of an elevated server. The owner keeps
	// FILE_CREATE_PIPE_INSTANCE so spare instances survive the existence check and the probe.
	private void ServeRestricted(string name)
	{
		using var identity = WindowsIdentity.GetCurrent();
		var security = new PipeSecurity();
		security.SetSecurityDescriptorSddlForm($"D:(A;;FR;;;WD)(A;;0x4;;;{identity.User!.Value})");

		for (var i = 0; i < 4; i++)
		{
			var server = NamedPipeServerStreamAcl.Create(name,
				PipeDirection.In,
				4,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous,
				0,
				0,
				security);
			_resources.Add(server);
			_ = server.WaitForConnectionAsync()
				.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
		}
	}
}
