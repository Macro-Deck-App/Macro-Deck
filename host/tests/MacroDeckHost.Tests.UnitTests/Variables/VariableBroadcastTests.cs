using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Tests.UnitTests.Ui;
using Mediator;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableBroadcastTests
{
	// --- Publish(): gating, chunking, deletions, error isolation - all deterministic, no timing ---

	[Test]
	public async Task Gating_a_narrowed_connection_only_receives_its_declared_names()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var b = NewVariable("b", "1");
		registry.Upsert(a);
		registry.Upsert(b);

		var interest = new VariableInterestTracker();
		interest.Set("c1", ["a"]);

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id, b.Id], CancellationToken.None);

		var toC1 = transport.To("c1");
		Assert.Multiple(() =>
		{
			Assert.That(toC1, Has.Count.EqualTo(1));
			Assert.That(toC1[0].Upserted.Select(v => v.Name), Is.EqualTo(new List<string> { "a" }));
			Assert.That(toC1.Any(m => m.Upserted.Any(v => v.Name == "b")), Is.False);
		});
	}

	[Test]
	public async Task Publish_carries_the_dynamic_resource_id_for_a_bound_variable_and_null_for_an_ordinary_one()
	{
		var registry = new VariableRegistry();
		var bound = NewVariable("kitchen_light", "on");
		bound.OwnerIntegrationId = "home-assistant";
		bound.DefinitionId = "light.kitchen";
		var ordinary = NewVariable("greeting", "hi");
		registry.Upsert(bound);
		registry.Upsert(ordinary);

		var bindingStore = new InMemoryVariableBindingStore();
		bindingStore.Save([
			new VariableBinding
			{
				Id = Guid.CreateVersion7(),
				IntegrationId = "home-assistant",
				LocalResourceId = "light.kitchen",
				Name = "kitchen_light",
				Type = VariableType.Text,
				CreatedAt = DateTime.UtcNow
			}
		]);
		var bindings = new VariableBindingLookup(registry, bindingStore);

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			new VariableInterestTracker(),
			transport,
			bindings,
			Serilog.Log.Logger);

		await broadcaster.Publish([bound.Id, ordinary.Id], CancellationToken.None);

		var upserted = transport.Group().Single().Upserted;
		var boundDto = upserted.Single(v => v.Name == "kitchen_light");
		var ordinaryDto = upserted.Single(v => v.Name == "greeting");

		Assert.Multiple(() =>
		{
			Assert.That(boundDto.DynamicResourceId, Is.EqualTo("light.kitchen"));
			Assert.That(ordinaryDto.DynamicResourceId, Is.Null);
		});
	}

	[Test]
	public async Task The_broadcast_path_carries_the_same_display_metadata_as_a_read()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("obs_mac_current_scene", "Main");
		a.Presentation = new VariablePresentation(LocalizedText.FromLiteral("Current scene"),
			"mac",
			LocalizedText.FromLiteral("Mac"));
		registry.Upsert(a);

		var interest = new VariableInterestTracker();
		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id], CancellationToken.None);

		var broadcastDto = transport.Group().Single().Upserted.Single();
		var readDto = VariableDtoMapper.ToDto(a, registry.IsAvailable(a.Id), null);

		Assert.Multiple(() =>
		{
			Assert.That(broadcastDto.DisplayName, Is.EqualTo(readDto.DisplayName));
			Assert.That(broadcastDto.ConfigurationKey, Is.EqualTo(readDto.ConfigurationKey));
			Assert.That(broadcastDto.ConfigurationName, Is.EqualTo(readDto.ConfigurationName));
			Assert.That(broadcastDto.DisplayName.Literal, Is.EqualTo("Current scene"));
			Assert.That(broadcastDto.ConfigurationKey, Is.EqualTo("mac"));
		});
	}

	[Test]
	public async Task A_connection_that_never_declared_interest_sees_everything_via_the_group()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var b = NewVariable("b", "1");
		registry.Upsert(a);
		registry.Upsert(b);

		var interest = new VariableInterestTracker();
		interest.Set("c1", ["a"]);
		// c2 never calls WatchVariables - it stays on the all-variables group.

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id, b.Id], CancellationToken.None);

		var group = transport.Group();
		var toC1 = transport.To("c1");
		Assert.Multiple(() =>
		{
			Assert.That(group, Has.Count.EqualTo(1));
			Assert.That(group[0].Upserted.Select(v => v.Name), Is.EquivalentTo(new List<string> { "a", "b" }));
			Assert.That(toC1, Has.Count.EqualTo(1));
			Assert.That(toC1[0].Upserted.Select(v => v.Name), Is.EqualTo(new List<string> { "a" }));
		});
	}

	[Test]
	public async Task An_empty_declaration_receives_nothing()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var b = NewVariable("b", "1");
		var c = NewVariable("c", "1");
		registry.Upsert(a);
		registry.Upsert(b);
		registry.Upsert(c);

		var interest = new VariableInterestTracker();
		interest.Set("c1", []);

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id, b.Id, c.Id], CancellationToken.None);

		var group = transport.Group();
		Assert.Multiple(() =>
		{
			Assert.That(transport.To("c1"), Is.Empty);
			Assert.That(group, Has.Count.EqualTo(1));
			Assert.That(group[0].Upserted.Select(v => v.Name), Is.EquivalentTo(new List<string> { "a", "b", "c" }));
		});
	}

	[Test]
	public void Snapshot_returns_current_values_and_sends_nothing()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var c = NewVariable("c", "7", classification: VariableClassification.Integration);
		registry.Upsert(a);
		registry.Upsert(c);
		registry.SetAvailable(c.Id, false);

		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		var snapshot = broadcaster.Snapshot(["a", "c", "missing"]);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Upserted, Has.Count.EqualTo(2));
			Assert.That(snapshot.Upserted.Single(v => v.Name == "a").Value, Is.EqualTo("1"));
			var cDto = snapshot.Upserted.Single(v => v.Name == "c");
			Assert.That(cDto.Available, Is.False);
			Assert.That(cDto.Value, Is.EqualTo("7"));
			Assert.That(snapshot.Upserted.Any(v => v.Name == "missing"), Is.False);
			Assert.That(snapshot.DeletedIds, Is.Empty);
			Assert.That(transport.Sent, Is.Empty);
		});
	}

	[Test]
	public async Task Set_fully_replaces_the_previous_declaration()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var b = NewVariable("b", "1");
		var c = NewVariable("c", "1");
		registry.Upsert(a);
		registry.Upsert(b);
		registry.Upsert(c);

		var interest = new VariableInterestTracker();
		interest.Set("c1", ["a", "b"]);
		interest.Set("c1", ["b", "c"]);

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id, b.Id, c.Id], CancellationToken.None);

		var names = transport.To("c1").SelectMany(m => m.Upserted).Select(v => v.Name).ToList();
		Assert.That(names, Is.EquivalentTo(new List<string> { "b", "c" }));

		interest.Set("c1", []);
		await broadcaster.Publish([a.Id, b.Id, c.Id], CancellationToken.None);

		Assert.That(transport.To("c1"), Has.Count.EqualTo(1), "no further message once narrowed to nothing");
	}

	[Test]
	public async Task Deletions_are_gated_by_the_deleted_variables_last_known_name()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var b = NewVariable("b", "1");
		registry.Upsert(a);
		registry.Upsert(b);

		var interest = new VariableInterestTracker();
		interest.Set("c1", ["a"]);

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		// WatchVariables(["a"]) always calls Snapshot(["a"]) for the initial payload, which is what lets
		// the broadcaster still attribute "a"'s name once it is gone from the registry.
		broadcaster.Snapshot(["a"]);

		registry.Remove(a.Id);
		registry.Remove(b.Id);
		await broadcaster.Publish([a.Id, b.Id], CancellationToken.None);

		var toC1 = transport.To("c1");
		Assert.Multiple(() =>
		{
			Assert.That(toC1, Has.Count.EqualTo(1));
			Assert.That(toC1[0].DeletedIds, Is.EqualTo(new[] { a.Id.ToString() }));
			Assert.That(toC1[0].Upserted, Is.Empty);

			var group = transport.Group().SelectMany(m => m.DeletedIds).ToList();
			Assert.That(group, Is.EquivalentTo(new[] { a.Id.ToString(), b.Id.ToString() }));
		});
	}

	[Test]
	public async Task No_resurrection_within_one_coalesced_window()
	{
		var registry = new VariableRegistry();
		var z = NewVariable("z", "1");
		registry.Upsert(z);
		z.Value = "2";
		registry.Upsert(z);
		registry.Remove(z.Id);

		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		await broadcaster.Publish([z.Id], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(transport.Sent.SelectMany(m => m.Event.Upserted).Any(v => v.Id == z.Id.ToString()), Is.False);
			Assert.That(transport.Sent.SelectMany(m => m.Event.DeletedIds), Contains.Item(z.Id.ToString()));
		});
	}

	[Test]
	public async Task Deleting_and_recreating_the_same_name_in_one_window_keeps_the_ids_distinct()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "old");
		registry.Upsert(a);
		registry.Remove(a.Id);
		var a2 = NewVariable("a", "new");
		registry.Upsert(a2);

		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		await broadcaster.Publish([a.Id, a2.Id], CancellationToken.None);

		var deleted = transport.Sent.SelectMany(m => m.Event.DeletedIds).ToList();
		var upserted = transport.Sent.SelectMany(m => m.Event.Upserted).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(deleted, Contains.Item(a.Id.ToString()));
			Assert.That(upserted, Has.Count.EqualTo(1));
			Assert.That(upserted[0].Id, Is.EqualTo(a2.Id.ToString()));
			Assert.That(upserted[0].Value, Is.EqualTo("new"));
			Assert.That(a.Id, Is.Not.EqualTo(a2.Id));
		});
	}

	[Test]
	public async Task A_failing_recipient_does_not_block_others_or_later_batches()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		registry.Upsert(a);

		var interest = new VariableInterestTracker();
		interest.Set("c1", ["a"]);
		interest.Set("c2", ["a"]);
		// c3 never declares.

		var transport = new RecordingTransport { FailConnection = id => id == "c2" };
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id], CancellationToken.None);

		a.Value = "2";
		registry.Upsert(a);
		await broadcaster.Publish([a.Id], CancellationToken.None);

		var toC1 = transport.To("c1");
		var toGroup = transport.Group();
		Assert.Multiple(() =>
		{
			Assert.That(toC1, Has.Count.EqualTo(2));
			Assert.That(toGroup, Has.Count.EqualTo(2));
			Assert.That(toC1[1].Upserted.Single().Value, Is.EqualTo("2"));
			Assert.That(toGroup[1].Upserted.Single().Value, Is.EqualTo("2"));
		});
	}

	[Test]
	public async Task Exactly_200_changes_produce_a_single_message()
	{
		var (registry, ids) = ManyVariables(200);
		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		await broadcaster.Publish(ids, CancellationToken.None);

		Assert.That(transport.Sent, Has.Count.EqualTo(1));
		Assert.That(transport.Sent[0].Event.Upserted, Has.Count.EqualTo(200));
	}

	[TestCase(201, 2)]
	[TestCase(500, 3)]
	public async Task Larger_batches_split_into_chunks_that_cover_every_id_once(int count, int expectedChunks)
	{
		var (registry, ids) = ManyVariables(count);
		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		await broadcaster.Publish(ids, CancellationToken.None);

		Assert.That(transport.Sent, Has.Count.EqualTo(expectedChunks));
		Assert.That(transport.Sent.Select(m => m.Event.Upserted.Count + m.Event.DeletedIds.Count),
			Has.All.LessThanOrEqualTo(200).And.All.GreaterThan(0));

		var union = transport.Sent.SelectMany(m => m.Event.Upserted).Select(v => v.Id).Distinct().ToList();
		Assert.That(union, Has.Count.EqualTo(count));
	}

	[Test]
	public async Task Four_hundred_fifty_deletions_chunk_without_gaps_or_duplicates()
	{
		var registry = new VariableRegistry();
		var ids = Enumerable.Range(0, 450).Select(_ => Guid.NewGuid()).ToList();
		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		await broadcaster.Publish(ids, CancellationToken.None);

		Assert.That(transport.Sent.Select(m => m.Event.Upserted.Count + m.Event.DeletedIds.Count),
			Has.All.LessThanOrEqualTo(200));
		var union = transport.Sent.SelectMany(m => m.Event.DeletedIds).Distinct().ToList();
		Assert.That(union, Has.Count.EqualTo(450));
	}

	[Test]
	public async Task Chunking_composes_with_gating()
	{
		var registry = new VariableRegistry();
		var declaredNames = new List<string>();
		var declaredIds = new List<Guid>();
		for (var i = 0; i < 300; i++)
		{
			var v = NewVariable($"declared{i}", "x");
			registry.Upsert(v);
			declaredNames.Add(v.Name);
			declaredIds.Add(v.Id);
		}

		var allIds = new List<Guid>(declaredIds);
		for (var i = 0; i < 200; i++)
		{
			var v = NewVariable($"other{i}", "x");
			registry.Upsert(v);
			allIds.Add(v.Id);
		}

		var interest = new VariableInterestTracker();
		interest.Set("c1", declaredNames);

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish(allIds, CancellationToken.None);

		var toC1 = transport.To("c1");
		Assert.Multiple(() =>
		{
			Assert.That(toC1, Has.Count.EqualTo(2));
			Assert.That(toC1.Select(m => m.Upserted.Count), Has.All.LessThanOrEqualTo(200));

			var receivedIds = toC1.SelectMany(m => m.Upserted).Select(v => v.Id).ToHashSet();
			Assert.That(receivedIds, Is.EquivalentTo(declaredIds.Select(id => id.ToString())));
		});
	}

	[Test]
	public async Task Interest_dies_with_the_connection()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		var b = NewVariable("b", "1");
		registry.Upsert(a);
		registry.Upsert(b);

		var interest = new VariableInterestTracker();
		interest.Set("x", ["a"]);
		interest.RemoveConnection("x");
		// y never declares.

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		await broadcaster.Publish([a.Id, b.Id], CancellationToken.None);

		var group = transport.Group();
		Assert.Multiple(() =>
		{
			Assert.That(transport.To("x"), Is.Empty);
			Assert.That(group, Has.Count.EqualTo(1));
			Assert.That(group[0].Upserted.Select(v => v.Name), Is.EquivalentTo(new List<string> { "a", "b" }));
		});
	}

	[Test]
	public async Task Availability_flips_ride_the_batch()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1", classification: VariableClassification.Integration);
		registry.Upsert(a);

		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);

		registry.SetAvailable(a.Id, false);
		await broadcaster.Publish([a.Id], CancellationToken.None);

		Assert.That(transport.Sent, Has.Count.EqualTo(1));
		Assert.That(transport.Sent[0].Event.Upserted.Single().Available, Is.False);
	}

	// --- The background service: coalescing over a real window, idle silence, and non-blocking enqueue ---

	[Test]
	public async Task Burst_coalesces_to_the_last_value()
	{
		var registry = new VariableRegistry();
		var temp = NewVariable("temp", "0");
		registry.Upsert(temp);

		var channel = new VariableBroadcastChannel();
		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);
		var service = NewService(channel, broadcaster);

		await service.StartAsync(CancellationToken.None);
		try
		{
			for (var i = 1; i <= 50; i++)
			{
				temp.Value = i.ToString(CultureInfo.InvariantCulture);
				registry.Upsert(temp);
				channel.Enqueue(temp.Id);
			}

			await WaitUntil(() => transport.Sent.Count >= 1);
			await Task.Delay(TestWindow * 4);
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}

		Assert.That(transport.Sent, Has.Count.EqualTo(1));
		var evt = transport.Sent[0].Event;
		Assert.Multiple(() =>
		{
			Assert.That(evt.Upserted, Has.Count.EqualTo(1));
			Assert.That(evt.Upserted[0].Value, Is.EqualTo("50"));
			Assert.That(evt.DeletedIds, Is.Empty);
		});
	}

	[Test]
	public async Task Per_variable_coalescing_keeps_only_the_last_value_per_id()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "a0");
		var b = NewVariable("b", "b0");
		registry.Upsert(a);
		registry.Upsert(b);

		var channel = new VariableBroadcastChannel();
		var transport = new RecordingTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);
		var service = NewService(channel, broadcaster);

		await service.StartAsync(CancellationToken.None);
		try
		{
			Change(registry, channel, a, "a1");
			Change(registry, channel, b, "b1");
			Change(registry, channel, a, "a2");
			Change(registry, channel, b, "b2");

			await WaitUntil(() => transport.Sent.Count >= 1);
			await Task.Delay(TestWindow * 4);
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}

		Assert.That(transport.Sent, Has.Count.EqualTo(1));
		var evt = transport.Sent[0].Event;
		Assert.Multiple(() =>
		{
			Assert.That(evt.Upserted.Select(v => v.Id), Is.EquivalentTo(new[] { a.Id.ToString(), b.Id.ToString() }));
			Assert.That(evt.Upserted.Single(v => v.Id == a.Id.ToString()).Value, Is.EqualTo("a2"));
			Assert.That(evt.Upserted.Single(v => v.Id == b.Id.ToString()).Value, Is.EqualTo("b2"));
		});
	}

	[Test]
	public async Task Idle_host_produces_no_messages_until_something_changes()
	{
		var registry = new VariableRegistry();
		var a = NewVariable("a", "1");
		registry.Upsert(a);

		var interest = new VariableInterestTracker();
		interest.Set("c1", ["a"]);
		// c2 never declares.

		var channel = new VariableBroadcastChannel();
		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);
		var service = NewService(channel, broadcaster);

		await service.StartAsync(CancellationToken.None);
		try
		{
			await Task.Delay(TestWindow * 4);
			Assert.That(transport.Sent, Is.Empty, "idle host must not produce traffic");

			Change(registry, channel, a, "2");
			await WaitUntil(() => transport.Group().Count >= 1);

			await Task.Delay(TestWindow * 4);
			Assert.That(transport.Group(), Has.Count.EqualTo(1), "still exactly one flush after further idle time");
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}

		Assert.That(transport.Sent.Any(m => m.Event.Upserted.Count == 0 && m.Event.DeletedIds.Count == 0), Is.False);
	}

	[Test]
	public async Task Publish_never_blocks_the_caller_while_a_flush_is_stuck_in_flight()
	{
		var registry = new VariableRegistry();
		var variables = Enumerable.Range(0, 10).Select(i => NewVariable($"v{i}", "0")).ToList();
		foreach (var v in variables)
		{
			registry.Upsert(v);
		}

		var channel = new VariableBroadcastChannel();
		var transport = new GatedTransport();
		var broadcaster
			= new VariableBroadcaster(registry,
				new VariableInterestTracker(),
				transport,
				new VariableBindingLookup(registry,
					new InMemoryVariableBindingStore()),
				Serilog.Log.Logger);
		var service = NewService(channel, broadcaster);

		await service.StartAsync(CancellationToken.None);
		try
		{
			channel.Enqueue(variables[0].Id);
			await WaitUntil(() => transport.InFlight);

			var expectedFinal = new Dictionary<Guid, string>();
			var writer = Task.Run(() =>
			{
				for (var i = 0; i < 1000; i++)
				{
					var target = variables[i % 10];
					var value = i.ToString(CultureInfo.InvariantCulture);
					target.Value = value;
					registry.Upsert(target);
					expectedFinal[target.Id] = value;
					channel.Enqueue(target.Id);
				}
			});

			var completed = await Task.WhenAny(writer, Task.Delay(TimeSpan.FromSeconds(5)));
			Assert.That(completed, Is.EqualTo(writer), "enqueueing must never wait on a stuck flush");
			await writer;

			transport.Release();
			await WaitUntil(() => transport.Sent.Count >= 2);

			var next = transport.Sent[1];
			Assert.That(next.Upserted, Has.Count.LessThanOrEqualTo(10));
			foreach (var v in next.Upserted)
			{
				Assert.That(v.Value, Is.EqualTo(expectedFinal[Guid.Parse(v.Id)]));
			}
		}
		finally
		{
			transport.Release();
			await service.StopAsync(CancellationToken.None);
		}
	}

	// --- Volatile attributes: bounds travel with a reading, so a range change is a broadcast of its own ---

	/// <summary>
	/// A media player's seek range is its current track's length, so a range can move while the value does
	/// not. If only a value change reached clients, a slider bound to that variable would keep scaling the
	/// old track's length until the position happened to change - which it does not while paused.
	/// </summary>
	[Test]
	public async Task A_bounds_only_reading_reaches_a_watching_connection_carrying_the_new_range()
	{
		var (service, registry, broadcaster, transport, _) = BoundsPipeline();
		var id = await SeekVariable(service, max: 180);
		transport.Sent.Clear();

		var result = await service.ReportIntegrationVariableValue("player",
			id,
			null,
			new VariableBounds(0, 240, 1));

		await broadcaster.Publish([id], CancellationToken.None);
		var dto = transport.Group().Single().Upserted.Single();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(dto.Max, Is.EqualTo(240));
			Assert.That(dto.Min, Is.EqualTo(0));
			Assert.That(dto.Step, Is.EqualTo(1));
			Assert.That(registry.GetById(id)!.Max, Is.EqualTo(240));
		});
	}

	[Test]
	public async Task A_repeated_identical_reading_changes_nothing_and_publishes_nothing()
	{
		var pipeline = BoundsPipeline();
		var id = await SeekVariable(pipeline.Service, max: 180);
		await pipeline.Service.ReportIntegrationVariableValue("player", id, 30d, new VariableBounds(0, 180, 1));
		pipeline.Mediator.Published.Clear();

		await pipeline.Service.ReportIntegrationVariableValue("player", id, 30d, new VariableBounds(0, 180, 1));

		Assert.That(pipeline.Mediator.Published, Is.Empty);
	}

	/// <summary>
	/// Absent bounds mean "not supplied", not "no range". A provider that reports a value without bounds -
	/// and every provider that has no range to report does - must not collapse a slider to zero width.
	/// </summary>
	[Test]
	public async Task A_reading_that_supplies_no_bounds_leaves_the_range_alone()
	{
		var (service, registry, _, _, _) = BoundsPipeline();
		var id = await SeekVariable(service, max: 180);

		await service.ReportIntegrationVariableValue("player", id, 30d);

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetById(id)!.Max, Is.EqualTo(180));
			Assert.That(registry.GetById(id)!.Value, Is.EqualTo("30"));
		});
	}

	[Test]
	public async Task Going_unavailable_leaves_the_range_alone()
	{
		var (service, registry, _, _, _) = BoundsPipeline();
		var id = await SeekVariable(service, max: 180);

		await service.SetIntegrationVariableAvailability("player", id, false);

		Assert.Multiple(() =>
		{
			Assert.That(registry.IsAvailable(id), Is.False);
			Assert.That(registry.GetById(id)!.Max, Is.EqualTo(180));
		});
	}

	private static (VariableService Service, VariableRegistry Registry, VariableBroadcaster Broadcaster,
		RecordingTransport Transport, RecordingMediator Mediator) BoundsPipeline()
	{
		var registry = new VariableRegistry();
		var mediator = new RecordingMediator();
		var service = TestVariableServices.Create(registry, new NullUserVariableStore(), mediator);
		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			new VariableInterestTracker(),
			transport,
			new VariableBindingLookup(registry, new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);

		return (service, registry, broadcaster, transport, mediator);
	}

	private static async Task<Guid> SeekVariable(VariableService service, double max)
	{
		var created = await service.CreateIntegrationVariable("player",
			"position",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			0,
			0,
			"position");

		await service.ReportIntegrationVariableValue("player", created.Data!.Id, 0d, new VariableBounds(0, max, 1));
		return created.Data.Id;
	}

	// --- Wiring: the broadcast channel sits alongside label rendering, state bindings and triggers ---

	[Test]
	public async Task Trigger_fires_even_with_no_client_connected()
	{
		var registry = new VariableRegistry();
		var channel = new VariableBroadcastChannel();
		var bus = new RecordingEventBus();

		var mediator = new FanOutMediator();
		mediator.Register(new VariableUpdatedBroadcastHandler(channel, new VariableChangeNotifier()));
		mediator.Register(new VariableValueChangedEventTriggerHandler(bus));

		var service = TestVariableServices.Create(registry, new NullUserVariableStore(), mediator);
		var created = await service.CreateUserVariable("a", VariableScope.Global, null, VariableType.Numeric, 0, null);
		Assert.That(created.Success, Is.True);
		bus.Published.Clear();

		await service.SetValue(created.Data!.Id, 42);

		// Asserted immediately, with no Task.Delay anywhere above: the trigger must not wait on the UI flush.
		Assert.That(bus.Published, Has.Count.EqualTo(1));
		var occurrence = bus.Published[0];
		Assert.Multiple(() =>
		{
			Assert.That(occurrence.EventId, Is.EqualTo(EventIds.Qualify(EventIds.VariableChanged)));
			Assert.That(occurrence.Parameters["variable"], Is.EqualTo("a"));
			Assert.That(occurrence.Parameters["value"], Is.EqualTo(42));
		});
	}

	[Test]
	public async Task Widget_state_bindings_are_unaffected_by_broadcast_gating()
	{
		var registry = new VariableRegistry();
		var channel = new VariableBroadcastChannel();
		var folderCache = new StubFolderCache();
		var widget = StubFolderCache.Widget(
			"{\"stateBinding\":{\"kind\":\"compare\",\"left\":{\"$var\":\"a\"},\"right\":1}}");
		folderCache.AddFolder(widget);
		var index = new WidgetVariableIndex(folderCache);
		index.ReindexWidget(widget.Id, WidgetTypeIds.ActionButton, widget.Data);
		var evalQueue = new WidgetStateEvalChannel();

		var mediator = new FanOutMediator();
		mediator.Register(new VariableUpdatedBroadcastHandler(channel, new VariableChangeNotifier()));
		mediator.Register(new VariableValueChangedStateMappingHandler(index, evalQueue));

		var service = TestVariableServices.Create(registry, new NullUserVariableStore(), mediator);
		var created = await service.CreateUserVariable("a", VariableScope.Global, null, VariableType.Text, "1", null);

		await service.SetValue(created.Data!.Id, "2");

		Assert.That(Drain(evalQueue), Is.EqualTo(new[] { widget.Id }));
	}

	[Test]
	public async Task Label_rendering_survives_a_narrowed_client()
	{
		var registry = new VariableRegistry();
		var channel = new VariableBroadcastChannel();
		var folderCache = new StubFolderCache();
		var widget = StubFolderCache.Widget("{\"label\":\"{{ vars.a }}\"}");
		folderCache.AddFolder(widget);
		var index = new WidgetVariableIndex(folderCache);
		index.ReindexWidget(widget.Id, WidgetTypeIds.ActionButton, widget.Data);

		var labelQueue = new LabelRenderChannel();
		var labelSubscriptions = new LabelSubscriptionTracker();
		labelSubscriptions.Add("c1", widget.Id.ToString(), "off");

		var interest = new VariableInterestTracker();
		interest.Set("c1", []);

		var mediator = new FanOutMediator();
		mediator.Register(new VariableUpdatedBroadcastHandler(channel, new VariableChangeNotifier()));
		mediator.Register(new VariableValueChangedNotificationHandler(index,
			labelQueue,
			labelSubscriptions,
			new RecordingRenderSignals()));

		var service = TestVariableServices.Create(registry, new NullUserVariableStore(), mediator);
		var created = await service.CreateUserVariable("a", VariableScope.Global, null, VariableType.Text, "1", null);

		await service.SetValue(created.Data!.Id, "2");

		Assert.That(Drain(labelQueue), Is.EqualTo(new[] { widget.Id }));

		var ids = new HashSet<Guid>();
		while (channel.Reader.TryRead(out var id))
		{
			ids.Add(id);
		}

		var transport = new RecordingTransport();
		var broadcaster = new VariableBroadcaster(registry,
			interest,
			transport,
			new VariableBindingLookup(registry,
				new InMemoryVariableBindingStore()),
			Serilog.Log.Logger);
		await broadcaster.Publish(ids, CancellationToken.None);

		Assert.That(transport.To("c1"), Is.Empty);
	}

	[Test]
	public async Task GetVariables_stays_full_regardless_of_declared_interest()
	{
		var registry = new VariableRegistry();
		for (var i = 0; i < 1000; i++)
		{
			registry.Upsert(NewVariable($"v{i}", "x"));
		}

		var interest = new VariableInterestTracker();
		interest.Set("c1", []);

		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var handler = new GetVariablesRequestMessageHandler(
			TestVariableServices.Create(registry, new NullUserVariableStore(), new FanOutMediator()),
			registry,
			new FakeVariableBindingService(),
			readiness);

		var response = await handler.Handle(new GetVariablesRequest(), CancellationToken.None);

		Assert.That(response.Variables, Has.Count.EqualTo(1000));
	}

	private const int TestWindow = 30;

	private static VariableBroadcastBackgroundService NewService(
		VariableBroadcastChannel channel,
		VariableBroadcaster broadcaster)
		=> new(new StartedHostLifetime(),
			channel,
			broadcaster,
			Serilog.Core.Logger.None,
			TimeSpan.FromMilliseconds(TestWindow));

	private static void Change(VariableRegistry registry,
		VariableBroadcastChannel channel,
		VariableEntity v,
		string value)
	{
		v.Value = value;
		registry.Upsert(v);
		channel.Enqueue(v.Id);
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("Condition was not met within the timeout.");
			}

			await Task.Delay(10);
		}
	}

	private static List<Guid> Drain(LabelRenderChannel queue)
	{
		var ids = new List<Guid>();
		while (queue.Reader.TryRead(out var id))
		{
			ids.Add(id);
		}

		return ids;
	}

	private static List<Guid> Drain(WidgetStateEvalChannel queue)
	{
		var ids = new List<Guid>();
		while (queue.Reader.TryRead(out var id))
		{
			ids.Add(id);
		}

		return ids;
	}

	private static VariableEntity NewVariable(
		string name,
		string value,
		VariableScope scope = VariableScope.Global,
		string? scopeRefId = null,
		VariableClassification classification = VariableClassification.User,
		VariableType type = VariableType.Text)
		=> new()
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = type,
			Classification = classification,
			Value = value,
			UpdatedAt = DateTime.UtcNow,
		};

	private static (VariableRegistry Registry, List<Guid> Ids) ManyVariables(int count)
	{
		var registry = new VariableRegistry();
		var ids = new List<Guid>(count);
		for (var i = 0; i < count; i++)
		{
			var v = NewVariable($"v{i}", i.ToString(CultureInfo.InvariantCulture));
			registry.Upsert(v);
			ids.Add(v.Id);
		}

		return (registry, ids);
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);

		public CancellationToken ApplicationStopping => CancellationToken.None;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	// RecordingTransport keeps the connection id every send was addressed to, unlike
	// Events/WidgetBatchNotificationFanOutTests's RecordingTransport, which discards it - gating cannot
	// be observed without knowing which connection a message went to.
	private sealed class RecordingTransport : IUiTransport
	{
		public List<(string Kind, string? TargetId, VariablesChangedEvent Event)> Sent { get; } = [];

		public Func<string, bool>? FailConnection { get; set; }

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> throw new NotSupportedException("VariableBroadcaster only ever addresses a group or a connection.");

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(("group", group, (VariablesChangedEvent)(object)message));
			return Task.CompletedTask;
		}

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			if (FailConnection?.Invoke(connectionId) == true)
			{
				throw new InvalidOperationException($"send failed for {connectionId}");
			}

			Sent.Add(("connection", connectionId, (VariablesChangedEvent)(object)message));
			return Task.CompletedTask;
		}

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public List<VariablesChangedEvent> To(string connectionId)
			=> Sent.Where(s => s.Kind == "connection" && s.TargetId == connectionId).Select(s => s.Event).ToList();

		public List<VariablesChangedEvent> Group()
			=> Sent.Where(s => s.Kind == "group" && s.TargetId == VariableGroups.WatchAll)
				.Select(s => s.Event)
				.ToList();
	}

	// A transport whose group send hangs until Release() is called, so a Publish() call can be driven
	// into "stuck in flight" from the test without a real slow network.
	private sealed class GatedTransport : IUiTransport
	{
		private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public volatile bool InFlight;

		public List<VariablesChangedEvent> Sent { get; } = [];

		public void Release() => _gate.TrySetResult();

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> throw new NotSupportedException();

		public async Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			InFlight = true;
			await _gate.Task;
			lock (Sent)
			{
				Sent.Add((VariablesChangedEvent)(object)message);
			}
		}

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	// A hand-rolled fan-out that dispatches to exactly the handlers a test registers, instead of
	// AddMediator()'s whole-assembly scan - the acceptance scenarios only care about a handful of
	// notification handlers, not the rest of the application's wiring.
	private sealed class FanOutMediator : IMediator
	{
		private readonly List<Func<object, CancellationToken, ValueTask>> _handlers = [];

		public void Register<T>(INotificationHandler<T> handler)
			where T : INotification
			=> _handlers.Add((notification, ct) => notification is T typed ? handler.Handle(typed, ct) : default);

		public async ValueTask Publish<TNotification>(
			TNotification notification,
			CancellationToken cancellationToken = default)
			where TNotification : INotification
		{
			foreach (var handler in _handlers)
			{
				await handler(notification!, cancellationToken);
			}
		}

		public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(
			IRequest<TResponse> request,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(
			ICommand<TResponse> command,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(
			IQuery<TResponse> query,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
			IStreamRequest<TResponse> request,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
			IStreamCommand<TResponse> command,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
			IStreamQuery<TResponse> query,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}
}
