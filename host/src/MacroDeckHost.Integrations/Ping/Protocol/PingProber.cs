using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MacroDeckHost.Integrations.Ping.State;
using IcmpPing = System.Net.NetworkInformation.Ping;

namespace MacroDeckHost.Integrations.Ping.Protocol;

internal delegate Task<IPAddress[]> PingResolver(string host, CancellationToken cancellationToken);

internal delegate Task<PingProbeResult> PingEcho(
	IPAddress address,
	TimeSpan timeout,
	CancellationToken cancellationToken);

internal sealed class PingProber : IPingProber
{
	public const int MaximumAddresses = 4;

	private static readonly TimeSpan _minimumResolveTime = TimeSpan.FromSeconds(5);

	private readonly PingResolver _resolve;
	private readonly PingEcho _echo;

	public PingProber()
		: this(Dns.GetHostAddressesAsync, EchoAsync)
	{
	}

	internal PingProber(PingResolver resolve, PingEcho echo)
	{
		_resolve = resolve;
		_echo = echo;
	}

	public async Task<PingProbeResult> ProbeAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
	{
		IPAddress[] addresses;
		if (IPAddress.TryParse(host, out var literal))
		{
			addresses = [literal];
		}
		else
		{
			var resolved = await ResolveAsync(host, timeout, cancellationToken).ConfigureAwait(false);
			if (resolved is not { Length: > 0 })
			{
				return new PingProbeResult(PingProblem.NameNotResolved);
			}

			addresses = resolved;
		}

		PingProbeResult? conclusive = null;
		PingProbeResult? inconclusive = null;
		foreach (var address in addresses.Take(MaximumAddresses))
		{
			var result = await _echo(address, timeout, cancellationToken).ConfigureAwait(false);
			if (result.Succeeded)
			{
				return result;
			}

			if (result.Problem is PingProblem.TimedOut or PingProblem.Unreachable)
			{
				conclusive ??= result;
			}
			else
			{
				inconclusive ??= result;
			}
		}

		return conclusive ?? inconclusive ?? new PingProbeResult(PingProblem.Failed);
	}

	internal static PingProblem Classify(Exception exception)
	{
		return exception switch
		{
			PlatformNotSupportedException or UnauthorizedAccessException => PingProblem.NotPermitted,
			SocketException { SocketErrorCode: SocketError.AccessDenied } => PingProblem.NotPermitted,
			SocketException
			{
				SocketErrorCode: SocketError.NetworkUnreachable
				or SocketError.HostUnreachable
				or SocketError.AddressNotAvailable
			} => PingProblem.Unreachable,
			SocketException => PingProblem.Failed,
			Win32Exception => PingProblem.NotPermitted,
			PingException { InnerException: { } inner } => Classify(inner),
			_ => PingProblem.Failed
		};
	}

	internal static PingProblem Classify(IPStatus status)
	{
		return status switch
		{
			IPStatus.Success => PingProblem.None,
			IPStatus.TimedOut or IPStatus.TimeExceeded or IPStatus.TtlExpired => PingProblem.TimedOut,
			_ => PingProblem.Unreachable
		};
	}

	private async Task<IPAddress[]?> ResolveAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		bounded.CancelAfter(timeout > _minimumResolveTime ? timeout : _minimumResolveTime);
		try
		{
			return await _resolve(host, bounded.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception exception) when (exception is SocketException or ArgumentException)
		{
			return null;
		}
	}

	private static async Task<PingProbeResult> EchoAsync(
		IPAddress address,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		using var ping = new IcmpPing();
		try
		{
			var reply = await ping.SendPingAsync(address, timeout, null, null, cancellationToken).ConfigureAwait(false);
			var problem = Classify(reply.Status);
			return problem == PingProblem.None
				? PingProbeResult.Reply(reply.RoundtripTime)
				: new PingProbeResult(problem);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
#pragma warning disable CA1031 // Any platform failure of a single echo is a probe result, never a crash.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			return new PingProbeResult(Classify(exception));
		}
	}
}
