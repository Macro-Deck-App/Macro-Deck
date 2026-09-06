using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class ImportSingleIconFromPathRequestMessageHandlerTests
{
	private IconTestHarness _harness = null!;
	private FakeAppIconExtractor _extractor = null!;
	private ImportSingleIconFromPathRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_extractor = new FakeAppIconExtractor();
		_handler = new ImportSingleIconFromPathRequestMessageHandler(_extractor, _harness.CreateImportService());
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Handle_ReturnsTheCreatedIconForTheDefaultPack()
	{
		var pack = await _harness.CreatePack("Imported Icons", isDefault: true);
		_extractor.Result = Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([1, 2, 3], "Spotify.png"));

		var response = await _handler.Handle(new ImportSingleIconFromPathRequest { Path = "/Applications/Spotify.app" },
			CancellationToken.None);

		Assert.That(response.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(response.Icon!.Name, Is.EqualTo("Spotify"));
			Assert.That(response.Icon!.PackId, Is.EqualTo(pack.Id.ToString()));
			Assert.That(response.Icon!.ProcessingState, Is.EqualTo(nameof(IconProcessingState.Pending)));
			Assert.That(_harness.Cache.GetIconById(Guid.Parse(response.Icon!.Id)), Is.Not.Null);
			Assert.That(_extractor.Requested, Is.EqualTo(new List<string> { "/Applications/Spotify.app" }));
		});
	}

	[Test]
	public async Task Handle_MissingPath_Fails()
	{
		var response = await _handler.Handle(new ImportSingleIconFromPathRequest { Path = "  " },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.ValidationError)));
			Assert.That(_extractor.Requested, Is.Empty);
		});
	}

	[Test]
	public async Task Handle_InvalidPackId_Fails()
	{
		var response = await _handler.Handle(
			new ImportSingleIconFromPathRequest { PackId = "not-a-guid", Path = "/tmp/logo.png" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.ValidationError)));
		});
	}

	[Test]
	public async Task Handle_UnsupportedSource_FailsWithoutExtracting()
	{
		var response = await _handler.Handle(new ImportSingleIconFromPathRequest { Path = "/tmp/notes.txt" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.UnsupportedFormat)));
			Assert.That(_extractor.Requested, Is.Empty);
		});
	}

	[Test]
	public async Task Handle_ExtractionFailure_ReportsTheExtractorsError()
	{
		await _harness.CreatePack(isDefault: true);
		_extractor.Result = Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat, "no icon in there");

		var response = await _handler.Handle(new ImportSingleIconFromPathRequest { Path = "/tmp/app.exe" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.UnsupportedFormat)));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Is.EqualTo("no icon in there"));
		});
	}

	[Test]
	public async Task Handle_WithoutADefaultPack_Fails()
	{
		_extractor.Result = Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([1], "logo.png"));

		var response = await _handler.Handle(new ImportSingleIconFromPathRequest { Path = "/tmp/logo.png" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.PackNotFound)));
		});
	}
}
