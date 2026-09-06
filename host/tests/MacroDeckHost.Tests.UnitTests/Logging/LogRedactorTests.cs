using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
public class LogRedactorTests
{
	private const string Jwt =
		"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzY29wZSI6ImFkbWluIn0.dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

	[Test]
	public void Redacts_UiWebSocket_Ticket_QueryString()
	{
		var redacted = LogRedactor.Redact("?id=abc&ticket=one-time-secret");

		Assert.That(redacted, Is.EqualTo("?id=abc&ticket=***"));
	}

	[Test]
	public void Redacts_OAuth_AuthorizationCode_But_Keeps_State()
	{
		var redacted = LogRedactor.Redact("?code=AQD5x9k-secret&state=b7f1");

		Assert.That(redacted, Is.EqualTo("?code=***&state=b7f1"));
	}

	[Test]
	public void Redacts_Bare_Jwt_Anywhere_In_The_Line()
	{
		var redacted = LogRedactor.Redact($"Token validation failed for {Jwt} on /hubs/ui");

		Assert.That(redacted, Is.EqualTo("Token validation failed for *** on /hubs/ui"));
	}

	[Test]
	public void Redacts_Authorization_Header_Schemes()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LogRedactor.Redact("Bearer abcdefghijklmnop"), Is.EqualTo("Bearer ***"));
			Assert.That(LogRedactor.Redact("Basic dXNlcjpwYXNzd29yZA=="), Is.EqualTo("Basic ***"));
		});
	}

	[Test]
	public void Redacts_Url_UserInfo()
	{
		var redacted = LogRedactor.Redact("SinusBot connection failed for http://admin:hunter2@bot.local:8087/api");

		Assert.That(redacted, Is.EqualTo("SinusBot connection failed for http://***:***@bot.local:8087/api"));
	}

	[TestCase("--password=hunter2", "--password=***")]
	[TestCase("curl -H \"X-Api-Key: abc123\"", "curl -H \"X-Api-Key: ***\"")]
	[TestCase("client_secret=s3cr3t&grant_type=code", "client_secret=***&grant_type=code")]
	[TestCase("{\"refresh_token\":\"abc\",\"expires_in\":3600}", "{\"refresh_token\":\"***\",\"expires_in\":3600}")]
	public void Redacts_Sensitive_Pairs_In_Commands_Headers_And_Json(string input, string expected)
	{
		Assert.That(LogRedactor.Redact(input), Is.EqualTo(expected));
	}

	[TestCase("Widget 4f2a updated in folder 91bd")]
	[TestCase("Request finished HTTP/1.1 GET http://127.0.0.1:5191/api/profiles - 200")]
	[TestCase("Response code: 500, error_code: 42")]
	[TestCase("Listening on http://127.0.0.1:5191")]
	[TestCase("Hotkey pressed: Ctrl+Shift+F1")]
	[TestCase("Navigated to https://app.example.com/home/dashboard")]
	[TestCase(@"C:\Program Files\MacroDeck\MacroDeck.exe")]
	[TestCase("/Users/Shared/MacroDeck/plugins/weather.dll")]
	[TestCase(@"C:\Users\Public\Documents\shared.json")]
	public void Leaves_Ordinary_Messages_Untouched(string message)
	{
		Assert.That(LogRedactor.Redact(message), Is.EqualTo(message));
	}

	[TestCase(@"Failed to open C:\Users\other-user\Documents\profile.json",
		@"Failed to open C:\Users\<user>\Documents\profile.json")]
	[TestCase("Failed to open /home/other-user/.config/md.json",
		"Failed to open /home/<user>/.config/md.json")]
	[TestCase("Failed to open /Users/other-user/Library/md.json",
		"Failed to open /Users/<user>/Library/md.json")]
	public void Redacts_The_User_Name_In_Another_Machines_Paths(string input, string expected)
	{
		Assert.That(LogRedactor.Redact(input), Is.EqualTo(expected));
	}

	[Test]
	public void Redacts_The_Home_Directory_Of_The_Machine_The_Log_Came_From()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(home))
		{
			Assert.Ignore("The operating system reports no home directory.");
		}

		var redacted = LogRedactor.Redact($"Opening database at {Path.Combine(home, "MacroDeck", "database.db")}");

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Does.Not.Contain(home));
			Assert.That(redacted, Does.Contain("~"));
			Assert.That(redacted, Does.Contain("MacroDeck"));
		});
	}

	[Test]
	public void Redacts_Both_When_A_Path_And_A_Credential_Share_A_Message()
	{
		Assert.Multiple(() =>
		{
			Assert.That(
				LogRedactor.Redact(
					$"Failed to read /home/other-user/.config/md.json while calling ?access_token={Jwt}"),
				Is.EqualTo("Failed to read /home/<user>/.config/md.json while calling ?access_token=***"));

			Assert.That(LogRedactor.Redact(@"--data-dir=C:\Users\other-user\AppData\Roaming\MacroDeck"),
				Does.Not.Contain("other-user"));

			Assert.That(LogRedactor.Redact("--password=/home/other-user/secret.txt"),
				Is.EqualTo("--password=***"));
		});
	}

	[Test]
	public void Handles_Null_And_Empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LogRedactor.Redact(null), Is.Empty);
			Assert.That(LogRedactor.Redact(string.Empty), Is.Empty);
		});
	}

	[Test]
	public void Is_Idempotent()
	{
		var once = LogRedactor.Redact($"?access_token={Jwt}&code=abc");

		Assert.That(LogRedactor.Redact(once), Is.EqualTo(once));
	}

	[TestCase("""{"pluginSecret":"AbCdEf123456"}""", """{"pluginSecret":"***"}""")]
	[TestCase("""{"sessionToken":"AbCdEf123456"}""", """{"sessionToken":"***"}""")]
	[TestCase("""{"enrollmentToken":"AbCdEf123456"}""", """{"enrollmentToken":"***"}""")]
	public void Redacts_Plugin_CamelCase_Keys_In_Json(string input, string expected)
	{
		Assert.That(LogRedactor.Redact(input), Is.EqualTo(expected));
	}

	[TestCase("?pluginSecret=AbCdEf123456&pluginId=com.example.plugin",
		"?pluginSecret=***&pluginId=com.example.plugin")]
	[TestCase("?sessionToken=AbCdEf123456&sessionId=abc", "?sessionToken=***&sessionId=abc")]
	public void Redacts_Plugin_CamelCase_Keys_In_Query_Strings(string input, string expected)
	{
		Assert.That(LogRedactor.Redact(input), Is.EqualTo(expected));
	}

	[TestCase("X-MacroDeck-Plugin-Secret: AbCdEf123456", "X-MacroDeck-Plugin-Secret: ***")]
	[TestCase("X-MacroDeck-Enrollment-Token: AbCdEf123456", "X-MacroDeck-Enrollment-Token: ***")]
	public void Redacts_Plugin_Headers(string input, string expected)
	{
		Assert.That(LogRedactor.Redact(input), Is.EqualTo(expected));
	}

	[Test]
	public void Leaves_PluginId_Untouched()
	{
		var message = "pluginId=com.example.plugin sessionId=abc-123";

		Assert.That(LogRedactor.Redact(message), Is.EqualTo(message));
	}

	[TestCase("PRIVATE KEY")]
	[TestCase("RSA PRIVATE KEY")]
	[TestCase("EC PRIVATE KEY")]
	[TestCase("ENCRYPTED PRIVATE KEY")]
	public void Redacts_A_Whole_Pem_Private_Key_Block(string label)
	{
		var message = $"failed to load: -----BEGIN {label}-----\n" +
			"MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDb3sEcRETuUgxK\n" +
			"c2VjcmV0LWtleS1tYXRlcmlhbC10aGF0LW11c3Qtbm90LWJlLWxvZ2dlZAo=\n" +
			$"-----END {label}-----\n after";

		var redacted = LogRedactor.Redact(message);

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Does.Not.Contain("MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDb3sEcRETuUgxK"));
			Assert.That(redacted, Does.Not.Contain("BEGIN"));
			Assert.That(redacted, Does.Contain("failed to load: ***"));
			Assert.That(redacted, Does.Contain("after"), "surrounding text still has to be readable");
		});
	}

	// The public half of a certificate is not a secret - the user has to be able to copy it into a
	// phone or browser trust store, and redacting it would make a log about it useless.
	[Test]
	public void Leaves_A_Certificate_Block_Untouched()
	{
		var message = "-----BEGIN CERTIFICATE-----\nMIIDazCCAlOgAwIBAgIU\n-----END CERTIFICATE-----";

		Assert.That(LogRedactor.Redact(message), Is.EqualTo(message));
	}
}
