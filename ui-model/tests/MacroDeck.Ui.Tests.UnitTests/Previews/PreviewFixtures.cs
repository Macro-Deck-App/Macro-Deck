using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Ui.Tests.UnitTests.Previews;

// The scenarios the catalog tests scan. Each fixture is its own nested type so one fixture's malformed
// members cannot change what another fixture's scan finds.
internal static class PreviewFixtures
{
	private static UiConfigStack Showing(string state)
		=> new()
			{ Key = "root", Children = [new UiHeading { Key = "state", Text = state }] };

	internal static class SpotifyConfigViewPreviews
	{
		internal static int Invocations { get; private set; }

		internal static void ResetInvocations() => Invocations = 0;

		[UiPreview("Default")]
		internal static UiElement Default()
		{
			Invocations++;

			return Showing("Default");
		}

		[UiPreview("Connected")]
		internal static UiElement Connected()
		{
			Invocations++;

			return Showing("Connected");
		}
	}

	internal static class WeatherDetailsViewPreviews
	{
		[UiPreview("Long text")]
		internal static UiElement LongText() => Showing("Long text");
	}

	internal static class ExplicitlyNamed
	{
		[UiPreview("Default", View = "SpotifyConfigView")]
		internal static UiElement Default() => Showing("Explicit");
	}

	internal sealed class MixedPreviews
	{
		[UiPreview("Default")]
		internal static UiElement Healthy() => Showing("Healthy");

		// Deliberately an instance method: the contract requires static, and this is the member that
		// proves a non-static one is rejected rather than bound.
#pragma warning disable CA1822
		[UiPreview("Instance")]
		internal UiElement Instance() => Showing("Instance");
#pragma warning restore CA1822

		[UiPreview("Parameterized")]
		internal static UiElement Parameterized(string state) => Showing(state);

		[UiPreview("Wrong return")]
		internal static int WrongReturn() => 0;
	}

	internal static class OwnedResourcePreviews
	{
		internal static readonly List<SpyResource> Created = [];

		[UiPreview("Owns a mock")]
		internal static UiPreview OwnsAMock()
		{
			var mock = new SpyResource();
			Created.Add(mock);

			return UiPreview.Of(Showing("Mocked"), mock);
		}
	}

	internal sealed class SpyResource : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}
}
