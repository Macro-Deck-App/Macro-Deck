using MacroDeckHost.Integrations.SinusBot;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.SinusBot;

[TestFixture]
internal sealed class SinusBotIntegrationTests
{
	private SinusBotIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_integration = new SinusBotIntegration();
	}

	[Test]
	public void DeclaredVariables_is_a_template_when_nothing_is_configured()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.VariablesDependOnConfiguration, Is.True);
			Assert.That(_integration.Variables, Is.Empty);
			Assert.That(_integration.DeclaredVariables, Is.Not.Empty);
			Assert.That(_integration.DeclaredVariables.Select(v => v.Name),
				Is.All.Contains(VariableNameTemplate.Placeholder("instance")));
		});
	}

	[Test]
	public async Task DeclaredVariables_is_the_real_per_instance_set_once_connected()
	{
		var context = new FakeSinusBotIntegrationContext();
		context.ConfigStore.AddEntry("My Bot",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[SinusBotConfigKeys.ServerUrl] = "http://127.0.0.1:1",
				[SinusBotConfigKeys.Username] = "admin",
				[SinusBotConfigKeys.InstanceId] = "instance-1",
				[SinusBotConfigKeys.InstanceName] = "My Bot"
			},
			new Dictionary<string, string>(StringComparer.Ordinal) { [SinusBotConfigKeys.Password] = "secret" });

		await _integration.InitializeAsync(context);

		var expectedKey = SinusBotIntegration.Slugify("My Bot");
		var configurationKey = _integration.Variables[0].Configuration?.Key;
		var expected = SinusBotVariables.Declare(expectedKey, new VariableConfiguration(configurationKey!, "My Bot"));
		Assert.Multiple(() =>
		{
			Assert.That(configurationKey, Is.Not.Null.And.Not.Empty);
			Assert.That(_integration.Variables, Is.EqualTo(expected));
			Assert.That(_integration.DeclaredVariables, Is.EqualTo(_integration.Variables));
			Assert.That(_integration.DeclaredVariables.Select(v => v.Name),
				Has.None.Contains(VariableNameTemplate.PlaceholderStart));
		});
	}
}
