using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk;

/// <summary>
/// What an out-of-process plugin implements: a bundle of actions, and optionally variables, events, a
/// config flow and the other capability interfaces in this assembly.
///
/// <para>
/// Deliberately smaller than <see cref="IIntegration" />, which the in-process host still uses. A plugin
/// states no <c>Id</c>, <c>Name</c> or <c>Version</c>, because <c>manifest.json</c> already does and
/// nothing out of process ever read the class's copies - only the manifest's values reach the host. The
/// icon is the manifest's <c>icon</c> path for the same reason; there is no icon member here. Removing
/// the members is what stops the two from drifting: the compiler, not an analyzer, is what makes
/// restating them impossible.
/// </para>
///
/// <para>
/// There is no <c>IsInitialized</c> either. It exists on <see cref="IIntegration" /> to gate whether an
/// in-process capability is currently usable, a question the host answers for a plugin from the session's
/// own state. Keep such a flag privately if the plugin's own code needs it.
/// </para>
///
/// <para>Register one with <c>PluginHostBuilder.RegisterIntegration&lt;T&gt;()</c>.</para>
/// </summary>
public interface IPluginIntegration
{
	/// <summary>
	/// The actions this integration offers. Complete and side-effect free from construction onward: the
	/// capability catalog is built from this before <see cref="InitializeAsync" /> runs, so building the
	/// list must not connect, probe hardware, or perform any other I/O.
	/// </summary>
	IReadOnlyList<IActionDefinition> Actions { get; }

	/// <summary>Connects the integration to whatever it controls and starts any background work.</summary>
	Task InitializeAsync(IIntegrationContext context);

	/// <summary>Disconnects and releases whatever <see cref="InitializeAsync" /> acquired.</summary>
	Task ShutdownAsync();
}
