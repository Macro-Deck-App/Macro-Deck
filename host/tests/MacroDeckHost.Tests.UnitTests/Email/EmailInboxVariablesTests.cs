using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Infrastructure.SystemIntegrations.Email;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using static MacroDeckHost.Tests.UnitTests.Email.EmailTesting;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxVariablesTests
{
	private static readonly string[] _suffixes =
		["unread_count", "total_count", "latest_sender", "latest_subject", "latest_received_at", "is_connected"];

	private FakeEmailIntegration _imap = null!;
	private FakeEmailIntegration _other = null!;
	private ConfigurableIntegrationRegistry _integrations = null!;
	private EmailWorld _world = null!;
	private FakeEmailVariableApi _variables = null!;

	[SetUp]
	public void SetUp()
	{
		_imap = new FakeEmailIntegration("app.imap", "IMAP").WithInbox("work", "Work");
		_other = new FakeEmailIntegration("app.other", "Other Mail").WithInbox("home", "Home");
		_imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				2,
				[
					Message("standup", -10, sender: "Ann", subject: "Standup notes"),
					Message("invoice", -30, true)
				],
				40));
		_other.Set("home", Snapshot(EmailInboxStatus.Connected, 1, [Message("dinner", -20, sender: "Sam")], 12));
		_integrations = new ConfigurableIntegrationRegistry([_imap, _other]);
		_world = new EmailWorld(_integrations);
		_variables = new FakeEmailVariableApi();
	}

	[TearDown]
	public void TearDown()
	{
		_world.Dispose();
	}

	[Test]
	public async Task Each_inbox_gets_its_own_variables_next_to_the_totals()
	{
		await _world.SyncAllAsync();
		var email = await StartAsync();

		var values = await ReadAllAsync(email);

		Assert.Multiple(() =>
		{
			Assert.That(email.Variables.Select(variable => variable.Name),
				Is.EquivalentTo(new[] { "email_unread_count", "email_inbox_count" }
					.Concat(_suffixes.Select(suffix => "email_work_" + suffix))
					.Concat(_suffixes.Select(suffix => "email_home_" + suffix))));
			Assert.That(values["email_unread_count"], Is.EqualTo(3));
			Assert.That(values["email_inbox_count"], Is.EqualTo(2));
			Assert.That(values["email_work_unread_count"], Is.EqualTo(2));
			Assert.That(values["email_work_total_count"], Is.EqualTo(40));
			Assert.That(values["email_work_latest_sender"], Is.EqualTo("Ann"));
			Assert.That(values["email_work_latest_subject"], Is.EqualTo("Standup notes"));
			Assert.That(values["email_work_latest_received_at"], Is.EqualTo("2026-01-01T11:50:00.0000000Z"));
			Assert.That(values["email_work_is_connected"], Is.EqualTo(true));
			Assert.That(values["email_home_unread_count"], Is.EqualTo(1));
			Assert.That(values["email_home_latest_sender"], Is.EqualTo("Sam"));
			Assert.That(TestLocalization.Resolve(Declared(email, "email_work_unread_count").DisplayName),
				Does.Contain("Work"));
		});
	}

	[Test]
	public async Task A_change_in_an_inbox_is_read_right_away()
	{
		await _world.SyncAllAsync();
		var email = await StartAsync();
		var refreshes = _variables.RefreshRequests;

		_imap.Set("work", Snapshot(EmailInboxStatus.Connected, 5, [Message("urgent", 0, sender: "Carol")]));
		await _world.SyncAllAsync();

		var values = await ReadAllAsync(email);

		Assert.Multiple(() =>
		{
			Assert.That(_variables.RefreshRequests, Is.GreaterThan(refreshes));
			Assert.That(values["email_work_unread_count"], Is.EqualTo(5));
			Assert.That(values["email_unread_count"], Is.EqualTo(6));
			Assert.That(values["email_work_latest_sender"], Is.EqualTo("Carol"));
		});
	}

	[Test]
	public async Task An_inbox_its_running_provider_no_longer_lists_loses_its_variables()
	{
		await _world.SyncAllAsync();
		var email = await StartAsync();
		await _variables.MaterializeAsync(email.Variables);
		var declarations = _variables.DeclarationChanges;

		_imap.Inboxes.Clear();
		_imap.RaiseChanged(null);
		await _world.SyncPendingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(email.Variables.Select(variable => variable.Name), Has.None.StartsWith("email_work_"));
			Assert.That(_variables.Names, Has.None.StartsWith("email_work_"));
			Assert.That(_variables.Names, Has.Some.StartsWith("email_home_"));
			Assert.That(_variables.DeclarationChanges, Is.GreaterThan(declarations));
		});
	}

	[Test]
	public async Task A_disabled_provider_keeps_its_inbox_variables_but_they_read_as_unavailable()
	{
		await _world.SyncAllAsync();
		var email = await StartAsync();
		await _variables.MaterializeAsync(email.Variables);

		_integrations.SetEnabled("app.imap", false);
		await _world.SyncAllAsync();

		var values = await ReadAllAsync(email);

		Assert.Multiple(() =>
		{
			Assert.That(_world.Cache.Snapshot.Inboxes.Select(state => state.Ref),
				Is.EqualTo(new[] { "app.other::home" }));
			Assert.That(email.Variables.Select(variable => variable.Name), Has.Some.EqualTo("email_work_unread_count"));
			Assert.That(_variables.Names, Has.Some.EqualTo("email_work_unread_count"));
			Assert.That(values["email_work_unread_count"], Is.Null);
			Assert.That(values["email_work_is_connected"], Is.Null);
			Assert.That(values["email_inbox_count"], Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_provider_disabled_before_the_start_keeps_the_variables_it_already_had()
	{
		await _world.SyncAllAsync();
		await _variables.MaterializeAsync((await StartAsync()).Variables);

		_integrations.SetEnabled("app.imap", false);
		await _world.SyncAllAsync();
		var restarted = await StartAsync();

		var values = await ReadAllAsync(restarted);

		Assert.Multiple(() =>
		{
			Assert.That(restarted.Variables.Select(variable => variable.Name),
				Has.Some.EqualTo("email_work_latest_subject"));
			Assert.That(values["email_work_latest_subject"], Is.Null);
			Assert.That(_variables.Names, Has.Some.EqualTo("email_work_latest_subject"));
		});
	}

	[Test]
	public async Task Only_the_first_forty_inboxes_seen_get_variables_and_a_later_one_never_displaces_them()
	{
		var bulk = new FakeEmailIntegration("app.bulk");
		for (var index = 0; index < 40; index++)
		{
			bulk.WithInbox("z" + index, "Zulu " + index);
		}

		var integrations = new ConfigurableIntegrationRegistry([bulk]);
		using var world = new EmailWorld(integrations);
		await world.SyncAllAsync();
		var email = await StartAsync(world, integrations);

		bulk.WithInbox("a1", "Alpha 1").WithInbox("a2", "Alpha 2");
		bulk.RaiseChanged(null);
		await world.SyncPendingAsync();

		var names = email.Variables.Select(variable => variable.Name).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(world.Cache.Snapshot.Inboxes, Has.Count.EqualTo(42));
			Assert.That(names, Has.Count.EqualTo(2 + 6 * 40));
			Assert.That(names, Has.Some.EqualTo("email_zulu_0_unread_count"));
			Assert.That(names, Has.Some.EqualTo("email_zulu_39_unread_count"));
			Assert.That(names, Has.None.StartsWith("email_alpha_"));
		});
	}

	private Task<EmailIntegration> StartAsync()
	{
		return StartAsync(_world, _integrations);
	}

	private async Task<EmailIntegration> StartAsync(EmailWorld world, ConfigurableIntegrationRegistry integrations)
	{
		var email = new EmailIntegration();
		email.UseEmailServices(new EmailHostServices(world.Cache, world.Keys, integrations));
		await email.InitializeAsync(new EmailTestContext(_variables));
		return email;
	}

	private static VariableDefinition Declared(EmailIntegration email, string name)
	{
		return email.Variables.Single(variable => variable.Name == name);
	}

	private static async Task<Dictionary<string, object?>> ReadAllAsync(EmailIntegration email)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var variable in email.Variables)
		{
			values[variable.Name!] = (await email.ReadAsync(variable.ResolvedId!)).Value;
		}

		return values;
	}

	private sealed class EmailTestContext(IVariableApi variables) : IIntegrationContext
	{
		public IVariableApi Variables { get; } = variables;

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	private sealed class FakeEmailVariableApi : IVariableApi
	{
		private readonly Dictionary<Guid, VariableHandle> _handles = [];

		public int RefreshRequests { get; private set; }

		public int DeclarationChanges { get; private set; }

		public IReadOnlyList<string> Names => [.. _handles.Values.Select(handle => handle.Name)];

		public async Task MaterializeAsync(IReadOnlyList<VariableDefinition> definitions)
		{
			foreach (var definition in definitions.Where(definition =>
				_handles.Values.All(handle => handle.DefinitionId != definition.ResolvedId)))
			{
				await CreateAsync(definition.Name!, definition.Type, definitionId: definition.ResolvedId);
			}
		}

		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		{
			return Task.FromResult<IReadOnlyList<VariableHandle>>([.. _handles.Values]);
		}

		public Task<VariableHandle?> GetByNameAsync(string name)
		{
			return Task.FromResult(_handles.Values.FirstOrDefault(handle => handle.Name == name));
		}

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			var handle = new VariableHandle(Guid.NewGuid(), name, type, initialValue, decimalPlaces)
			{
				DefinitionId = definitionId
			};
			_handles[handle.Id] = handle;
			return Task.FromResult(handle);
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			return Task.CompletedTask;
		}

		public Task DeleteAsync(Guid variableId)
		{
			_handles.Remove(variableId);
			return Task.CompletedTask;
		}

		public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
		{
			RefreshRequests++;
			return Task.CompletedTask;
		}

		public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
		{
			DeclarationChanges++;
			return Task.CompletedTask;
		}
	}
}
