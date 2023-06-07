using SuchByte.MacroDeck.Utils;

namespace MacroDeck.Tests;

[TestFixture]
public class StringCipherTests
{
    private string machineId;
    
    [SetUp]
    public void Setup()
    {
        machineId = StringCipher.GetMachineGuid();
    }

    [TestCase("This is a short test")]
    [TestCase("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.")]
    public void EncryptionTest(string unencrypted)
    {
        var encrypted = StringCipher.Encrypt(unencrypted, machineId);
        Assert.That(encrypted, Is.Not.EqualTo(unencrypted));

        var decrypted = StringCipher.Decrypt(encrypted, machineId);
        Assert.That(decrypted, Is.EqualTo(unencrypted));
    }
}