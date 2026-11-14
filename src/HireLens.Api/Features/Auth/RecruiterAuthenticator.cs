using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HireLens.Api.Features.Auth;

/// <summary>
/// The one seeded recruiter account. Both values come from configuration (environment variables in
/// production: Recruiter__Email, Recruiter__PasswordHash). Only a hash is ever stored, never the password.
/// Generate one with: dotnet run --project src/HireLens.Api -- hash-password
/// </summary>
public sealed class RecruiterOptions
{
    public const string SectionName = "Recruiter";

    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(PasswordHash);
}

/// <summary>Marker type for ASP.NET Core Identity's PasswordHasher (PBKDF2 with a per-hash salt).</summary>
public sealed class Recruiter;

public sealed class RecruiterAuthenticator(IOptions<RecruiterOptions> options, ILogger<RecruiterAuthenticator> logger)
{
    private static readonly PasswordHasher<Recruiter> Hasher = new();

    // Verified when the email is wrong, so a wrong email takes as long as a wrong password and response
    // timing doesn't reveal which emails exist.
    private static readonly string DummyHash = Hasher.HashPassword(new Recruiter(), Guid.NewGuid().ToString());

    public static string Hash(string password) => Hasher.HashPassword(new Recruiter(), password);

    public bool Verify(string? email, string? password)
    {
        var recruiter = options.Value;
        if (!recruiter.IsConfigured)
        {
            logger.LogWarning("Recruiter sign-in attempted but Recruiter:Email / Recruiter:PasswordHash are not configured");
            return false;
        }

        var emailMatches = string.Equals(email?.Trim(), recruiter.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        PasswordVerificationResult result;
        try
        {
            result = Hasher.VerifyHashedPassword(new Recruiter(), emailMatches ? recruiter.PasswordHash : DummyHash, password ?? "");
        }
        catch (FormatException)
        {
            logger.LogError("Recruiter:PasswordHash is not a valid hash. Generate one with the hash-password command");
            return false;
        }

        return emailMatches && result != PasswordVerificationResult.Failed;
    }
}
