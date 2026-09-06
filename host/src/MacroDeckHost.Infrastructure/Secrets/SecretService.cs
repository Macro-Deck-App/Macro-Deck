using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.AspNetCore.DataProtection;

namespace MacroDeckHost.Infrastructure.Secrets;

public class SecretService : ISecretService
{
	private readonly ISecretRepository _secretRepository;
	private readonly IDataProtector _protector;

	public SecretService(ISecretRepository secretRepository, IDataProtectionProvider dataProtectionProvider)
	{
		_secretRepository = secretRepository;
		_protector = dataProtectionProvider.CreateProtector("MacroDeck.Secrets");
	}

	public async Task<Guid> Create(string value, SecretKind kind)
	{
		var secret = new SecretEntity
		{
			Id = Guid.NewGuid(),
			Kind = kind,
			EncryptedValue = _protector.Protect(value),
			UpdatedAt = DateTime.UtcNow
		};

		await _secretRepository.Create(secret);

		return secret.Id;
	}

	public async Task<bool> Replace(Guid id, string value)
	{
		var secret = await _secretRepository.GetById(id);
		if (secret is null)
		{
			return false;
		}

		secret.EncryptedValue = _protector.Protect(value);
		secret.UpdatedAt = DateTime.UtcNow;
		await _secretRepository.Update(secret);

		return true;
	}

	public async Task<bool> Delete(Guid id)
	{
		var secret = await _secretRepository.GetById(id);
		if (secret is null)
		{
			return false;
		}

		await _secretRepository.TryDeleteById(id);

		return true;
	}

	public async Task<Guid?> Clone(Guid id)
	{
		var secret = await _secretRepository.GetById(id);
		if (secret is null)
		{
			return null;
		}

		var copy = new SecretEntity
		{
			Id = Guid.NewGuid(),
			Kind = secret.Kind,
			EncryptedValue = secret.EncryptedValue,
			UpdatedAt = DateTime.UtcNow
		};

		await _secretRepository.Create(copy);

		return copy.Id;
	}

	public async Task<string?> Reveal(Guid id)
	{
		var secret = await _secretRepository.GetById(id);
		if (secret is null || secret.Kind != SecretKind.Password)
		{
			return null;
		}

		return _protector.Unprotect(secret.EncryptedValue);
	}

	public async Task<string?> Resolve(Guid id)
	{
		var secret = await _secretRepository.GetById(id);

		return secret is null ? null : _protector.Unprotect(secret.EncryptedValue);
	}

	public async Task<SecretMaterial?> ExportForArchive(Guid id)
	{
		var secret = await _secretRepository.GetById(id);

		return secret is null ? null : new SecretMaterial(secret.Kind, _protector.Unprotect(secret.EncryptedValue));
	}
}
