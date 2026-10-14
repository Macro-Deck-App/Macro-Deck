using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Sdk.Variables;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableBindingAppenderTests
{
	private const string HomeAssistant = "app.macro-deck.homeassistant";

	private static readonly VariableBindingRequest _kitchen
		= new("entity/light.kitchen/state", "ha_light_kitchen", VariableType.Text) { MigratedFrom = "v1" };

	private static readonly VariableBindingRequest _desk
		= new("entity/switch.desk/state", "ha_switch_desk", VariableType.Boolean);

	[Test]
	public async Task New_bindings_are_stored_for_the_calling_integration_next_to_the_existing_ones()
	{
		var store = new MemoryBindingStore(Binding("app.example.other", "entity/light.kitchen/state", "other"));

		var outcomes = await new VariableBindingAppender(store).AppendAsync(HomeAssistant, [_kitchen, _desk]);

		Assert.Multiple(() =>
		{
			Assert.That(outcomes, Is.EqualTo(new[] { VariableBindingOutcome.Bound, VariableBindingOutcome.Bound }));
			Assert.That(
				store.Bindings.Select(b => (b.IntegrationId, b.LocalResourceId, b.Name, b.Type, b.MigratedFrom)),
				Is.EqualTo(new[]
				{
					("app.example.other", "entity/light.kitchen/state", "other", DomainVariableType.Text,
						(string?)null),
					(HomeAssistant, "entity/light.kitchen/state", "ha_light_kitchen", DomainVariableType.Text, "v1"),
					(HomeAssistant, "entity/switch.desk/state", "ha_switch_desk", DomainVariableType.Boolean, null)
				}));
		});
	}

	[Test]
	public async Task A_resource_already_bound_keeps_its_binding()
	{
		var store = new MemoryBindingStore(Binding(HomeAssistant, "entity/light.kitchen/state", "renamed_by_user"));

		var outcomes = await new VariableBindingAppender(store).AppendAsync(HomeAssistant, [_kitchen, _desk, _desk]);

		Assert.Multiple(() =>
		{
			Assert.That(outcomes,
				Is.EqualTo(new[]
				{
					VariableBindingOutcome.AlreadyBound, VariableBindingOutcome.Bound,
					VariableBindingOutcome.AlreadyBound
				}));
			Assert.That(store.Bindings.Select(b => b.Name), Is.EqualTo(new[] { "renamed_by_user", "ha_switch_desk" }));
		});
	}

	[Test]
	public void An_invalid_name_records_nothing()
	{
		var store = new MemoryBindingStore();

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<ArgumentException>(() => new VariableBindingAppender(store)
				.AppendAsync(HomeAssistant, [_desk, _kitchen with { Name = "Kitchen Light" }]));
			Assert.That(store.Bindings, Is.Empty);
		});
	}

	[Test]
	public void An_unreadable_store_is_never_overwritten()
	{
		var store = new MemoryBindingStore { Readable = false };

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<InvalidOperationException>(() =>
				new VariableBindingAppender(store).AppendAsync(HomeAssistant, [_kitchen]));
			Assert.That(store.Saves, Is.Zero);
		});
	}

	[Test]
	public void A_failed_save_is_reported()
	{
		var store = new MemoryBindingStore { Writable = false };

		Assert.ThrowsAsync<InvalidOperationException>(() =>
			new VariableBindingAppender(store).AppendAsync(HomeAssistant, [_kitchen]));
	}

	private static VariableBinding Binding(string integrationId, string resourceId, string name)
	{
		return new VariableBinding
		{
			Id = Guid.NewGuid(),
			IntegrationId = integrationId,
			LocalResourceId = resourceId,
			Name = name,
			Type = DomainVariableType.Text
		};
	}

	private sealed class MemoryBindingStore(params VariableBinding[] initial) : IVariableBindingStore
	{
		public List<VariableBinding> Bindings { get; private set; } = [.. initial];

		public bool Readable { get; init; } = true;

		public bool Writable { get; init; } = true;

		public int Saves { get; private set; }

		public IReadOnlyList<VariableBinding> Load()
		{
			return Bindings;
		}

		public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
		{
			bindings = Readable ? [.. Bindings] : [];
			return Readable;
		}

		public bool Save(IEnumerable<VariableBinding> bindings)
		{
			if (!Writable)
			{
				return false;
			}

			Saves++;
			Bindings = [.. bindings];
			return true;
		}
	}
}
