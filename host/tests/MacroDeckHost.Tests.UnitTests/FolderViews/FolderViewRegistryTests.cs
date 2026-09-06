using MacroDeck.Localization;
using MacroDeck.Sdk.FolderViews;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.FolderViews;

/// <summary>
/// The catalog side of the folder view provider contract (issue #785): a registration is qualified and
/// owner-scoped, and the built-in widget grid is always on offer whatever any provider does.
/// </summary>
public class FolderViewRegistryTests
{
	private FolderViewRegistry _registry = null!;

	[SetUp]
	public void SetUp() => _registry = new FolderViewRegistry(new RecordingMediator());

	[Test]
	public async Task Register_QualifiesTheViewIdWithTheOwner()
	{
		var registration = await _registry.Register("com.example.home", Dashboard("dashboard"));

		Assert.That(registration.FolderViewId, Is.EqualTo("com.example.home::dashboard"));
		Assert.That(registration.ProviderId, Is.EqualTo("com.example.home"));
	}

	[Test]
	public void Register_RejectsAnEmptyOwnerId()
		=> Assert.That(() => _registry.Register(string.Empty, Dashboard("dashboard")), Throws.ArgumentException);

	[Test]
	public void Register_RejectsAnEmptyName()
		=> Assert.That(() => _registry.Register("com.example.home", new FolderViewDescriptor("dashboard", default)),
			Throws.ArgumentException);

	[Test]
	public async Task TwoProviders_OfferingTheSameLocalName_ResolveIndependently()
	{
		var first = await _registry.Register("com.example.first", Dashboard("dashboard"));
		var second = await _registry.Register("com.example.second", Dashboard("dashboard"));

		Assert.That(_registry.TryResolve(first.FolderViewId, out var resolvedFirst), Is.True);
		Assert.That(_registry.TryResolve(second.FolderViewId, out var resolvedSecond), Is.True);
		Assert.That(resolvedFirst.ProviderId, Is.EqualTo("com.example.first"));
		Assert.That(resolvedSecond.ProviderId, Is.EqualTo("com.example.second"));
	}

	[Test]
	public async Task Register_UnderAKnownLocalId_ReplacesRatherThanDuplicates()
	{
		await _registry.Register("com.example.home", Dashboard("dashboard"));
		await _registry.Register("com.example.home", Dashboard("dashboard", hasConfiguration: true));

		Assert.That(_registry.GetAll().Count(entry => entry.FolderViewId == "com.example.home::dashboard"),
			Is.EqualTo(1));
		Assert.That(_registry.TryResolve("com.example.home::dashboard", out var resolved), Is.True);
		Assert.That(resolved.Descriptor.HasConfiguration, Is.True);
	}

	[Test]
	public async Task UnregisterAll_WithdrawsOnlyThatOwnersViews()
	{
		var mine = await _registry.Register("com.example.first", Dashboard("dashboard"));
		var theirs = await _registry.Register("com.example.second", Dashboard("dashboard"));

		await _registry.UnregisterAll("com.example.first");

		Assert.That(_registry.TryResolve(mine.FolderViewId, out _), Is.False);
		Assert.That(_registry.TryResolve(theirs.FolderViewId, out _), Is.True);
	}

	[Test]
	public void TheBuiltInGrid_IsAlwaysOnOffer()
		=> Assert.That(_registry.GetAll().Select(entry => entry.FolderViewId),
			Does.Contain(BuiltInFolderViews.WidgetGrid));

	[Test]
	public async Task TheBuiltInGrid_CannotBeWithdrawnByAProvider()
	{
		await _registry.UnregisterAll(string.Empty);
		await _registry.Unregister("macrodeck", "widget-grid");

		Assert.That(_registry.GetAll().Select(entry => entry.FolderViewId),
			Does.Contain(BuiltInFolderViews.WidgetGrid));
	}

	/// <summary>
	/// The grid is rendered by each client, never opened as a session, so resolving it to a provider would
	/// hand a caller an owner that does not exist.
	/// </summary>
	[Test]
	public void TheBuiltInGrid_DoesNotResolveToAProvider()
		=> Assert.That(_registry.TryResolve(BuiltInFolderViews.WidgetGrid, out _), Is.False);

	[Test]
	public void IsWidgetGrid_ReadsAnAbsentIdAsTheGrid()
	{
		Assert.That(BuiltInFolderViews.IsWidgetGrid(null), Is.True);
		Assert.That(BuiltInFolderViews.IsWidgetGrid(string.Empty), Is.True);
		Assert.That(BuiltInFolderViews.IsWidgetGrid(BuiltInFolderViews.WidgetGrid), Is.True);
		Assert.That(BuiltInFolderViews.IsWidgetGrid("com.example.home::dashboard"), Is.False);
	}

	private static FolderViewDescriptor Dashboard(string id, bool hasConfiguration = false)
		=> new(id, LocalizedText.FromLiteral("Dashboard"), HasConfiguration: hasConfiguration);
}
