using MacroDeck.Localization;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.ScreenSavers;

public class ScreenSaverRegistryTests
{
	private ScreenSaverRegistry _registry = null!;

	[SetUp]
	public void SetUp() => _registry = new ScreenSaverRegistry(new RecordingMediator());

	[Test]
	public async Task Register_QualifiesTheIdWithTheOwner()
	{
		var registration = await _registry.Register("com.example.home", Photos("photos"));

		Assert.Multiple(() =>
		{
			Assert.That(registration.ScreenSaverId, Is.EqualTo("com.example.home::photos"));
			Assert.That(registration.ProviderId, Is.EqualTo("com.example.home"));
			Assert.That(_registry.TryResolve(registration.ScreenSaverId, out _), Is.True);
		});
	}

	[Test]
	public void Register_RejectsAnEmptyName()
		=> Assert.That(() => _registry.Register("com.example.home", new ScreenSaverDescriptor("photos", default)),
			Throws.ArgumentException);

	[Test]
	public async Task RegisteringTheSameLocalIdAgain_ReplacesTheDescriptor()
	{
		await _registry.Register("com.example.home", Photos("photos"));
		await _registry.Register("com.example.home", Photos("photos") with { Interactive = true });

		_registry.TryResolve("com.example.home::photos", out var entry);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetAll(), Has.Count.EqualTo(1));
			Assert.That(entry.Descriptor.Interactive, Is.True);
		});
	}

	[Test]
	public async Task UnregisterAll_WithdrawsOnlyThatOwnersScreenSavers()
	{
		await _registry.Register("com.example.home", Photos("photos"));
		await _registry.Register("com.example.other", Photos("slides"));

		await _registry.UnregisterAll("com.example.home");

		Assert.Multiple(() =>
		{
			Assert.That(_registry.TryResolve("com.example.home::photos", out _), Is.False);
			Assert.That(_registry.TryResolve("com.example.other::slides", out _), Is.True);
		});
	}

	[Test]
	public async Task GetAll_ListsTheBuiltInsFirst()
	{
		var registry = TestScreenSaverProviders.RegistryWithBuiltInClock();
		await registry.Register("com.example.aaa", Photos("photos"));

		Assert.That(registry.GetAll()[0].ScreenSaverId, Is.EqualTo(BuiltInScreenSavers.Clock));
	}

	[Test]
	public void Register_RefusesTheBuiltInOwner()
		=> Assert.That(() => _registry.Register(BuiltInScreenSavers.ProviderId, Photos("clock")), Throws.ArgumentException);

	[Test]
	public void ABuiltInProvider_IsOnOffer_BeforeAnythingRegisters()
	{
		var registry = TestScreenSaverProviders.RegistryWithBuiltInClock();

		Assert.That(registry.TryResolve(BuiltInScreenSavers.Clock, out var entry), Is.True);
		Assert.That(entry.ProviderId, Is.EqualTo(BuiltInScreenSavers.ProviderId));
	}

	private static ScreenSaverDescriptor Photos(string id)
		=> new(id, LocalizedText.FromLiteral("Photos"));
}
