using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome.Configuration;
using MacroDeckHost.Integrations.AdGuardHome.Protocol;
using MacroDeckHost.Integrations.AdGuardHome.State;
using MacroDeckHost.Integrations.AdGuardHome.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeWiringTests
{
	[Test]
	public async Task The_integration_feeds_its_own_widgets_without_host_wiring()
	{
		var integration = new AdGuardHomeIntegration(_ => new FakeAdGuardHomeClient(), new FakeTimeProvider());
		var context = new FakeAdGuardHomeContext();
		context.AddEntry("Home",
			new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan" });

		await integration.InitializeAsync(context);

		try
		{
			await CalendarTree.WaitForAsync(() => integration.Instances.Instances is [{ IsConnected: true }],
				"the widgets never saw the instance");
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task A_widget_opened_before_the_integration_initialized_shows_its_icons_afterwards()
	{
		var integration = Integration();
		var context = new FakeAdGuardHomeContext();
		var entryId = context.AddEntry("Home",
			new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan" });
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetType]
					= JsonSerializer.SerializeToElement(AdGuardHomeWidgetType.QualifiedId),
				[UiWidgetSurfaceAttributes.Data]
					= JsonSerializer.SerializeToElement(new { instance = entryId.ToString("D") })
			}
		};
		await using var session = (await integration.CreateSessionAsync(
			new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None))!;
		var logo = await context.UiResources.GetPluginIconAsync("widget", "logo");
		var shield = await context.UiResources.GetPluginIconAsync("widget", "protected");
		Assert.That(Tree(session), Does.Not.Contain(logo.ResourceId));

		await integration.InitializeAsync(context);

		try
		{
			await CalendarTree.WaitForAsync(()
					=> Tree(session).Contains(logo.ResourceId, StringComparison.Ordinal)
					&& Tree(session).Contains(shield.ResourceId, StringComparison.Ordinal),
				"the open widget never showed its icons");
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task A_renamed_instance_keeps_its_variable_key()
	{
		var integration = Integration();
		var existing = Entry("Home", "home");

		var result = await integration.PrepareEntryAsync(Preparation("Home DNS",
			existing.Id,
			existing,
			[existing],
			true));

		Assert.That(Key(result), Is.EqualTo("home"));
	}

	[Test]
	public async Task New_instances_with_the_same_name_get_distinct_variable_keys()
	{
		var integration = Integration();
		var sibling = Entry("Home", "home");

		var result = await integration.PrepareEntryAsync(Preparation("Home", Guid.NewGuid(), null, [sibling], false));

		Assert.That(Key(result), Is.EqualTo("home_2"));
	}

	[Test]
	public async Task Two_instances_being_added_at_once_get_distinct_variable_keys()
	{
		var integration = Integration();

		var first = await integration.PrepareEntryAsync(Preparation("Home", Guid.NewGuid(), null, [], false));
		var second = await integration.PrepareEntryAsync(Preparation("Home", Guid.NewGuid(), null, [], false));

		Assert.That(Key(second), Is.Not.EqualTo(Key(first)));
	}

	[Test]
	public async Task A_released_preparation_frees_its_variable_key()
	{
		var integration = Integration();
		var abandoned = Guid.NewGuid();
		await integration.PrepareEntryAsync(Preparation("Home", abandoned, null, [], false));
		await integration.ReleasePreparationAsync(abandoned);

		var result = await integration.PrepareEntryAsync(Preparation("Home", Guid.NewGuid(), null, [], false));

		Assert.That(Key(result), Is.EqualTo("home"));
	}

	[TestCase(AdGuardHomeConnection.Connected, ConfigEntryStatus.Connected)]
	[TestCase(AdGuardHomeConnection.Unauthorized, ConfigEntryStatus.NeedsReconfiguration)]
	[TestCase(AdGuardHomeConnection.Unreachable, ConfigEntryStatus.Disconnected)]
	[TestCase(AdGuardHomeConnection.Incompatible, ConfigEntryStatus.Disconnected)]
	public async Task The_entry_status_reflects_the_instance_connection(
		AdGuardHomeConnection connection,
		ConfigEntryStatus expected)
	{
		var client = new FakeAdGuardHomeClient();
		if (connection != AdGuardHomeConnection.Connected)
		{
			client.Failure = connection;
		}

		var integration = new AdGuardHomeIntegration(_ => client, new FakeTimeProvider());
		var context = new FakeAdGuardHomeContext();
		var entryId = context.AddEntry("Home",
			new Dictionary<string, string?>
			{
				[AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan",
				[AdGuardHomeConfigKeys.VariableKey] = "home"
			});
		await integration.InitializeAsync(context);

		try
		{
			var entry = Entry("Home", "home") with { Id = entryId };
			await CalendarTree.WaitForAsync(() => integration.GetEntryStatusAsync(entry).Result == expected,
				$"the entry never showed {expected}");
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task An_instance_still_waiting_for_its_server_shows_as_connecting()
	{
		var integration = new AdGuardHomeIntegration(_ => new UnansweredClient(), new FakeTimeProvider());
		var context = new FakeAdGuardHomeContext();
		var entryId = context.AddEntry("Home",
			new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan" });
		await integration.InitializeAsync(context);

		try
		{
			var status = await integration.GetEntryStatusAsync(Entry("Home", "home") with { Id = entryId });

			Assert.That(status, Is.EqualTo(ConfigEntryStatus.Connecting));
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task An_entry_without_a_running_instance_shows_as_disconnected()
	{
		var status = await Integration().GetEntryStatusAsync(Entry("Home", "home"));

		Assert.That(status, Is.EqualTo(ConfigEntryStatus.Disconnected));
	}

	[Test]
	public async Task Synchronizing_removes_the_variables_of_a_deleted_instance_and_keeps_the_others()
	{
		var integration = new AdGuardHomeIntegration(_ => new FakeAdGuardHomeClient(), new FakeTimeProvider());
		var context = new FakeAdGuardHomeContext();
		var kept = Variable("adguard-home-office-dns-queries");
		var orphaned = Variable("adguard-home-home-dns-queries");
		var foreign = Variable("something-else");
		context.OwnVariables.Add(kept);
		context.OwnVariables.Add(orphaned);
		context.OwnVariables.Add(foreign);
		await integration.InitializeAsync(context);

		try
		{
			await integration.SynchronizeEntriesAsync([Entry("Office", "office")]);

			Assert.That((await context.Variables.GetAllAsync()).Select(variable => variable.Id),
				Is.EquivalentTo(new[] { kept.Id, foreign.Id }));
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public void Nothing_is_removed_while_an_entry_has_no_stored_variable_key()
	{
		var legacy = new StoredConfigEntry { Id = Guid.NewGuid(), Title = "Home" };

		Assert.That(AdGuardHomeIntegration.OrphanedVariables([Variable("adguard-home-home-2-dns-queries")], [legacy]),
			Is.Empty);
	}

	[Test]
	public void Deleting_the_last_instance_removes_all_of_its_variables()
	{
		var variable = Variable("adguard-home-home-is-reachable");

		Assert.That(AdGuardHomeIntegration.OrphanedVariables([variable], []), Is.EqualTo(new[] { variable.Id }));
	}

	[Test]
	public async Task A_stored_change_is_applied_without_initializing_the_integration_again()
	{
		var created = 0;
		var integration = new AdGuardHomeIntegration(_ =>
			{
				created++;
				return new FakeAdGuardHomeClient();
			},
			new FakeTimeProvider());
		var context = new FakeAdGuardHomeContext();
		context.AddEntry("Home",
			new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://home.lan" });
		await integration.InitializeAsync(context);

		try
		{
			context.AddEntry("Office",
				new Dictionary<string, string?> { [AdGuardHomeConfigKeys.BaseUrl] = "http://office.lan" });

			var reloaded = await integration.ReloadEntriesAsync();

			Assert.Multiple(() =>
			{
				Assert.That(reloaded, Is.True);
				Assert.That(integration.Instances.Instances.Select(instance => instance.Title),
					Is.EquivalentTo(new[] { "Home", "Office" }));
				Assert.That(created, Is.EqualTo(2));
			});
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	private sealed class UnansweredClient : IAdGuardHomeClient
	{
		public Task<AdGuardHomeServerStatus> GetStatusAsync(CancellationToken cancellationToken)
		{
			return new TaskCompletionSource<AdGuardHomeServerStatus>().Task.WaitAsync(cancellationToken);
		}

		public Task<AdGuardHomeServerStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task SetProtectionAsync(bool enabled, TimeSpan? pause, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task<AdGuardHomeFilteringStatus> GetFilteringStatusAsync(CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task SetFilteringAsync(bool enabled, int interval, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task RefreshFiltersAsync(CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}
	}

	private static string Tree(IUiSession session)
	{
		return JsonSerializer.Serialize(session.BuildTree().Root);
	}

	private static AdGuardHomeIntegration Integration()
	{
		return new AdGuardHomeIntegration(_ => new FakeAdGuardHomeClient(), new FakeTimeProvider());
	}

	private static VariableHandle Variable(string definitionId)
	{
		return new VariableHandle(Guid.NewGuid(), definitionId.Replace('-', '_'), VariableType.Numeric, null, null)
		{
			DefinitionId = definitionId
		};
	}

	private static StoredConfigEntry Entry(string title, string key)
	{
		return new StoredConfigEntry
		{
			Id = Guid.NewGuid(),
			Title = title,
			CreatedAt = DateTime.UtcNow,
			Values = new Dictionary<string, JsonElement>
			{
				[AdGuardHomeConfigKeys.VariableKey] = JsonSerializer.SerializeToElement(key)
			}
		};
	}

	private static ConfigEntryPreparation Preparation(
		string title,
		Guid entryId,
		StoredConfigEntry? existing,
		IReadOnlyList<StoredConfigEntry> siblings,
		bool titleChanged)
	{
		return new ConfigEntryPreparation
		{
			EntryId = entryId,
			Title = title,
			Values = new Dictionary<string, JsonElement>(),
			Existing = existing,
			Siblings = siblings,
			TitleChanged = titleChanged
		};
	}

	private static string? Key(ConfigEntryPreparationResult result)
	{
		return result.Values![AdGuardHomeConfigKeys.VariableKey].GetString();
	}
}
