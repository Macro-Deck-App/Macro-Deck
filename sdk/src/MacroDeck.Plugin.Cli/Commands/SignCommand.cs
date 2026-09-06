using System.CommandLine;
using System.Security.Cryptography;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Signing;
using MacroDeck.Signing;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin sign</c>: signs a signable package with a creator certificate and private key.
/// There is no detached signature file - <see cref="PackageSigner" /> embeds the signature in the
/// artifact's own manifest and writes the certificate material to the archive root. The supplied
/// certificate is chain-verified against the pinned Macro Deck root (or <c>--root-public</c>) before
/// anything is signed, and the written artifact is re-verified with <see cref="PackageVerifier" /> before
/// success is reported - a signed artifact that does not itself verify is a failure, not a success.
/// </summary>
internal static class SignCommand
{
	public static Command Create()
	{
		var packageArgument = new Argument<string>("package") { Description = "The package to sign." };
		var outputOption = new Option<string>("--output")
			{ Description = "Where to write the signed artifact.", Required = true };
		var certificateOption = new Option<string>("--certificate")
			{ Description = "Path to the signing certificate (certificate.json).", Required = true };
		var certificateSignatureOption = new Option<string>("--certificate-signature")
			{ Description = "Path to the root's signature over the certificate (certificate.sig).", Required = true };
		var privateKeyOption = new Option<string>("--private-key")
			{ Description = "Path to the base64-encoded private key.", Required = true };
		var rootPublicOption = new Option<string?>("--root-public")
		{
			Description = "Verify the certificate against this root public key instead of the pinned Macro Deck " +
				"root. Not for production use."
		};

		var command = new Command("sign", "Sign a plugin, icon pack, profile, folder or widget package.");
		command.Add(packageArgument);
		command.Add(outputOption);
		command.Add(certificateOption);
		command.Add(certificateSignatureOption);
		command.Add(privateKeyOption);
		command.Add(rootPublicOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var packagePath = parseResult.GetValue(packageArgument)!;
			var outputPath = parseResult.GetValue(outputOption)!;
			var certificatePath = parseResult.GetValue(certificateOption)!;
			var certificateSignaturePath = parseResult.GetValue(certificateSignatureOption)!;
			var privateKeyPath = parseResult.GetValue(privateKeyOption)!;
			var rootPublicPath = parseResult.GetValue(rootPublicOption);

			if (SignablePackageFormats.Resolve(packagePath) is not { } format)
			{
				return ReportFailure(console,
					SigningError.PackageFormatUnsupported,
					$"'{Path.GetExtension(packagePath)}' is not a signable package format.");
			}

			var certificateBytes = await TryReadFileAsync(certificatePath, cancellationToken).ConfigureAwait(false);
			if (certificateBytes is null)
			{
				return ReportFailure(console,
					SigningError.CertificateUnreadable,
					$"No readable certificate file at '{CliText.DisplayPath(certificatePath)}'.");
			}

			var certificateSignatureBytes =
				await TryReadFileAsync(certificateSignaturePath, cancellationToken).ConfigureAwait(false);
			if (certificateSignatureBytes is null)
			{
				return ReportFailure(console,
					SigningError.CertificateUnreadable,
					$"No readable certificate signature file at '{CliText.DisplayPath(certificateSignaturePath)}'.");
			}

			byte[]? rootPublicKeyOverride = null;
			if (rootPublicPath is not null)
			{
				rootPublicKeyOverride = await RootPublicKeyReader.ReadAsync(rootPublicPath, cancellationToken)
					.ConfigureAwait(false);
				if (rootPublicKeyOverride is null)
				{
					console.WriteError("root-public-key-unreadable",
						$"'{CliText.DisplayPath(rootPublicPath)}' is not a readable base64-encoded Ed25519 public " +
						"key.");
					return ExitCode.InputUnreadable;
				}

				if (!RootPublicKeyReader.IsMacroDeckRoot(rootPublicKeyOverride))
				{
					console.WriteWarning("non-production-root",
						"Verifying against --root-public instead of the pinned Macro Deck root; the signed " +
						"artifact is not anchored to the Macro Deck root.");
				}
			}

			var chainResult = rootPublicKeyOverride is { } customRoot
				? SigningCertificateChain.Verify(certificateBytes,
					certificateSignatureBytes,
					customRoot,
					SigningCertificateChain.PackageKeyUsage)
				: SigningCertificateChain.Verify(certificateBytes,
					certificateSignatureBytes,
					SigningCertificateChain.PackageKeyUsage);

			if (!chainResult.Success)
			{
				return ReportFailure(console, chainResult.Error!.Value, chainResult.Message!);
			}

			var trusted = chainResult.TrustedCertificate!;

			if (SigningCertificateChain.EnsureValidAt(trusted.Certificate, DateTimeOffset.UtcNow) is
				{ } validityFailure)
			{
				return ReportFailure(console, validityFailure.Error, validityFailure.Message);
			}

			var (privateKeyBytes, privateKeyFailure) =
				await TryReadPrivateKeyAsync(privateKeyPath, cancellationToken).ConfigureAwait(false);
			if (privateKeyFailure is { } keyFailure)
			{
				return ReportFailure(console, keyFailure.Error, keyFailure.Message);
			}

			var materialResult = SigningMaterial.Create(privateKeyBytes!, trusted.Certificate);
			CryptographicOperations.ZeroMemory(privateKeyBytes!);

			if (!materialResult.Success)
			{
				return ReportFailure(console, materialResult.Error!.Value, materialResult.Message!);
			}

			using var signer = materialResult.Material!;

			var signResult = await PackageSigner.SignAsync(packagePath,
					outputPath,
					signer,
					certificateBytes,
					certificateSignatureBytes,
					ArtifactReaders.ManifestReader,
					cancellationToken)
				.ConfigureAwait(false);

			if (!signResult.Success)
			{
				return ReportFailure(console, signResult.Error!.Value, signResult.Message!);
			}

			var verifyResult = await PackageVerifier.VerifyAsync(outputPath,
					ArtifactReaders.ManifestReader,
					rootPublicKeyOverride,
					cancellationToken)
				.ConfigureAwait(false);

			if (!verifyResult.Success)
			{
				console.WriteError("post-sign-verification-failed",
					$"The signed artifact at '{CliText.DisplayPath(outputPath)}' did not verify " +
					$"({SigningFailureCode.For(verifyResult.Error!.Value)}): {verifyResult.Message}");
				return ExitCode.InternalError;
			}

			console.Info($"Signed {format} '{CliText.DisplayPath(packagePath)}' -> " +
				$"'{CliText.DisplayPath(outputPath)}' with certificate {signResult.CertificateId}.");

			return ExitCode.Success;
		});

		return command;
	}

	private static int ReportFailure(CliConsole console, SigningError error, string message)
	{
		console.WriteError(SigningFailureCode.For(error), message);
		return SigningFailureExitCode.For(error);
	}

	private static async Task<byte[]?> TryReadFileAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static async Task<(byte[]? Bytes, SigningFailure? Failure)> TryReadPrivateKeyAsync(string path,
		CancellationToken cancellationToken)
	{
		// ReadBase64Async reports an unreadable file and a malformed one the same way, and the two are
		// different answers for a caller: one is the environment, the other is the key itself.
		try
		{
			await using var probe = File.OpenRead(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return (null, new SigningFailure(SigningError.PrivateKeyUnreadable,
				$"No readable private key file at '{CliText.DisplayPath(path)}'."));
		}

		var bytes = await SigningKeyFile.ReadBase64Async(path, Ed25519KeyPair.PrivateKeyLength, cancellationToken)
			.ConfigureAwait(false);
		if (bytes is null)
		{
			return (null, new SigningFailure(SigningError.PrivateKeyMalformed,
				$"'{CliText.DisplayPath(path)}' is not valid base64."));
		}

		return (bytes, null);
	}
}
