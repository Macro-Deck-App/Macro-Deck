using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>The catalog half of <see cref="VariablesCapabilityHandler" /> - browse, resolve and
/// subscribe. The eager half is covered by <see cref="VariablesCapabilityHandlerTests" />.</summary>
[TestFixture]
public class VariableCatalogCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(
		string operation,
		object? arguments = null,
		string localId = "provider")
		=> new()
		{
			Kind = CapabilityKinds.Variables,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static VariablesCapabilityHandler Handler(params IPluginIntegration[] integrations)
		=> new(integrations,
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);

	[Test]
	public void An_integration_that_is_not_a_variable_provider_declares_no_capability()
	{
		var handler = Handler(new PlainIntegration());

		Assert.That(handler.DeclareCapabilities(), Is.Empty);
	}

	/// <summary>
	/// A catalog may back tens of thousands of resources; declaring one capability per resource would
	/// blow MaxDeclaredCapabilities. Only the eager set is declared, so a provider whose whole surface is
	/// its catalog declares nothing at all and addresses every resource through operation arguments.
	/// </summary>
	[Test]
	public void A_catalog_only_provider_declares_no_capability_per_resource()
	{
		var handler = Handler(new FakeCatalogProvider
		{
			Page = new VariableCatalogPage { Items = [Item("entity/a"), Item("entity/b")] }
		});

		Assert.That(handler.DeclareCapabilities(), Is.Empty);
	}

	/// <summary>
	/// The host models a plugin as one integration, so a second catalog's resource ids could never be
	/// routed back to the provider that owns them - which has to fail loudly at construction rather than
	/// silently pick one.
	/// </summary>
	[Test]
	public void A_second_catalog_provider_is_rejected_at_construction()
		=> Assert.That(() => Handler(new FakeCatalogProvider(), new FakeCatalogProvider()),
			Throws.InstanceOf<InvalidOperationException>());

	/// <summary>
	/// Security invariant: a provider must not be able to smuggle another owner's namespace, or a
	/// control character, into a resource id that ends up qualified into persisted configuration.
	/// </summary>
	[Test]
	public async Task A_page_item_whose_id_is_qualified_or_malformed_is_dropped()
	{
		var provider = new FakeCatalogProvider
		{
			Page = new VariableCatalogPage
			{
				Items =
				[
					Item("entity/light.living_room"),
					Item("other-owner::entity/x"),
					Item("entity/x\ny")
				]
			}
		};
		var handler = Handler(provider);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Discover),
			CancellationToken.None);

		var page = Deserialize<VariableCatalogPageResult>(result);

		Assert.Multiple(() =>
		{
			Assert.That(page.Items, Has.Count.EqualTo(1));
			Assert.That(page.Items[0].Id, Is.EqualTo("entity/light.living_room"));
		});
	}

	/// <summary>An eager definition is not a catalog resource: it is declared, materialized and read
	/// through its own declared local id, so letting one out of <c>discover</c> would give the same
	/// variable two identities.</summary>
	[Test]
	public async Task A_page_item_declaring_eager_materialization_is_dropped()
	{
		var provider = new FakeCatalogProvider
		{
			Page = new VariableCatalogPage
			{
				Items = [Item("entity/a"), VariableDefinition.Eager("cpu_temp", VariableType.Numeric)]
			}
		};
		var handler = Handler(provider);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Discover),
			CancellationToken.None);

		var page = Deserialize<VariableCatalogPageResult>(result);

		Assert.Multiple(() =>
		{
			Assert.That(page.Items, Has.Count.EqualTo(1));
			Assert.That(page.Items[0].Id, Is.EqualTo("entity/a"));
		});
	}

	[Test]
	public async Task A_page_larger_than_the_protocol_bound_is_truncated_not_rejected()
	{
		var items = Enumerable.Range(0, ProtocolLimits.MaxVariableCatalogPageSize + 50)
			.Select(index => Item($"entity/{index}"))
			.ToList();
		var provider = new FakeCatalogProvider { Page = new VariableCatalogPage { Items = items } };
		var handler = Handler(provider);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Discover),
			CancellationToken.None);

		var page = Deserialize<VariableCatalogPageResult>(result);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(page.Items, Has.Count.EqualTo(ProtocolLimits.MaxVariableCatalogPageSize));
		});
	}

	[Test]
	public async Task A_requested_page_size_above_the_protocol_bound_is_clamped()
	{
		var provider = new FakeCatalogProvider { Page = VariableCatalogPage.Empty };
		var handler = Handler(provider);

		await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Discover,
				new VariableDiscoverArguments { PageSize = ProtocolLimits.MaxVariableCatalogPageSize + 500 }),
			CancellationToken.None);

		Assert.That(provider.LastQuery!.PageSize, Is.EqualTo(ProtocolLimits.MaxVariableCatalogPageSize));
	}

	/// <summary>A resource id that never came out of <c>discover</c> - a stored binding whose entity has
	/// since gone - resolves to a present body with a null definition, which is how "not resolvable" is
	/// told apart from "no data".</summary>
	[Test]
	public async Task An_unresolvable_id_answers_a_body_with_a_null_definition()
	{
		var handler = Handler(new FakeCatalogProvider());

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Resolve,
				new VariableResolveArguments { Id = "entity/gone" }),
			CancellationToken.None);

		Assert.That(Deserialize<VariableResolveResult>(result).Definition, Is.Null);
	}

	[Test]
	public async Task Subscribe_replaces_the_working_set_rather_than_accumulating()
	{
		var provider = new FakeCatalogProvider();
		var handler = Handler(provider);

		await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Subscribe,
				new VariableSubscribeArguments { Ids = ["a", "b"] }),
			CancellationToken.None);

		Assert.That(provider.LastSubscribedIds, Is.EquivalentTo(new[] { "a", "b" }));

		await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Subscribe,
				new VariableSubscribeArguments { Ids = ["b"] }),
			CancellationToken.None);

		Assert.That(provider.LastSubscribedIds, Is.EquivalentTo(new[] { "b" }));
	}

	/// <summary>
	/// "Watch nothing" is a legitimate instruction - a user unbinding the last resource must still tell
	/// the provider to stop watching everything, so the call has to go through rather than be optimized
	/// away as a no-op.
	/// </summary>
	[Test]
	public async Task Subscribing_to_nothing_still_reaches_the_provider()
	{
		var provider = new FakeCatalogProvider();
		var handler = Handler(provider);

		await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Subscribe,
				new VariableSubscribeArguments { Ids = [] }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(provider.SubscribeAsyncWasCalled, Is.True);
			Assert.That(provider.LastSubscribedIds, Is.Not.Null);
			Assert.That(provider.LastSubscribedIds, Is.Empty);
		});
	}

	[Test]
	public async Task A_subscribe_result_is_filtered_to_the_requested_set()
	{
		var provider = new FakeCatalogProvider
		{
			SubscribeResult = [VariableValue.Of("a", "on"), VariableValue.Of("b", "off")]
		};
		var handler = Handler(provider);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Subscribe,
				new VariableSubscribeArguments { Ids = ["a"] }),
			CancellationToken.None);

		var subscribed = Deserialize<VariableSubscribeResult>(result);

		Assert.Multiple(() =>
		{
			Assert.That(subscribed.Values, Has.Count.EqualTo(1));
			Assert.That(subscribed.Values[0].Id, Is.EqualTo("a"));
		});
	}

	/// <summary>
	/// Proves CatalogName/SupportsPush/SupportsSearch/OnAttachedAsync really are defaulted - the
	/// additive-compatibility promise a plugin compiled against an older SDK depends on - and that an
	/// unnamed catalog falls back to the plugin's own name rather than showing up blank.
	/// </summary>
	[Test]
	public async Task A_minimal_catalog_implementing_only_the_required_members_works_end_to_end()
	{
		var handler = Handler(new MinimalCatalogProvider());

		var describeResult = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Describe),
			CancellationToken.None);
		var describe = Deserialize<VariableCatalogPayload>(describeResult);

		var discoverResult = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Discover),
			CancellationToken.None);
		var page = Deserialize<VariableCatalogPageResult>(discoverResult);

		Assert.Multiple(() =>
		{
			Assert.That(describe.SupportsCatalog, Is.True);
			Assert.That(describe.CatalogName, Is.EqualTo(TestMetadata.Default.Name));
			Assert.That(describe.SupportsPush, Is.False);
			Assert.That(describe.SupportsSearch, Is.False);
			Assert.That(page.Items, Has.Count.EqualTo(1));
			Assert.That(page.Items[0].Id, Is.EqualTo("only"));
		});
	}

	/// <summary>A provider that never opted into the catalog must not be browsable at all, however many
	/// eager variables it declares.</summary>
	[Test]
	public async Task A_provider_without_a_catalog_describes_none_and_discovers_nothing()
	{
		var handler = Handler(new EagerOnlyProvider());

		var describeResult = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Describe),
			CancellationToken.None);
		var discoverResult = await handler.InvokeAsync(Invocation(CapabilityOperations.Variables.Discover),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Deserialize<VariableCatalogPayload>(describeResult).SupportsCatalog, Is.False);
			Assert.That(Deserialize<VariableCatalogPayload>(describeResult).CatalogName, Is.Null);
			Assert.That(Deserialize<VariableCatalogPageResult>(discoverResult).Items, Is.Empty);
		});
	}

	[Test]
	public async Task Malformed_arguments_degrade_rather_than_throwing()
	{
		var handler = Handler(new FakeCatalogProvider());

		// VariableSubscribeArguments.Ids is required; this payload omits it, so deserialization throws
		// JsonException inside the handler - which must be caught, not left to escape.
		var malformed = Invocation(CapabilityOperations.Variables.Subscribe, new { notIds = "nope" });

		CapabilityInvocationResult? result = null;
		Assert.DoesNotThrowAsync(async () => result = await handler.InvokeAsync(malformed, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(result!.IsFailure, Is.True);
			Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
		});
	}

	private static VariableDefinition Item(string id)
		=> new() { Id = id, Name = id, Type = VariableType.Text, Materialization = VariableMaterialization.OnDemand };

	private static T Deserialize<T>(CapabilityInvocationResult result)
	{
		Assert.That(result.IsFailure, Is.False, result.Error?.Message);
		return result.Data!.Value.Deserialize<T>(PluginProtocolJson.Options)!;
	}

	private abstract class BaseIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class PlainIntegration : BaseIntegration;

	private sealed class FakeCatalogProvider : BaseIntegration, IVariableProvider
	{
		public VariableCatalogPage Page { get; set; } = VariableCatalogPage.Empty;

		public IReadOnlyList<VariableValue> SubscribeResult { get; set; } = [];

		public VariableCatalogQuery? LastQuery { get; private set; }

		public IReadOnlyCollection<string>? LastSubscribedIds { get; private set; }

		public bool SubscribeAsyncWasCalled { get; private set; }

		public IReadOnlyList<VariableDefinition> Variables => [];

		public bool SupportsCatalog => true;

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);

		public ValueTask<VariableCatalogPage> DiscoverAsync(
			VariableCatalogQuery query,
			CancellationToken cancellationToken = default)
		{
			LastQuery = query;
			return ValueTask.FromResult(Page);
		}

		public ValueTask<VariableDefinition?> ResolveAsync(
			string localId,
			CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<VariableDefinition?>(null);

		public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
			IReadOnlyCollection<string> localIds,
			CancellationToken cancellationToken = default)
		{
			SubscribeAsyncWasCalled = true;
			LastSubscribedIds = localIds;
			return ValueTask.FromResult(SubscribeResult);
		}
	}

	/// <summary>Implements only the two members without a default in <see cref="IVariableProvider" />,
	/// plus the two the catalog half is gated on.</summary>
	private sealed class MinimalCatalogProvider : BaseIntegration, IVariableProvider
	{
		public IReadOnlyList<VariableDefinition> Variables => [];

		public bool SupportsCatalog => true;

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);

		public ValueTask<VariableCatalogPage> DiscoverAsync(
			VariableCatalogQuery query,
			CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(new VariableCatalogPage
			{
				Items = [VariableDefinition.OnDemand("only", VariableType.Text)]
			});
	}

	/// <summary>Implements only the two members without a default, so the catalog half stays off.</summary>
	private sealed class EagerOnlyProvider : BaseIntegration, IVariableProvider
	{
		public IReadOnlyList<VariableDefinition> Variables { get; } =
			[VariableDefinition.Eager("cpu_temp", VariableType.Numeric)];

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);
	}
}
