using HireLens.Api.Features.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HireLens.Tests.Features;

public class RecruiterAuthenticatorTests
{
    private const string Email = "recruiter@example.com";
    private const string Password = "correct horse battery";
    private static readonly string Hash = RecruiterAuthenticator.Hash(Password);

    private static RecruiterAuthenticator Create(string email = Email, string? hash = null) =>
        new(Options.Create(new RecruiterOptions { Email = email, PasswordHash = hash ?? Hash }),
            NullLogger<RecruiterAuthenticator>.Instance);

    [Fact]
    public void Correct_email_and_password_succeeds()
    {
        Assert.True(Create().Verify(Email, Password));
    }

    [Fact]
    public void Email_match_ignores_case_and_surrounding_spaces()
    {
        Assert.True(Create().Verify("  Recruiter@Example.COM ", Password));
    }

    [Theory]
    [InlineData(Email, "wrong password")]
    [InlineData(Email, "")]
    [InlineData(Email, null)]
    [InlineData("someone@example.com", Password)]
    [InlineData(null, Password)]
    public void Wrong_credentials_fail(string? email, string? password)
    {
        Assert.False(Create().Verify(email, password));
    }

    [Fact]
    public void Password_is_case_sensitive()
    {
        Assert.False(Create().Verify(Email, Password.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("", "hash")]
    [InlineData(Email, "")]
    public void Unconfigured_recruiter_always_fails(string email, string hash)
    {
        var auth = new RecruiterAuthenticator(
            Options.Create(new RecruiterOptions { Email = email, PasswordHash = hash }),
            NullLogger<RecruiterAuthenticator>.Instance);

        Assert.False(auth.Verify(Email, Password));
    }

    [Fact]
    public void Malformed_hash_fails_instead_of_throwing()
    {
        Assert.False(Create(hash: "not-a-real-hash").Verify(Email, Password));
    }

    [Fact]
    public void Hashes_are_salted_so_the_same_password_hashes_differently()
    {
        Assert.NotEqual(RecruiterAuthenticator.Hash(Password), RecruiterAuthenticator.Hash(Password));
    }
}
