using MacroDeckHost.Integrations.Weather;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherConfigFlowTests
{
	private static readonly IConfigFlowContext _context = new FakeConfigFlowContext();

	[Test]
	public async Task Submit_WithCity_GeocodesAndCompletes()
	{
		var client = new FakeOpenMeteoClient
		{
			GeocodingResults =
			[
				new GeocodingResult { Name = "Berlin", Latitude = 52.52, Longitude = 13.41, Country = "Germany" }
			]
		};
		var flow = new WeatherConfigFlow(client);

		var input = new Dictionary<string, object?> { ["query"] = "Berlin", ["unit"] = "celsius" };
		var result = await flow.SubmitAsync("location", input, _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Berlin, Germany"));
			Assert.That(result.Values, Is.Not.Null);
			Assert.That(result.Values![WeatherConfigKeys.Latitude].Value, Is.EqualTo("52.52"));
			Assert.That(result.Values![WeatherConfigKeys.Longitude].Value, Is.EqualTo("13.41"));
			Assert.That(result.Values![WeatherConfigKeys.Unit].Value, Is.EqualTo("celsius"));
		});
	}

	[Test]
	public async Task Submit_WithManualCoordinates_Completes()
	{
		var flow = new WeatherConfigFlow(new FakeOpenMeteoClient());

		var input = new Dictionary<string, object?>
		{
			["latitude"] = 48.2,
			["longitude"] = 16.4,
			["unit"] = "fahrenheit"
		};
		var result = await flow.SubmitAsync("location", input, _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values![WeatherConfigKeys.Latitude].Value, Is.EqualTo("48.2"));
			Assert.That(result.Values![WeatherConfigKeys.Longitude].Value, Is.EqualTo("16.4"));
			Assert.That(result.Values![WeatherConfigKeys.Unit].Value, Is.EqualTo("fahrenheit"));
		});
	}

	[Test]
	public async Task Submit_OutOfRangeCoordinates_ReturnsFieldErrors()
	{
		var flow = new WeatherConfigFlow(new FakeOpenMeteoClient());

		var input = new Dictionary<string, object?> { ["latitude"] = 200.0, ["longitude"] = 13.0 };
		var result = await flow.SubmitAsync("location", input, _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Is.Not.Null);
			Assert.That(result.FieldErrors!.Keys, Does.Contain(WeatherConfigKeys.Latitude));
		});
	}

	[Test]
	public async Task Submit_NoCityAndNoCoordinates_ReturnsError()
	{
		var flow = new WeatherConfigFlow(new FakeOpenMeteoClient());

		var result = await flow.SubmitAsync("location",
			new Dictionary<string, object?>(),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Null);
		});
	}

	[Test]
	public async Task Submit_CityNotFound_ReturnsFieldError()
	{
		var flow = new WeatherConfigFlow(new FakeOpenMeteoClient { GeocodingResults = [] });

		var input = new Dictionary<string, object?> { ["query"] = "Xyzzy" };
		var result = await flow.SubmitAsync("location", input, _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Does.Contain("query"));
		});
	}

	[Test]
	public async Task Submit_GeocodingThrows_ReturnsError()
	{
		var flow = new WeatherConfigFlow(new FakeOpenMeteoClient
			{ GeocodingException = new HttpRequestException("down") });

		var input = new Dictionary<string, object?> { ["query"] = "Berlin" };
		var result = await flow.SubmitAsync("location", input, _context, CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private sealed class FakeConfigFlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth { get; } = new FakeOAuthSession();
	}

	private sealed class FakeOAuthSession : IOAuthSession
	{
		public string RedirectUri => "http://localhost/callback";
		public string State => "state";
		public string? AuthorizationCode => null;
	}
}
