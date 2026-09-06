using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin keygen</c>: generates a creator Ed25519 key pair for <c>sign</c>. Never issues a
/// certificate itself - the printed public key is what gets submitted to the Creator Portal, the only
/// thing that can turn it into a certificate the Macro Deck root has signed.
/// </summary>
internal static class KeygenCommand
{
	private static readonly string[] _reservedKeyNames = ["root", "macrodeck-root", "registry", "macrodeck-registry"];

	public static Command Create()
	{
		var outputOption = new Option<string>("--output")
			{ Description = "Directory to write the key pair into.", DefaultValueFactory = _ => "." };
		var keyNameOption = new Option<string>("--key-name")
			{ Description = "Base file name for the key pair.", DefaultValueFactory = _ => "macrodeck-creator" };

		var command = new Command("keygen", "Generate a creator Ed25519 key pair for 'sign'.");
		command.Add(outputOption);
		command.Add(keyNameOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var output = parseResult.GetValue(outputOption) ?? ".";
			var keyName = parseResult.GetValue(keyNameOption) ?? "macrodeck-creator";

			if (_reservedKeyNames.Contains(keyName, StringComparer.OrdinalIgnoreCase))
			{
				console.WriteError("reserved-key-name",
					$"'{keyName}' is reserved for Macro Deck's own trust anchors and cannot be used as a creator " +
					"key name.");
				return ExitCode.UsageError;
			}

			var publicPath = Path.Combine(output, $"{keyName}.public");
			var privatePath = Path.Combine(output, $"{keyName}.private");

			if (File.Exists(publicPath))
			{
				console.WriteError("output-exists", $"'{CliText.DisplayPath(publicPath)}' already exists.");
				return ExitCode.UsageError;
			}

			if (File.Exists(privatePath))
			{
				console.WriteError("output-exists", $"'{CliText.DisplayPath(privatePath)}' already exists.");
				return ExitCode.UsageError;
			}

			var (privateKey, publicKey) = Ed25519KeyPair.Create();
			var publicKeyBase64 = Convert.ToBase64String(publicKey);

			var publicContent = Encoding.UTF8.GetBytes(publicKeyBase64 + "\n");
			var publicWritten = await SigningKeyFile.WriteNewFileAsync(publicPath, publicContent, cancellationToken)
				.ConfigureAwait(false);
			if (!publicWritten)
			{
				CryptographicOperations.ZeroMemory(privateKey);
				console.WriteError("write-failed", $"Could not write '{CliText.DisplayPath(publicPath)}'.");
				return ExitCode.InternalError;
			}

			// Zeroed by WriteNewPrivateKeyFileAsync itself, whatever the outcome.
			var privateWritten =
				await SigningKeyFile.WriteNewPrivateKeyFileAsync(privatePath, privateKey, cancellationToken)
					.ConfigureAwait(false);
			if (!privateWritten)
			{
				console.WriteError("write-failed", $"Could not write '{CliText.DisplayPath(privatePath)}'.");
				return ExitCode.InternalError;
			}

			if (OperatingSystem.IsWindows())
			{
				console.WriteWarning("restrictive-file-mode-unavailable",
					$"'{CliText.DisplayPath(privatePath)}' could not be created with a restrictive file mode on " +
					"Windows; secure access to it yourself.");
			}

			console.Info($"Public key:  {CliText.DisplayPath(publicPath)}");
			console.Info($"Private key: {CliText.DisplayPath(privatePath)}");
			console.Info($"Public key (base64): {publicKeyBase64}");
			console.Info("Submit the public key above to the Creator Portal for certificate issuance - this " +
				"command does not issue certificates.");

			return ExitCode.Success;
		});

		return command;
	}
}
