using MacroDeck.Localization;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class VariableDtoMapperTests
{
	[Test]
	public void The_wire_scope_a_client_receives_is_widget()
	{
		var dto = VariableDtoMapper.ToDto(new VariableEntity
			{
				Name = "greeting",
				Scope = VariableScope.Widget,
				ScopeRefId = "11111111-1111-1111-1111-111111111111",
				Type = VariableType.Text,
				Classification = VariableClassification.User
			},
			true,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(dto.Scope, Is.EqualTo("widget"));

			// Renaming only the enum and still emitting the old word would leave every client on the old
			// vocabulary for good.
			Assert.That(dto.Scope, Is.Not.EqualTo("actionButton"));
		});
	}

	[Test]
	public void A_client_built_before_the_rename_still_addresses_the_widget_scope()
	{
		Assert.Multiple(() =>
		{
			Assert.That(VariableDtoMapper.ScopeFromWire("widget"), Is.EqualTo(VariableScope.Widget));
			Assert.That(VariableDtoMapper.ScopeFromWire("actionButton"), Is.EqualTo(VariableScope.Widget));
			Assert.That(VariableDtoMapper.ScopeFromWire("ActionButton"), Is.EqualTo(VariableScope.Widget));
			Assert.That(VariableDtoMapper.ScopeFromWire("global"), Is.EqualTo(VariableScope.Global));
			Assert.That(VariableDtoMapper.ScopeFromWire("nonsense"), Is.Null);
		});
	}

	[Test]
	public void The_declared_display_name_reaches_the_wire()
	{
		var displayName = LocalizedText.FromLiteral("CPU usage");

		var dto = VariableDtoMapper.ToDto(new VariableEntity
			{
				Name = "obs_mac_cpu_usage",
				Scope = VariableScope.Global,
				Type = VariableType.Numeric,
				Classification = VariableClassification.Integration,
				Presentation = new VariablePresentation(displayName, null, default)
			},
			true,
			null);

		Assert.That(dto.DisplayName, Is.EqualTo(displayName));
	}

	[Test]
	public void A_variable_with_no_declared_display_name_does_not_fall_back_to_its_identifier()
	{
		var dto = VariableDtoMapper.ToDto(new VariableEntity
			{
				Name = "obs_mac_cpu_usage",
				Scope = VariableScope.Global,
				Type = VariableType.Numeric,
				Classification = VariableClassification.Integration
			},
			true,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(dto.DisplayName.IsEmpty, Is.True);

			// The identifier fallback belongs on the client; if the host filled it in here, the client
			// could never tell "no display name" apart from "display name happens to equal the identifier".
			Assert.That(dto.DisplayName.Literal, Is.Not.EqualTo("obs_mac_cpu_usage"));
		});
	}

	[Test]
	public void Absence_of_a_configuration_invents_nothing()
	{
		var withConfiguration = VariableDtoMapper.ToDto(new VariableEntity
			{
				Name = "obs_mac_current_scene",
				Scope = VariableScope.Global,
				Type = VariableType.Text,
				Classification = VariableClassification.Integration,
				Presentation = new VariablePresentation(default, "mac", LocalizedText.FromLiteral("Mac"))
			},
			true,
			null);

		var withoutConfiguration = VariableDtoMapper.ToDto(new VariableEntity
			{
				Name = "obs_pc_current_scene",
				Scope = VariableScope.Global,
				Type = VariableType.Text,
				Classification = VariableClassification.Integration
			},
			true,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(withConfiguration.ConfigurationKey, Is.EqualTo("mac"));
			Assert.That(withConfiguration.ConfigurationName.Literal, Is.EqualTo("Mac"));

			Assert.That(withoutConfiguration.ConfigurationKey, Is.Null);
			Assert.That(withoutConfiguration.ConfigurationName.IsEmpty, Is.True);
			Assert.That(withoutConfiguration.ConfigurationKey, Is.Not.EqualTo("default"));
			Assert.That(withoutConfiguration.ConfigurationKey, Is.Not.EqualTo("General"));
			Assert.That(withoutConfiguration.ConfigurationKey, Is.Not.EqualTo("obs"));
		});
	}

	[Test]
	public void A_bound_variable_dto_carries_its_resource_id_and_an_ordinary_one_does_not()
	{
		var entity = new VariableEntity
		{
			Name = "kitchen_light",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = "home-assistant",
			DefinitionId = "light.kitchen",
		};

		var bound = VariableDtoMapper.ToDto(entity, available: true, dynamicResourceId: "light.kitchen");
		var ordinary = VariableDtoMapper.ToDto(new VariableEntity
			{
				Name = "greeting",
				Scope = VariableScope.Global,
				Type = VariableType.Text,
				Classification = VariableClassification.User,
			},
			true,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(bound.DynamicResourceId, Is.EqualTo("light.kitchen"));
			Assert.That(ordinary.DynamicResourceId, Is.Null);
		});
	}
}
