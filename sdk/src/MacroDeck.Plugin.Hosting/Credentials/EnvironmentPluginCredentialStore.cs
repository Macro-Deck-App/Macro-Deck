using Microsoft.Extensions.Options;

namespace MacroDeck.Plugin.Hosting.Credentials;

/// <summary>
/// Managed mode: the host registered the plugin and launched the process with the id and secret in
/// its environment. Nothing is read from or written to disk.
///
/// <para>
/// Passing a secret through the environment is no weaker than a file for a same-user supervisor - on
/// Linux <c>/proc/&lt;pid&gt;/environ</c> is owner-readable only, and on Windows reading another
/// process's environment block needs a token for the same user. It is weaker against anything that
/// captures a process list together with its environment, which some crash reporters do.
/// </para>
/// </summary>
internal sealed class EnvironmentPluginCredentialStore(IOptions<PluginHostOptions> options) : IPluginCredentialStore
{
	private readonly PluginHostOptions _options = options.Value;

	public bool CanSave => false;

	public Task<PluginCredentials?> LoadAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_options.Id) || string.IsNullOrEmpty(_options.Secret))
		{
			return Task.FromResult<PluginCredentials?>(null);
		}

		return Task.FromResult<PluginCredentials?>(
			new PluginCredentials(_options.Id, _options.HostUrl, _options.Secret));
	}

	public Task SaveAsync(PluginCredentials credentials, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException(
			"A managed plugin does not register itself, so it has no credentials to persist. " +
			"The host issues them and passes them in the environment.");
}
