using IconChest.Core.Security;
using Xunit;

namespace IconChest.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_ProducesExpectedFormat()
    {
        var hash = PasswordHasher.HashPassword("MySecretPassword123!");
        Assert.NotNull(hash);

        // Format: $pbkdf2-sha256$v=1$i=600000$s=<salt>$h=<hash>
        var parts = hash.Split('$');
        Assert.Equal(6, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[1]);
        Assert.Equal("v=1", parts[2]);
        Assert.StartsWith("i=", parts[3]);
        Assert.True(int.TryParse(parts[3][2..], out var iterations));
        Assert.True(iterations >= 600_000);
        Assert.StartsWith("s=", parts[4]);
        Assert.StartsWith("h=", parts[5]);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var password = "CorrectHorseBatteryStaple!";
        var hash = PasswordHasher.HashPassword(password);

        Assert.True(PasswordHasher.VerifyPassword(password, hash));
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var password = "CorrectHorseBatteryStaple!";
        var hash = PasswordHasher.HashPassword(password);

        Assert.False(PasswordHasher.VerifyPassword("WrongPassword!", hash));
        Assert.False(PasswordHasher.VerifyPassword("", hash));
    }

    [Fact]
    public void VerifyPassword_MalformedHash_ReturnsFalse()
    {
        Assert.False(PasswordHasher.VerifyPassword("password", ""));
        Assert.False(PasswordHasher.VerifyPassword("password", "invalid$format"));
        Assert.False(PasswordHasher.VerifyPassword("password", "pbkdf2-sha512$1000$salt$hash"));
    }

    [Fact]
    public void HashPassword_SamePasswordProducesDifferentSaltsAndHashes()
    {
        var password = "SamePassword456!";
        var hash1 = PasswordHasher.HashPassword(password);
        var hash2 = PasswordHasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
        Assert.True(PasswordHasher.VerifyPassword(password, hash1));
        Assert.True(PasswordHasher.VerifyPassword(password, hash2));
    }
}
