using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Portable;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableImportDiagnosticsTests
{
	private const string Manifest = """{"formatVersion":2,"kind":"Widgets"}""";
	private const string InvalidContent = """{"kind":"Widgets","widgets":[{"width":3.0}]}""";

	[TestCase(InvalidContent, "$.widgets[0].width")]
	[TestCase("""{"scripts":[{"inputs":[{"type":"number"}]}]}""", "$.scripts[0].inputs[0].type")]
	[TestCase("""{"widgets":[""", "$.widgets[0]")]
	public void Read_InvalidContent_IdentifiesTheEntryAndJsonPath(string content, string path)
	{
		var outcome = PortableArchive.Read(Archive(Manifest, content), null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.InvalidJson));
			Assert.That(outcome.JsonErrorLocation, Does.StartWith($"content.json:{path}:1:"));
			Assert.That(outcome.Content, Is.Null);
		});
	}

	[Test]
	public void Read_InvalidManifest_ReportsTheSameLocationAsInspection()
	{
		var bytes = Archive("""{"formatVersion":"invalid-value"}""", "{}");

		Assert.That(PortableArchive.ReadManifest(bytes, out var location), Is.Null);
		var outcome = PortableArchive.Read(bytes, null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(PortableReadStatus.InvalidJson));
			Assert.That(location, Does.StartWith("manifest.json:$.formatVersion:1:"));
			Assert.That(outcome.JsonErrorLocation, Is.EqualTo(location));
			Assert.That(location, Does.Not.Contain("invalid-value"));
		});
	}

	[Test]
	public void Read_InvalidContent_ReportsOneBasedLineAndByteCoordinates()
	{
		var outcome = PortableArchive.Read(Archive(Manifest, "{\n\"widgets\":[{\"width\":3.0}]}"), null);

		Assert.That(outcome.JsonErrorLocation, Is.EqualTo("content.json:$.widgets[0].width:2:24"));
	}

	[Test]
	public void Read_JsonDiagnostics_DoNotEchoTheInvalidValue()
	{
		const string value = "private-fixture-value";
		var bytes = Archive(Manifest, $$"""{"scripts":[{"inputs":[{"type":"{{value}}"}]}]}""");

		var outcome = PortableArchive.Read(bytes, null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.JsonErrorLocation, Does.Contain("$.scripts[0].inputs[0].type"));
			Assert.That(outcome.JsonErrorLocation, Does.Not.Contain(value));
		});
	}

	[Test]
	public void Read_EncryptedContent_ReportsJsonErrorsOnlyAfterSuccessfulDecryption()
	{
		var manifest = new PortableArchiveManifest
		{
			Kind = PortableArchiveKind.Widgets,
			Encryption = PortableArchiveCrypto.CreateParameters()
		};
		var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PersistenceJsonOptions.Default);
		var inner = Archive(Manifest, InvalidContent);
		var payload = PortableArchiveCrypto.Encrypt(inner, "fixture-password", manifest.Encryption, manifestBytes);
		var bytes = Zip(new Dictionary<string, byte[]>
		{
			["manifest.json"] = manifestBytes,
			["payload.enc"] = payload
		});

		var withoutPassword = PortableArchive.Read(bytes, null);
		var wrongPassword = PortableArchive.Read(bytes, "wrong");
		var decrypted = PortableArchive.Read(bytes, "fixture-password");

		Assert.Multiple(() =>
		{
			Assert.That(withoutPassword.Status, Is.EqualTo(PortableReadStatus.PasswordRequired));
			Assert.That(withoutPassword.JsonErrorLocation, Is.Null);
			Assert.That(wrongPassword.Status, Is.EqualTo(PortableReadStatus.WrongPassword));
			Assert.That(wrongPassword.JsonErrorLocation, Is.Null);
			Assert.That(decrypted.Status, Is.EqualTo(PortableReadStatus.InvalidJson));
			Assert.That(decrypted.JsonErrorLocation, Does.StartWith("content.json:$.widgets[0].width:"));
		});
	}

	[TestCase("Profile")]
	[TestCase("Folder")]
	[TestCase("Widgets")]
	public async Task Import_JsonFailure_ReachesTheLocalizedErrorWithoutChangingProfiles(string kind)
	{
		var events = new List<LogEvent>();
		using var logger = new LoggerConfiguration().WriteTo.Sink(new DelegatingLogSink(events.Add)).CreateLogger();
		using var harness = new PortabilityTestHarness(logger);
		var bytes = Archive(Manifest,
			"""{"scripts":[{"inputs":[{"type":"private-fixture-value"}]}]}""");
		var failure = await Import(harness, kind, bytes);
		var error = PortabilityHttp.ToTransportError(failure.Error!.Value, failure.ErrorMessage);

		Assert.Multiple(() =>
		{
			Assert.That(failure.Success, Is.False);
			Assert.That(error.Code, Is.EqualTo("InvalidJson"));
			Assert.That(error.Message.IsLocalized, Is.True);
			Assert.That(TestLocalization.Resolve(error.Message, "en"),
				Does.StartWith("Invalid JSON data at content.json:$.scripts[0].inputs[0].type:"));
			Assert.That(TestLocalization.Resolve(error.Message, "fr"),
				Does.StartWith("Données JSON invalides à content.json:$.scripts[0].inputs[0].type:"));
			Assert.That(harness.ProfileCache.GetAll(), Is.Empty);
			Assert.That(events, Has.Count.EqualTo(1));
		});
		var log = events.Single();
		Assert.Multiple(() =>
		{
			Assert.That(log.Level, Is.EqualTo(LogEventLevel.Warning));
			Assert.That(log.Properties["Operation"],
				Is.EqualTo(new ScalarValue(kind == "Widgets" ? "WidgetImport" : kind + "Import")));
			Assert.That(log.Properties["ErrorCode"], Is.EqualTo(new ScalarValue("InvalidJson")));
			Assert.That(log.Properties["JsonLocation"], Is.EqualTo(new ScalarValue(failure.ErrorMessage)));
			Assert.That(log.Exception, Is.Null);
			Assert.That(log.RenderMessage(CultureInfo.InvariantCulture), Does.Not.Contain("private-fixture-value"));
		});
	}

	[TestCase("Profile")]
	[TestCase("Folder")]
	[TestCase("Widgets")]
	public async Task Import_OtherFailures_DoNotEmitJsonDiagnostic(string kind)
	{
		var events = new List<LogEvent>();
		using var logger = new LoggerConfiguration().WriteTo.Sink(new DelegatingLogSink(events.Add)).CreateLogger();
		using var harness = new PortabilityTestHarness(logger);
		var encrypted = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Widgets },
			new PortableContent { Kind = PortableArchiveKind.Widgets }, [], "fixture-password");
		var newerManifest = JsonSerializer.Serialize(new
		{
			formatVersion = PortableArchiveManifest.CurrentFormatVersion + 1
		});
		(byte[] Bytes, string? Password, PortabilityError Error)[] cases =
		[
			([], null, PortabilityError.InvalidArchive),
			(Archive(newerManifest, "{}"), null, PortabilityError.UnsupportedVersion),
			(encrypted, null, PortabilityError.PasswordRequired),
			(encrypted, "incorrect", PortabilityError.InvalidPassword)
		];
		foreach (var example in cases)
		{
			var result = await Import(harness, kind, example.Bytes, example.Password);
			Assert.That(result.Error, Is.EqualTo(example.Error));
		}

		Assert.That(events, Is.Empty);
	}

	[Test]
	public async Task Inspect_InvalidManifest_PreservesTheJsonLocation()
	{
		using var harness = new PortabilityTestHarness();
		var events = new List<LogEvent>();
		using var logger = new LoggerConfiguration().WriteTo.Sink(new DelegatingLogSink(events.Add)).CreateLogger();
		var inspector = new PortableArchiveInspector(harness.Integrations, new FakeIntegrationConfigStore(), logger);
		var result = await inspector.Inspect(Archive("{", "{}"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PortabilityError.InvalidJson));
			Assert.That(result.ErrorMessage, Does.StartWith("manifest.json:$:1:"));
			Assert.That(events, Has.Count.EqualTo(1));
		});
		var log = events.Single();
		Assert.Multiple(() =>
		{
			Assert.That(log.Level, Is.EqualTo(LogEventLevel.Warning));
			Assert.That(log.Properties["Operation"], Is.EqualTo(new ScalarValue("Inspect")));
			Assert.That(log.Properties["ErrorCode"], Is.EqualTo(new ScalarValue("InvalidJson")));
			Assert.That(log.Properties["JsonLocation"], Is.EqualTo(new ScalarValue(result.ErrorMessage)));
			Assert.That(log.Exception, Is.Null);
		});
	}

	private static async Task<(bool Success, PortabilityError? Error, string? ErrorMessage)> Import(
		PortabilityTestHarness harness,
		string kind,
		byte[] bytes,
		string? password = null)
	{
		return kind switch
		{
			"Profile" => Failure(await harness.ProfileService.Import(bytes, password, CancellationToken.None)),
			"Folder" => Failure(await harness.FolderService.Import(Guid.NewGuid(), null, bytes, password,
				CancellationToken.None)),
			_ => Failure(await harness.WidgetService.Import(Guid.NewGuid(), 0, 0, bytes, password,
				CancellationToken.None))
		};
	}

	private static (bool Success, PortabilityError? Error, string? ErrorMessage) Failure<T>(
		Result<T, PortabilityError> result)
	{
		return (result.Success, result.Error, result.ErrorMessage);
	}

	private static byte[] Archive(string manifest, string content)
	{
		return Zip(new Dictionary<string, byte[]>
		{
			["manifest.json"] = Encoding.UTF8.GetBytes(manifest),
			["content.json"] = Encoding.UTF8.GetBytes(content)
		});
	}

	private static byte[] Zip(Dictionary<string, byte[]> entries)
	{
		using var buffer = new MemoryStream();
		using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
		{
			foreach (var (name, bytes) in entries)
			{
				using var stream = zip.CreateEntry(name).Open();
				stream.Write(bytes);
			}
		}

		return buffer.ToArray();
	}
}
