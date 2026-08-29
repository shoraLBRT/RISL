using RISL.Application.Security;

namespace RISL.Tests.Security;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_ПринимаетВерныйПароль()
    {
        var (salt, hash) = PasswordHasher.Create("очень секретный пароль");

        Assert.True(PasswordHasher.Verify("очень секретный пароль", salt, hash));
    }

    [Fact]
    public void Verify_ОтклоняетНеверныйПароль()
    {
        var (salt, hash) = PasswordHasher.Create("правильный");

        Assert.False(PasswordHasher.Verify("неправильный", salt, hash));
    }

    [Fact]
    public void Create_ДаётРазныеСолиИХешиДляОдногоПароля()
    {
        var first = PasswordHasher.Create("пароль");
        var second = PasswordHasher.Create("пароль");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData(null, "c29sdA==", "aGFzaA==")]
    [InlineData("", "c29sdA==", "aGFzaA==")]
    [InlineData("пароль", null, "aGFzaA==")]
    [InlineData("пароль", "c29sdA==", null)]
    [InlineData("пароль", "не base64!", "aGFzaA==")]
    [InlineData("пароль", "", "")]
    public void Verify_НеПадаетНаНезаполненномИлиИспорченномКонфиге(string? password, string? salt, string? hash)
    {
        Assert.False(PasswordHasher.Verify(password, salt, hash));
    }
}
