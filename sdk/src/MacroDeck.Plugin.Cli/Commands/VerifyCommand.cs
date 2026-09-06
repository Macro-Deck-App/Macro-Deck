using System.CommandLine;
using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Signing;
using MacroDeck.Signing.Packages;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin verify</c>: verifies a signed package's embedded signature and certificate against
/// the pinned Macro Deck root (or <c>--root-public</c>) with <see cref="PackageVerifier" />. Cryptographic
/// verification only - revocation is never consulted, and every run says so, on both output formats.
/// </summary>
internal static class VerifyCommand
{
	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

	public static Command Create()
	{
		var packageArgument = new Argument<string>("package") { Description = "The package to verify." };
		var rootPublicOption = new Option<string?>("--root-public")
		{
			Description = "Verify the certificate against this root public key instead of the pinned Macro Deck " +
				"root. Not for production use."
		};
		var outputFormatOption = GlobalOptions.CreateOutputFormatOption();

		var command = new Command("verify", "Verify a signed plugin, icon pack, profile, folder or widget package.");
		command.Add(packageArgument);
		command.Add(rootPublicOption);
		command.Add(outputFormatOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var packagePath = parseResult.GetValue(packageArgument)!;
			var rootPublicPath = parseResult.GetValue(rootPublicOption);
			var outputFormat = parseResult.GetValue(outputFormatOption);

			ReadOnlyMemory<byte>? rootPublicKeyOverride = null;
			var rootAnchored = true;
			if (rootPublicPath is not null)
			{
				var rootPublicKey = await RootPublicKeyReader.ReadAsync(rootPublicPath, cancellationToken)
					.ConfigureAwait(false);
				if (rootPublicKey is null)
				{
					console.WriteError("root-public-key-unreadable",
						$"'{CliText.DisplayPath(rootPublicPath)}' is not a readable base64-encoded Ed25519 public " +
						"key.");
					return ExitCode.InputUnreadable;
				}

				rootPublicKeyOverride = rootPublicKey;
				rootAnchored = RootPublicKeyReader.IsMacroDeckRoot(rootPublicKey);
				if (!rootAnchored)
				{
					console.WriteWarning("non-production-root",
						"Verifying against --root-public instead of the pinned Macro Deck root; a successful " +
						"result is not anchored to the Macro Deck root.");
				}
			}

			var result = await PackageVerifier.VerifyAsync(packagePath,
					ArtifactReaders.ManifestReader,
					rootPublicKeyOverride,
					cancellationToken)
				.ConfigureAwait(false);

			WriteResult(console, outputFormat, result, rootAnchored);

			return result.Success ? ExitCode.Success : SigningFailureExitCode.For(result.Error!.Value);
		});

		return command;
	}

	private static void WriteResult(CliConsole console,
		CliOutputFormat format,
		PackageVerifyResult result,
		bool rootAnchored)
	{
		if (format == CliOutputFormat.Json)
		{
			WriteJson(console, result, rootAnchored);
			return;
		}

		WriteText(console, result, rootAnchored);
	}

	private static void WriteText(CliConsole console, PackageVerifyResult result, bool rootAnchored)
	{
		if (result.Success)
		{
			console.WriteLine($"{result.Format}: valid, signed by certificate {result.CertificateId}" +
				(rootAnchored ? "." : " (not anchored to the Macro Deck root)."));
		}
		else
		{
			console.WriteLine($"invalid: {SigningFailureCode.For(result.Error!.Value)}: {result.Message}");
		}

		console.WriteLine("Revocation was not checked; this is cryptographic verification only.");
	}

	private static void WriteJson(CliConsole console, PackageVerifyResult result, bool rootAnchored)
	{
		var payload = new
		{
			valid = result.Success,
			format = result.Format?.ToString(),
			certificateId = result.CertificateId,
			rootAnchored,
			revocationChecked = false,
			problems = result.Success
				? Array.Empty<object>()
				: new object[] { new { code = SigningFailureCode.For(result.Error!.Value), message = result.Message } }
		};

		console.WriteLine(JsonSerializer.Serialize(payload, _jsonOptions));
	}
}
