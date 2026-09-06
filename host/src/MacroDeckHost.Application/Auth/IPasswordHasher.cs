namespace MacroDeckHost.Application.Auth;

public interface IPasswordHasher
{
	string Hash(string password);

	bool Verify(string password, string encodedHash);
}
