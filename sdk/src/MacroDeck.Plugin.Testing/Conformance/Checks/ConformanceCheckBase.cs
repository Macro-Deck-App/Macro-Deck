namespace MacroDeck.Plugin.Testing.Conformance.Checks;

/// <summary>
/// Implements <see cref="IConformanceCheck" />'s metadata plumbing so each concrete check supplies its id,
/// title, category, requirement and preconditions once, in its constructor, and implements only
/// <see cref="RunAsync" />. Internal, and so are every concrete check in this namespace: a consumer always
/// goes through the <see cref="IConformanceCheck" /> interface via <see cref="ConformanceRunner.Checks" />,
/// which is what keeps 35 check classes from being 35 more names in this package's public surface.
/// </summary>
internal abstract class ConformanceCheckBase : IConformanceCheck
{
	protected ConformanceCheckBase(
		string id,
		string title,
		ConformanceCategory category,
		ConformanceRequirement requirement,
		params ConformancePrecondition[] requires)
	{
		Id = id;
		Title = title;
		Category = category;
		Requirement = requirement;
		Requires = requires;
	}

	public string Id { get; }

	public string Title { get; }

	public ConformanceCategory Category { get; }

	public ConformanceRequirement Requirement { get; }

	public IReadOnlyList<ConformancePrecondition> Requires { get; }

	public abstract Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken);
}
