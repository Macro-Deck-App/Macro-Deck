using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal interface INativeLinkSession
{
	bool IsLinked { get; }

	bool EverLinked { get; }

	bool ByeReceived { get; }

	bool Ended { get; }

	Task Completion { get; }

	void Stop();
}

internal sealed class NativeLinkSession : INativeLinkSession, IDisposable
{
	private readonly NativeLink _link;
	private readonly CancellationTokenSource _stop = new();

	public NativeLinkSession(ILinkCarrier carrier,
		string deviceKey,
		ILinkStreamDialer dialer,
		TimeProvider time,
		ILogger logger)
	{
		_link = new NativeLink(carrier, deviceKey, dialer, time, logger);
		Completion = TaskObservation.Settle(Task.Run(() => RunAsync(carrier)), logger);
	}

	public bool IsLinked => !Ended && _link.IsLinked;

	public bool EverLinked => _link.EverLinked;

	public bool ByeReceived => _link.ByeReceived;

	public bool Ended => Completion.IsCompleted;

	public Task Completion { get; }

	public void Stop()
	{
		try
		{
			TaskObservation.Observe(_stop.CancelAsync());
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task RunAsync(ILinkCarrier carrier)
	{
		try
		{
			await _link.RunAsync(_stop.Token);
		}
		finally
		{
			await carrier.DisposeAsync();
			Dispose();
		}
	}

	internal Task LoopsEnded => _link.LoopsEnded;

	public void Dispose() => _stop.Dispose();
}
