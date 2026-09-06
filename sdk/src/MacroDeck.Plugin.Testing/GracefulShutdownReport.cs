namespace MacroDeck.Plugin.Testing;

/// <summary>What actually happened when <see cref="ExternalPlugin.StopGracefullyAsync" /> asked a plugin process to shut down.</summary>
public sealed record GracefulShutdownReport
{
	/// <summary>True when the process exited on its own within the grace period - never had to be killed.</summary>
	public required bool ExitedWithinGrace { get; init; }

	/// <summary>How long shutdown took, from the first signal to the process actually being gone (or given up on).</summary>
	public required TimeSpan Elapsed { get; init; }

	/// <summary>The process's exit code, when it exited. Null if it never did.</summary>
	public int? ExitCode { get; init; }

	/// <summary>True when the process did not exit within the grace period and had to be killed.</summary>
	public required bool Killed { get; init; }
}
