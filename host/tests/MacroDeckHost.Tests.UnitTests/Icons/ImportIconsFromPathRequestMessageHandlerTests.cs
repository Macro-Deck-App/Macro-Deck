using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class ImportIconsFromPathRequestMessageHandlerTests
{
	private IconTestHarness _harness = null!;
	private ImportIconsFromPathRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_handler = new ImportIconsFromPathRequestMessageHandler(_harness.CreateImportService(), _harness.BatchTracker);
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Handle_NoPaths_FailsValidation()
	{
		var response = await _handler.Handle(new ImportIconsFromPathRequest { Paths = ["  "] },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.ValidationError)));
		});
	}

	// A client may send "paths": null explicitly, which the property initializer does not cover.
	[Test]
	public async Task Handle_NullPaths_FailsValidation()
	{
		var response = await _handler.Handle(new ImportIconsFromPathRequest { Paths = null },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(IconError.ValidationError)));
		});
	}
}
