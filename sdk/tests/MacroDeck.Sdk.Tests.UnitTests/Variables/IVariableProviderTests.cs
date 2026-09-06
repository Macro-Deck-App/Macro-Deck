using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class IVariableProviderTests
{
	/// <summary>
	/// A default interface member does not dispatch off the concrete type - reading
	/// <c>new Plain().DeclaredVariables</c> directly would not even compile - so the default has to be
	/// observed through an <see cref="IVariableProvider"/>-typed reference, exactly as the host reads it.
	/// </summary>
	[Test]
	public void DeclaredVariables_defaults_to_Variables()
	{
		var provided = new[] { VariableDefinition.Eager("temperature", VariableType.Numeric) };
		IVariableProvider provider = new Plain(provided);

		Assert.That(provider.DeclaredVariables, Is.EqualTo(provided));
	}

	[Test]
	public void An_override_wins_over_the_default()
	{
		var provided = new[] { VariableDefinition.Eager("temperature", VariableType.Numeric) };
		var declared = new[] { VariableDefinition.Eager("<account>_temperature", VariableType.Numeric) };
		IVariableProvider provider = new Overriding(provided, declared);

		Assert.That(provider.DeclaredVariables, Is.EqualTo(declared));
	}

	/// <summary>
	/// The factory takes only what every eager variable has to say; everything ADR 0081 added - the
	/// attributes, the write capability, the catalog fields - is an init property that stays at its
	/// inert default, so a declaration that mentions none of them keeps behaving as a plain read-only
	/// scalar.
	/// </summary>
	[Test]
	public void An_eager_declaration_defaults_every_attribute_and_capability_member()
	{
		var declared = VariableDefinition.Eager("cpu", VariableType.Numeric);

		Assert.Multiple(() =>
		{
			Assert.That(declared.Materialization, Is.EqualTo(VariableMaterialization.Eager));
			Assert.That(declared.ResolvedId, Is.EqualTo(VariableDefinitionId.FromName("cpu")));
			Assert.That(declared.DisplayName.IsEmpty, Is.True);
			Assert.That(declared.Configuration, Is.Null);
			Assert.That(declared.Unit, Is.Null);
			Assert.That(declared.SemanticKind, Is.Null);
			Assert.That(declared.Attributes, Is.Null);
			Assert.That(declared.Write, Is.Null);
			Assert.That(declared.CanWrite, Is.False);
			Assert.That(declared.IsBindable, Is.True);
			Assert.That(declared.IsContainer, Is.False);
		});
	}

	[Test]
	public void VariablesDependOnConfiguration_defaults_to_false()
	{
		IVariableProvider provider = new Plain([]);

		Assert.That(provider.VariablesDependOnConfiguration, Is.False);
	}

	/// <summary>
	/// The catalog half of the contract is opt-in: a provider that only declares eager variables must
	/// stay out of the browse tree entirely, and its defaults have to say so without it writing a line.
	/// </summary>
	[Test]
	public void The_catalog_half_is_off_by_default()
	{
		IVariableProvider provider = new Plain([]);

		Assert.Multiple(() =>
		{
			Assert.That(provider.SupportsCatalog, Is.False);
			Assert.That(provider.SupportsPush, Is.False);
			Assert.That(provider.SupportsSearch, Is.False);
			Assert.That(provider.CatalogName, Is.Empty);
		});
	}

	[Test]
	public async Task Discover_and_subscribe_default_to_nothing()
	{
		IVariableProvider provider = new Plain([]);

		var page = await provider.DiscoverAsync(new VariableCatalogQuery());
		var values = await provider.SubscribeAsync(["cpu"]);

		Assert.Multiple(() =>
		{
			Assert.That(page.Items, Is.Empty);
			Assert.That(page.ContinuationToken, Is.Null);
			Assert.That(values, Is.Empty);
		});
	}

	/// <summary>The default resolve is what lets a provider with no catalog still answer for its eager
	/// variables, which the host asks about by the same id either way.</summary>
	[Test]
	public async Task Resolve_defaults_to_matching_an_eager_variable_by_its_id()
	{
		var cpu = VariableDefinition.Eager("cpu", VariableType.Numeric);
		IVariableProvider provider = new Plain([cpu]);

		Assert.Multiple(async () =>
		{
			Assert.That(await provider.ResolveAsync(cpu.ResolvedId!), Is.EqualTo(cpu));
			Assert.That(await provider.ResolveAsync("not-a-variable"), Is.Null);
		});
	}

	/// <summary>Writing is opt-in at the implementation as well as at the declaration: a provider that
	/// never overrides this refuses a write rather than silently swallowing it.</summary>
	[Test]
	public async Task SetValueAsync_defaults_to_refusing_the_write()
	{
		IVariableProvider provider = new Plain([]);

		var result = await provider.SetValueAsync("cpu", 42);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
	}

	[Test]
	public void The_placeholder_wraps_the_label_in_angle_brackets()
	{
		Assert.That(VariableNameTemplate.Placeholder("account"), Is.EqualTo("<account>"));
	}

	private sealed class Plain : IVariableProvider
	{
		public Plain(IReadOnlyList<VariableDefinition> provided) => Variables = provided;

		public IReadOnlyList<VariableDefinition> Variables { get; }

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);
	}

	private sealed class Overriding : IVariableProvider
	{
		private readonly IReadOnlyList<VariableDefinition> _declared;

		public Overriding(IReadOnlyList<VariableDefinition> provided, IReadOnlyList<VariableDefinition> declared)
		{
			Variables = provided;
			_declared = declared;
		}

		public IReadOnlyList<VariableDefinition> Variables { get; }

		public IReadOnlyList<VariableDefinition> DeclaredVariables => _declared;

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);
	}
}
