using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchEventDefinitionsTests
{
	[Test]
	public void Event_ids_are_unique()
	{
		var ids = TwitchEventDefinitions.All.Select(definition => definition.Id).ToList();

		Assert.That(ids, Is.Unique);
	}

	[Test]
	public void Every_configuration_parameter_has_a_payload_parameter_of_the_same_name()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in TwitchEventDefinitions.All)
			{
				var payloadNames = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(payloadNames,
						Has.Member(configuration.Name),
						$"{definition.Id}.{configuration.Name}");
				}
			}
		});
	}

	[Test]
	public void Every_event_can_be_filtered_by_account_and_says_which_one_it_came_from()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in TwitchEventDefinitions.All)
			{
				Assert.That(definition.ConfigurationParameters.Select(parameter => parameter.Name),
					Has.Member("account"),
					definition.Id);

				Assert.That(definition.PayloadParameters.Select(parameter => parameter.Name),
					Has.Member("account"),
					definition.Id);
			}
		});
	}

	[Test]
	public void No_configuration_parameter_is_required_and_each_says_what_empty_means()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in TwitchEventDefinitions.All)
			{
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(configuration.Required, Is.False, $"{definition.Id}.{configuration.Name}");

					if (configuration.Type is ActionParameterType.DynamicChoice)
					{
						Assert.That(TestLocalization.Resolve(configuration.Placeholder),
							Is.Not.Null.And.Not.Empty,
							$"{definition.Id}.{configuration.Name}");
					}
				}
			}
		});
	}

	[Test]
	public void Every_parameter_uses_a_defined_parameter_type()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in TwitchEventDefinitions.All)
			{
				foreach (var parameter in definition.ConfigurationParameters.Concat(definition.PayloadParameters))
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{definition.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public void Every_event_is_categorised_and_described()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in TwitchEventDefinitions.All)
			{
				Assert.That(TestLocalization.Resolve(definition.Category), Is.Not.Null.And.Not.Empty, definition.Id);
				Assert.That(TestLocalization.Resolve(definition.Description), Is.Not.Null.And.Not.Empty, definition.Id);
			}
		});
	}

	[Test]
	public void The_subscription_catalogue_and_the_definitions_describe_the_same_events()
	{
		var declared = TwitchEventDefinitions.All.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
		var catalogued = TwitchEventCatalog.All.Select(spec => spec.EventId).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(catalogued, Is.SubsetOf(declared));

			Assert.That(declared.Except(catalogued, StringComparer.Ordinal),
				Is.EquivalentTo(new[] { TwitchEventIds.Connected, TwitchEventIds.Disconnected, TwitchEventIds.Any }));
		});
	}

	[Test]
	public void Every_subscription_names_a_version_and_a_scope_we_actually_request()
	{
		Assert.Multiple(() =>
		{
			foreach (var spec in TwitchEventCatalog.All)
			{
				Assert.That(spec.Type, Is.Not.Empty, spec.EventId);
				Assert.That(spec.Version, Is.Not.Empty, spec.EventId);
				Assert.That(Enum.IsDefined(spec.Condition), Is.True, spec.EventId);

				if (spec.Scope is { } scope)
				{
					Assert.That(TwitchScopes.All, Has.Member(scope), spec.EventId);
				}
			}
		});
	}

	[Test]
	public void At_least_one_subscription_needs_no_scope()
	{
		Assert.That(TwitchEventCatalog.All.Where(spec => spec.Scope is null), Is.Not.Empty);
	}

	[Test]
	public void Every_requested_scope_is_used_by_an_event_or_an_action()
	{
		var usedByEvents = TwitchEventCatalog.All
			.Select(spec => spec.Scope)
			.Where(scope => scope is not null)
			.ToHashSet(StringComparer.Ordinal);

		var usedByActions = new[]
		{
			TwitchScopes.UserWriteChat,
			TwitchScopes.ChannelManageBroadcast,
			TwitchScopes.ChannelManageRaids,
			TwitchScopes.ChannelEditCommercial,
			TwitchScopes.ChannelManageAds,
			TwitchScopes.ClipsEdit,
			TwitchScopes.ModeratorManageAnnouncements,
			TwitchScopes.ModeratorManageShoutouts,
			TwitchScopes.ModeratorManageChatMessages,
			TwitchScopes.ModeratorManageChatSettings,
			TwitchScopes.ModeratorManageBannedUsers
		};

		Assert.That(TwitchScopes.All.Except(usedByEvents.Concat(usedByActions), StringComparer.Ordinal), Is.Empty);
	}
}
