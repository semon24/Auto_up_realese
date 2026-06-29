using System.Security.Cryptography;
using System.Text;

namespace AutoUpRelease.Api.Auth;

public static class SimpleLoginAuth
{
    const string UserName = "ft-soft";
    const string Password = "Admin_1235";
    const string CookieName = "auto_up_release_auth";

    static readonly string SessionToken = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{UserName}:{Password}")));

    public static WebApplication MapSimpleLoginEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/me", (HttpContext context) =>
        {
            return Results.Json(new { authenticated = IsAuthenticated(context) });
        });

        app.MapPost("/api/auth/login", (LoginBody? body, HttpContext context) =>
        {
            var userName = body?.UserName?.Trim() ?? string.Empty;
            var password = body?.Password ?? string.Empty;

            if (!FixedTimeEquals(userName, UserName) || !FixedTimeEquals(password, Password))
                return Results.Json(new { error = "Invalid username or password" }, statusCode: 401);

            context.Response.Cookies.Append(CookieName, SessionToken, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/auth/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Delete(CookieName);
            return Results.Json(new { ok = true });
        });

        return app;
    }

    public static IApplicationBuilder UseSimpleLoginAuth(this WebApplication app)
    {
        return app.Use(async (context, next) =>
        {
            if (!RequiresLogin(context.Request.Path))
            {
                await next(context);
                return;
            }

            if (IsAuthenticated(context))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
            }
        });
    }

    static bool RequiresLogin(PathString path)
    {
        if (path.StartsWithSegments("/api/auth"))
            return false;

        return path.StartsWithSegments("/api")
            || path.StartsWithSegments("/hubs/ui")
            || path.StartsWithSegments("/swagger");
    }

    static bool IsAuthenticated(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var token))
            return false;

        return FixedTimeEquals(token, SessionToken);
    }

    static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed record LoginBody(string? UserName, string? Password);
