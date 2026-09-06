namespace MacroDeckHost.Application.Auth;

public interface ISigningKeyProvider
{
	byte[] GetKey();
}
