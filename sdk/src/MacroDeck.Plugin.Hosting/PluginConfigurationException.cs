namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// Thrown by <see cref="PluginHostBuilder.Build" /> when the plugin as configured cannot run: a
/// missing or malformed id, an illegal capability id, a route under the reserved prefix, a service
/// graph that will not resolve.
///
/// <para>
/// It carries <em>every</em> problem found, not the first. An author fixing a plugin should need one
/// run to see the whole list rather than one run per mistake.
/// </para>
/// </summary>
public sealed class PluginConfigurationException : Exception
{
	public PluginConfigurationException()
		: this([])
	{
	}

	public PluginConfigurationException(string message)
		: this([message])
	{
	}

	public PluginConfigurationException(string message, Exception innerException)
		: base(message, innerException)
		=> Problems = [message];

	public PluginConfigurationException(IReadOnlyList<string> problems)
		: base(Describe(problems))
		=> Problems = problems;

	/// <summary>One line per problem, in the order they were found.</summary>
	public IReadOnlyList<string> Problems { get; } = [];

	private static string Describe(IReadOnlyList<string> problems)
		=> problems.Count switch
		{
			0 => "The plugin is not configured correctly.",
			1 => problems[0],
			_ => "The plugin is not configured correctly:" +
				Environment.NewLine +
				string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem))
		};
}
