using System.Security.Cryptography;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

internal sealed class SwitchableKeyFactory
{
	private readonly List<SwitchableEcdsa> _created = [];

	public bool Available { get; set; } = true;

	public int Created => _created.Count;

	public ECDsa Create(ECParameters parameters)
	{
		if (!Available)
		{
			throw new CryptographicException("The cryptography provider is unavailable.");
		}

		var key = new SwitchableEcdsa(ECDsa.Create(parameters));
		_created.Add(key);
		return key;
	}

	public void BreakCreatedKeys()
	{
		foreach (var key in _created)
		{
			key.Broken = true;
		}
	}

	private sealed class SwitchableEcdsa : ECDsa
	{
		private readonly ECDsa _inner;

		public SwitchableEcdsa(ECDsa inner)
		{
			_inner = inner;
			KeySizeValue = inner.KeySize;
		}

		public bool Broken { get; set; }

		public override byte[] SignHash(byte[] hash)
		{
			return _inner.SignHash(hash);
		}

		public override bool VerifyHash(byte[] hash, byte[] signature)
		{
			return Broken
				? throw new CryptographicException("The cryptography provider is unavailable.")
				: _inner.VerifyHash(hash, signature);
		}

		public override ECParameters ExportParameters(bool includePrivateParameters)
		{
			return _inner.ExportParameters(includePrivateParameters);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_inner.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
