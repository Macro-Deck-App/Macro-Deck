using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.ScreenSavers;

public class DeviceScreenSaverSettingsTests
{
	[Test]
	public void AnAbsentSelection_IsAccepted_AndMeansTheBuiltInClock()
	{
		var result = DeviceScreenSaverSettings.Validate(TestScreenSaverProviders.Registry(), true, 300, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data.ScreenSaverId, Is.Null);
			Assert.That(result.Data.Enabled, Is.True);
		});
	}

	[Test]
	public void AnUnknownSelection_IsRejected()
	{
		var result = DeviceScreenSaverSettings.Validate(TestScreenSaverProviders.Registry(),
			true,
			300,
			"com.example.gone::photos",
			null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(DeviceError.ValidationError));
		});
	}

	[Test]
	public void AConfigurationThatIsNotAnObject_IsRejected()
	{
		var (registry, id) = TestScreenSaverProviders.WithScreenSaver();

		var result = DeviceScreenSaverSettings.Validate(registry, true, 300, id, "[1,2]");

		Assert.That(result.Success, Is.False);
	}

	[TestCase(1)]
	[TestCase(int.MaxValue)]
	public void AnIdleTimeoutOutsideTheBounds_IsRejected(int idleSeconds)
	{
		var result = DeviceScreenSaverSettings.Validate(TestScreenSaverProviders.Registry(), true, idleSeconds, null, null);

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public void TurningItOff_IsAccepted_WhateverTheIdleTimeoutSays()
	{
		var result = DeviceScreenSaverSettings.Validate(TestScreenSaverProviders.Registry(), false, 0, null, null);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data.Enabled, Is.False);
	}

	[Test]
	public void AnOversizedConfiguration_IsRejected()
	{
		var configuration = $"{{\"text\":\"{new string('x', DeviceScreenSaverSettings.MaximumConfigurationLength)}\"}}";

		var result = DeviceScreenSaverSettings.Validate(TestScreenSaverProviders.Registry(), true, 300, null, configuration);

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public void AnUnchangedStoredSelection_IsKept_EvenWhileNothingProvidesIt()
	{
		var result = DeviceScreenSaverSettings.Validate(TestScreenSaverProviders.Registry(),
			true,
			300,
			"com.example.gone::photos",
			null,
			storedScreenSaverId: "com.example.gone::photos");

		Assert.That(result.Success, Is.True);
	}
}
