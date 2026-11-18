using System.ComponentModel;
using System.Diagnostics;
using MacroDeckHost.Application.Mcp;

namespace MacroDeckHost.Mcp;

public sealed partial class ShellMcpExtensionTokenStore : IMcpExtensionTokenStore
{
	public const string Subcommand = "mcp-token";

	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

	private readonly ILogger<ShellMcpExtensionTokenStore> _logger;

	public ShellMcpExtensionTokenStore(ILogger<ShellMcpExtensionTokenStore> logger)
	{
		_logger = logger;
	}

	public async Task<bool> Store(Guid tokenId, string token)
	{
		if (await Run("store", tokenId, token))
		{
			return true;
		}

		await Remove(tokenId);
		return false;
	}

	public async Task Remove(Guid tokenId)
	{
		await Run("delete", tokenId, null);
	}

	// The keychain item trusts the binary that wrote it, so the shell that later runs the bridge
	// has to write it too. The token goes over stdin, never into a process listing.
	private async Task<bool> Run(string action, Guid tokenId, string? input)
	{
		var executable = ClaudeDesktopExtension.ShellExecutable();
		if (executable is null || ClaudeDesktopExtension.CurrentPlatform() is null)
		{
			return false;
		}

		var startInfo = new ProcessStartInfo(executable)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add(Subcommand);
		startInfo.ArgumentList.Add(action);
		startInfo.ArgumentList.Add(tokenId.ToString("D"));

		using var process = new Process();
		process.StartInfo = startInfo;
		try
		{
			process.Start();
		}
		catch (Exception e) when (e is Win32Exception or InvalidOperationException)
		{
			StartFailed(_logger, e, action, tokenId);
			return false;
		}

		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEndAsync();
		try
		{
			if (input is not null)
			{
				await process.StandardInput.WriteAsync(input);
			}

			process.StandardInput.Close();
		}
		catch (IOException)
		{
			// A child that refused its arguments exits before reading stdin; its exit code says so.
		}

		using var timeout = new CancellationTokenSource(_timeout);
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(true);
			}
			catch (InvalidOperationException)
			{
				// It exited between the timeout and the kill.
			}

			TimedOut(_logger, action, tokenId);
			return false;
		}

		await output;
		var message = (await error).Trim();
		if (process.ExitCode == 0)
		{
			return true;
		}

		Failed(_logger, action, tokenId, process.ExitCode, message);
		return false;
	}

	[LoggerMessage(EventId = 5700,
		Level = LogLevel.Warning,
		Message = "Could not start the shell to {Action} the MCP token {TokenId}")]
	private static partial void StartFailed(ILogger logger, Exception exception, string action, Guid tokenId);

	[LoggerMessage(EventId = 5701,
		Level = LogLevel.Warning,
		Message = "The shell did not {Action} the MCP token {TokenId} in time")]
	private static partial void TimedOut(ILogger logger, string action, Guid tokenId);

	[LoggerMessage(EventId = 5702,
		Level = LogLevel.Warning,
		Message = "The shell could not {Action} the MCP token {TokenId}: exit code {ExitCode} {Message}")]
	private static partial void Failed(ILogger logger, string action, Guid tokenId, int exitCode, string message);
}
