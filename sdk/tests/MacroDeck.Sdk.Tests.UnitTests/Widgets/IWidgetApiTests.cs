using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Widgets;

[TestFixture]
public class IWidgetApiTests
{
	/// <summary>
	/// A default interface member does not dispatch off the concrete type, so the default has to be
	/// observed through an <see cref="IWidgetApi" />-typed reference - exactly how the host reads a
	/// widget api it did not author. <see cref="Minimal" /> declares only the three members that predate
	/// <see cref="IWidgetApi.InvalidateIconAsync" />, proving an implementation compiled against an
	/// earlier SDK keeps compiling and keeps behaving exactly as before: the call resolves to the
	/// default and completes without throwing.
	/// </summary>
	[Test]
	public void InvalidateIconAsync_defaults_to_a_completed_no_op_for_an_implementer_that_predates_it()
	{
		IWidgetApi widgets = new Minimal();

		var task = widgets.InvalidateIconAsync("current-track");

		Assert.That(task.IsCompletedSuccessfully, Is.True);
	}

	private sealed class Minimal : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

		public bool Exists(string widgetId) => false;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(false);
	}
}
