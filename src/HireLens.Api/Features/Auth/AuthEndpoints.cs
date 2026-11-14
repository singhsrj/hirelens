using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HireLens.Api.Features.Auth;

public sealed record LoginRequest([Required, StringLength(254)] string Email, [Required, StringLength(200)] string Password);

public sealed record CurrentRecruiter(string Email);

public static class AuthEndpoints
{
    public const string RecruiterPolicy = "Recruiter";
    public const string RecruiterRole = "recruiter";
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddRecruiterAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RecruiterOptions>(config.GetSection(RecruiterOptions.SectionName));
        services.AddSingleton<RecruiterAuthenticator>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "hirelens.auth";
                o.Cookie.HttpOnly = true;                       // not readable from JavaScript
                o.Cookie.SameSite = SameSiteMode.Strict;        // not sent on cross-site requests (CSRF)
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS-only when served over HTTPS
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
                o.LoginPath = "/login.html";

                // APIs get status codes, not HTML redirects.
                o.Events.OnRedirectToLogin = ctx => RespondOrRedirect(ctx, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = ctx => RespondOrRedirect(ctx, StatusCodes.Status403Forbidden);
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(RecruiterPolicy, p => p.RequireAuthenticatedUser().RequireRole(RecruiterRole));

        return services;
    }

    private static Task RespondOrRedirect(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
            ctx.Response.StatusCode = status;
        else
            ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    public static bool IsRecruiter(this ClaimsPrincipal user) => user.IsInRole(RecruiterRole);

    /// <summary>Recruiter pages are static files, so they're gated here rather than by endpoint authorization.</summary>
    public static IApplicationBuilder UseRecruiterPageGate(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/recruiter") && !ctx.User.IsRecruiter())
        {
            var returnUrl = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString);
            ctx.Response.Redirect($"/login.html?returnUrl={returnUrl}");
            return;
        }
        await next();
    });

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", Login).RequireRateLimiting(LoginRateLimitPolicy).WithSummary("Recruiter sign-in");
        auth.MapPost("/logout", Logout).WithSummary("Sign out");
        auth.MapGet("/me", (ClaimsPrincipal user) => TypedResults.Ok(new CurrentRecruiter(user.Identity!.Name!)))
            .RequireAuthorization(RecruiterPolicy)
            .WithSummary("The signed-in recruiter");

        return app;
    }

    private static async Task<Results<Ok<CurrentRecruiter>, ProblemHttpResult>> Login(
        LoginRequest request, RecruiterAuthenticator authenticator, HttpContext http)
    {
        if (!authenticator.Verify(request.Email, request.Password))
            return TypedResults.Problem(detail: "Email or password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);

        var email = request.Email.Trim().ToLowerInvariant();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Role, RecruiterRole)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return TypedResults.Ok(new CurrentRecruiter(email));
    }

    private static async Task<Ok> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.Ok();
    }
}
