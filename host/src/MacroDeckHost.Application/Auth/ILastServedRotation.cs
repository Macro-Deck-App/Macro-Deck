namespace MacroDeckHost.Application.Auth;

// When this host last rotated a refresh token. A successor reads it to tell a rotation its predecessor
// was interrupted at from one it served and long outlived; see AuthService.WithinGrace.
public interface ILastServedRotation
{
	DateTime Read();

	void Record(DateTime rotatedAt);
}
