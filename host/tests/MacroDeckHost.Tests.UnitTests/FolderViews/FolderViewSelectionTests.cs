using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.FolderViews;

/// <summary>
/// What a folder is allowed to store as its view (issue #785). The rule the placeholder depends on is
/// here: an id is validated when a user chooses it, never when a folder merely holds one.
/// </summary>
public class FolderViewSelectionTests
{
	[Test]
	public void AnAbsentViewId_IsTheWidgetGridAndCarriesNoConfiguration()
	{
		var registry = TestFolderViewProviders.Registry();

		var result = FolderViewSelection.Validate(registry, null, """{"instanceId":"a"}""");

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data.ViewId, Is.Null);
		Assert.That(result.Data.Configuration, Is.Null);
	}

	[Test]
	public void TheWidgetGrid_CarriesNoConfiguration()
	{
		var registry = TestFolderViewProviders.Registry();

		var result = FolderViewSelection.Validate(registry,
			BuiltInFolderViews.WidgetGrid,
			"""{"instanceId":"a"}""");

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data.Configuration, Is.Null);
	}

	[Test]
	public void AViewNothingProvides_IsRejected()
	{
		var registry = TestFolderViewProviders.Registry();

		var result = FolderViewSelection.Validate(registry, "com.example.home::dashboard", null);

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Is.EqualTo(FolderError.UnknownFolderView));
	}

	[Test]
	public void AProvidedView_IsAcceptedWithItsConfiguration()
	{
		var (registry, folderViewId) = TestFolderViewProviders.WithView();

		var result = FolderViewSelection.Validate(registry, folderViewId, """{"areaId":"living-room"}""");

		Assert.That(result.Success, Is.True);
		Assert.That(result.Data.ViewId, Is.EqualTo(folderViewId));
		Assert.That(result.Data.Configuration, Is.EqualTo("""{"areaId":"living-room"}"""));
	}

	[TestCase("[]")]
	[TestCase("\"text\"")]
	[TestCase("not json")]
	[TestCase("")]
	public void AConfigurationThatIsNotAJsonObject_IsRejected(string configuration)
	{
		var (registry, folderViewId) = TestFolderViewProviders.WithView();

		var result = FolderViewSelection.Validate(registry, folderViewId, configuration);

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Is.EqualTo(FolderError.InvalidFolderViewConfiguration));
	}
}
