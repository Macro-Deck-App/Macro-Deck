namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A plugin process launched via <see cref="MacroDeckTestHost.LaunchAsync" /> exited on its own before
/// it started serving <c>/_macrodeck/health</c>. Carries the already-exited <see cref="Plugin" /> so a
/// test can assert on <see cref="ExternalPlugin.HasExited" />, <see cref="ExternalPlugin.ExitCode" /> and
/// <see cref="ExternalPlugin.StandardError" /> directly, rather than parsing them back out of this
/// exception's message.
/// </summary>
public sealed class PluginProcessExitedException : InvalidOperationException
{
	/// <summary>
	/// The plugin that exited. Already dead by the time this exception is thrown, so
	/// <see cref="MacroDeckTestHost.LaunchAsync" /> does not dispose it itself - unlike a process that
	/// never exits, an already-exited one holds no resource worth reclaiming urgently - but the caller
	/// now owns it and should still dispose it once done inspecting it.
	/// </summary>
	public ExternalPlugin Plugin { get; }

	/// <summary>Creates the exception with a message describing the exit and the plugin that produced it.</summary>
	public PluginProcessExitedException(string message, ExternalPlugin plugin)
		: base(message)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		Plugin = plugin;
	}
}
