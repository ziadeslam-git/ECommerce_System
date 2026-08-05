namespace ECommerce_System.Middleware;

/// <summary>
/// Adds hardened HTTP security headers to every response.
/// Placed first in the middleware pipeline (after UseHttpsRedirection)
/// so headers are always present regardless of what happens downstream.
///
/// Headers added:
///   X-Content-Type-Options   — prevents MIME-type sniffing
///   X-Frame-Options          — blocks clickjacking (iframes)
///   X-XSS-Protection         — legacy XSS filter for older browsers
///   Referrer-Policy          — controls referer header leakage
///   Permissions-Policy       — restricts access to browser APIs
///   Content-Security-Policy  — allowlist of trusted content sources
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // CSP intentionally allows:
    //   - 'self'                       for all local assets
    //   - js.stripe.com                for Stripe.js payment widget
    //   - fonts.googleapis.com         for Google Fonts CSS
    //   - fonts.gstatic.com            for Google Fonts files
    //   - res.cloudinary.com           for Cloudinary product images
    //   - cdn.jsdelivr.net             for Bootstrap / jQuery CDN
    //   - 'unsafe-inline' styles only  required by Bootstrap and Stripe
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self' https://js.stripe.com https://cdn.jsdelivr.net 'unsafe-inline'; " +
        "style-src 'self' https://fonts.googleapis.com https://cdn.jsdelivr.net 'unsafe-inline'; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src 'self' https://res.cloudinary.com data:; " +
        "frame-src https://js.stripe.com; " +
        "connect-src 'self' https://api.stripe.com; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self';";

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"]        = "DENY";
        headers["X-XSS-Protection"]       = "1; mode=block";
        headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"]      = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Content-Security-Policy"] = Csp;

        await next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
