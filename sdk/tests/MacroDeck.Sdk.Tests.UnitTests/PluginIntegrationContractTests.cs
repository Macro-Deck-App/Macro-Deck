using System.Reflection;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Weather;

namespace MacroDeck.Sdk.Tests.UnitTests;

/// <summary>
/// <see cref="IPluginIntegration" /> exists so that an out-of-process plugin author *cannot* restate what
/// <c>manifest.json</c> already says (#560) - the compiler is the enforcement, not an analyzer. That only
/// holds while the interface stays this shape, so these are surface tests: they fail the moment an
/// identity member is added back, or the in-process <see cref="IIntegration" /> is quietly changed to
/// match it.
/// </summary>
[TestFixture]
public class PluginIntegrationContractTests
{
	/// <summary>
	/// The negative half is the point. A plugin that could declare <c>Name</c> and satisfy the interface
	/// would be exactly the duplication this interface removes.
	/// </summary>
	[Test]
	public void A_plugin_integration_declares_actions_and_a_lifecycle_and_no_identity()
	{
		var members = typeof(IPluginIntegration).GetMembers(BindingFlags.Public | BindingFlags.Instance)
			.Select(member => member.Name)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(members, Does.Contain(nameof(IPluginIntegration.Actions)));
			Assert.That(members, Does.Contain(nameof(IPluginIntegration.InitializeAsync)));
			Assert.That(members, Does.Contain(nameof(IPluginIntegration.ShutdownAsync)));

			Assert.That(members, Has.None.EqualTo("Id"));
			Assert.That(members, Has.None.EqualTo("Name"));
			Assert.That(members, Has.None.EqualTo("Version"));
			Assert.That(members, Has.None.EqualTo("IsInitialized"));
		});
	}

	/// <summary>
	/// The in-process host reads <c>Id</c> on 90-odd call sites and every built-in integration implements
	/// this interface unchanged. #560 deliberately left it alone; this is the guard that says so.
	/// </summary>
	[Test]
	public void The_in_process_integration_contract_is_unchanged()
	{
		var members = typeof(IIntegration).GetMembers(BindingFlags.Public | BindingFlags.Instance)
			.Select(member => member.Name)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(members, Does.Contain("Id"));
			Assert.That(members, Does.Contain("Name"));
			Assert.That(members, Does.Contain("Version"));
			Assert.That(members, Does.Contain("IsInitialized"));
			Assert.That(members, Does.Contain(nameof(IIntegration.Actions)));
		});
	}

	/// <summary>
	/// A plugin type is not an <see cref="IIntegration" /> and vice versa: C# has no structural typing, so
	/// the two contracts are genuinely separate and neither implies the other. If this ever became true by
	/// accident, an in-process integration would start satisfying the plugin constraint and the whole
	/// "cannot restate" guarantee would leak.
	/// </summary>
	[Test]
	public void The_two_integration_contracts_are_unrelated()
	{
		Assert.Multiple(() =>
		{
			Assert.That(typeof(IPluginIntegration).IsAssignableFrom(typeof(IIntegration)), Is.False);
			Assert.That(typeof(IIntegration).IsAssignableFrom(typeof(IPluginIntegration)), Is.False);
		});
	}

	/// <summary>
	/// <c>ProviderName</c> became optional rather than disappearing: removing or renaming it would be a
	/// binary break for every provider compiled against an older SDK, all of which state one.
	/// </summary>
	[TestCase(typeof(IEventProvider))]
	[TestCase(typeof(IWeatherProvider))]
	[TestCase(typeof(IMusicPlayerProvider))]
	[TestCase(typeof(IProfileProvider))]
	public void A_provider_name_is_optional_but_still_declared(Type providerType)
	{
		var property = providerType.GetProperty("ProviderName");

		Assert.That(property, Is.Not.Null, $"{providerType.Name} no longer declares ProviderName.");
		Assert.That(property!.PropertyType, Is.EqualTo(typeof(string)));

		// A default interface implementation is a non-abstract interface method - which is exactly what
		// lets an existing provider that states a name keep binding unchanged.
		Assert.That(property.GetMethod!.IsAbstract, Is.False, "ProviderName is not optional.");
	}
}
