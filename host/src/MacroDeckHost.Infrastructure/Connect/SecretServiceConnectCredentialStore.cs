using System.Globalization;
using System.Security.Cryptography;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed class SecretServiceConnectCredentialStore : IConnectCredentialStore
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	public SecretServiceConnectCredentialStore(IServiceScopeFactory scopeFactory, ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<SecretServiceConnectCredentialStore>();
	}

	public async Task<ConnectCredential?> Load(CancellationToken cancellationToken = default)
	{
		// ISecretService and IAppPreferenceRepository are scoped over the DbContext, so every operation
		// owns a scope of its own rather than capturing one in this singleton.
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

		var secretId = await ReadSecretId(preferences);
		if (secretId is null)
		{
			return null;
		}

		string? refreshToken;
		try
		{
			refreshToken = await secrets.Resolve(secretId.Value);
		}
		catch (CryptographicException)
		{
			// A rotated or lost Data Protection key leaves the stored ciphertext unreadable forever; the
			// pointer is dropped so the session reports itself signed out instead of failing every load.
			refreshToken = null;
		}

		if (string.IsNullOrEmpty(refreshToken))
		{
			_logger.Warning("The stored Macro Deck Connect credential could not be resolved and was dropped");
			await DropPointer(preferences, secrets, secretId.Value);
			return null;
		}

		var subject = await ReadValue(preferences, AppPreferenceService.ConnectCredentialSubjectKey);
		if (subject is null)
		{
			await DropPointer(preferences, secrets, secretId.Value);
			return null;
		}

		return new ConnectCredential(refreshToken,
			subject,
			await ReadValue(preferences, AppPreferenceService.ConnectCredentialCachedDisplayNameKey),
			await ReadValue(preferences, AppPreferenceService.ConnectCredentialCachedPictureUrlKey),
			await ReadTimestamp(preferences) ?? DateTimeOffset.UnixEpoch);
	}

	public async Task Save(ConnectCredential credential, CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

		var secretId = await ReadSecretId(preferences);
		if (secretId is null || !await secrets.Replace(secretId.Value, credential.RefreshToken))
		{
			var created = await secrets.Create(credential.RefreshToken, SecretKind.ConnectCredential);
			await preferences.SetValue(AppPreferenceService.ConnectCredentialSecretIdKey, created.ToString("D"));
		}

		await preferences.SetValue(AppPreferenceService.ConnectCredentialSubjectKey, credential.Subject);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialCachedDisplayNameKey,
			credential.CachedDisplayName ?? string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialCachedPictureUrlKey,
			credential.CachedPictureUrl ?? string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialIssuedAtKey,
			credential.IssuedAtUtc.ToString("O", CultureInfo.InvariantCulture));
	}

	public async Task Clear(CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();

		var secretId = await ReadSecretId(preferences);
		if (secretId is not null)
		{
			await secrets.Delete(secretId.Value);
		}

		await preferences.SetValue(AppPreferenceService.ConnectCredentialSecretIdKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialSubjectKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialCachedDisplayNameKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialCachedPictureUrlKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialIssuedAtKey, string.Empty);
	}

	private static async Task DropPointer(
		IAppPreferenceRepository preferences,
		ISecretService secrets,
		Guid secretId)
	{
		await secrets.Delete(secretId);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialSecretIdKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialSubjectKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialCachedDisplayNameKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialCachedPictureUrlKey, string.Empty);
		await preferences.SetValue(AppPreferenceService.ConnectCredentialIssuedAtKey, string.Empty);
	}

	private static async Task<Guid?> ReadSecretId(IAppPreferenceRepository preferences)
	{
		var value = await ReadValue(preferences, AppPreferenceService.ConnectCredentialSecretIdKey);

		return Guid.TryParse(value, out var parsed) ? parsed : null;
	}

	private static async Task<string?> ReadValue(IAppPreferenceRepository preferences, string key)
	{
		var value = (await preferences.GetByKey(key))?.Value;

		return string.IsNullOrEmpty(value) ? null : value;
	}

	private static async Task<DateTimeOffset?> ReadTimestamp(IAppPreferenceRepository preferences)
		=> DateTimeOffset.TryParse(await ReadValue(preferences, AppPreferenceService.ConnectCredentialIssuedAtKey),
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed
			: null;
}
