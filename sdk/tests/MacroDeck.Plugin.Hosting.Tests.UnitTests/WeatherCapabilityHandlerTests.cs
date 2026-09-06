using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Weather;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Weather;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class WeatherCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.Weather,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestWeatherIntegration("Open-Meteo",
			new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() });
		var handler = new WeatherCapabilityHandler([integration], TestMetadata.Default);

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.Weather));
		});
	}

	[Test]
	public void No_provider_declares_nothing() =>
		Assert.That(new WeatherCapabilityHandler([], TestMetadata.Default).DeclareCapabilities(), Is.Empty);

	[Test]
	public async Task Describe_reports_the_provider_name_and_instances()
	{
		var integration = new TestWeatherIntegration("Open-Meteo",
			new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() });
		var handler = new WeatherCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.Weather.Describe),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<WeatherDescribePayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.ProviderName, Is.EqualTo("Open-Meteo"));
			Assert.That(payload.Instances.Single().Id, Is.EqualTo("berlin"));
		});
	}

	[Test]
	public async Task Snapshot_round_trips_to_the_dto()
	{
		var station = new TestWeatherStation
		{
			SnapshotToReturn = new WeatherSnapshot
			{
				IsAvailable = true, LocationName = "Berlin", Condition = WeatherCondition.Rain,
				Unit = TemperatureUnit.Celsius
			}
		};
		var integration = new TestWeatherIntegration("Open-Meteo",
			new Dictionary<string, IWeatherStation> { ["berlin"] = station });
		var handler = new WeatherCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.Weather.Snapshot,
				new { instanceId = "berlin" }),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var dto = result.Data!.Value.Deserialize<WeatherSnapshotDto>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(dto!.LocationName, Is.EqualTo("Berlin"));
			Assert.That(dto.Condition, Is.EqualTo("Rain"));
		});
	}

	[Test]
	public async Task An_unknown_instance_id_is_unavailable()
	{
		var integration = new TestWeatherIntegration("Open-Meteo", new Dictionary<string, IWeatherStation>());
		var handler = new WeatherCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId,
				CapabilityOperations.Weather.Snapshot,
				new { instanceId = "gone" }),
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var integration = new TestWeatherIntegration("Open-Meteo",
			new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() });
		var handler = new WeatherCapabilityHandler([integration], TestMetadata.Default);

		var unknownLocalId =
			await handler.InvokeAsync(Invocation("nope", CapabilityOperations.Weather.Instances),
				CancellationToken.None);
		var unknownOperation =
			await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "rewind"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}
}
