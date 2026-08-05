using ECommerce_System.Common.Exceptions;

namespace ECommerce_System.Middleware;

/// <summary>
/// Global exception handler middleware — catches unhandled exceptions from the entire pipeline
/// and routes them to the correct error response. Controllers no longer need try/catch blocks
/// for infrastructure/unexpected errors.
///
/// Handled exception types:
///   NotFoundException        → HTTP 404 + redirect to /Home/Error?code=404
///   ForbiddenException       → HTTP 403 + redirect to /Identity/Account/AccessDenied
///   BusinessException        → HTTP 400 + TempData error + redirect back (for MVC)
///   OperationCanceledException → swallowed silently (client disconnected)
///   Exception                → HTTP 500 + logged + redirect to /Home/Error
///
/// NOTE: In development mode, the built-in developer exception page overrides this
/// (because UseExceptionHandler is only registered in production in Program.cs).
/// This middleware is the production safety net.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate                   next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IWebHostEnvironment               env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — swallow silently, no log spam
            context.Response.StatusCode = 499; // nginx-style "client closed"
        }
        catch (NotFoundException ex)
        {
            logger.LogWarning(ex, "Not found: {Path}", context.Request.Path);
            await HandleMvcErrorAsync(context, 404, ex.Message);
        }
        catch (ForbiddenException ex)
        {
            logger.LogWarning(ex, "Forbidden: {Path} — {UserId}",
                context.Request.Path,
                context.User.Identity?.Name ?? "anonymous");
            await HandleMvcErrorAsync(context, 403, ex.Message);
        }
        catch (BusinessException ex)
        {
            // Business errors are user-friendly — no stack trace needed
            logger.LogInformation(ex, "Business rule violation: {Path}", context.Request.Path);
            await HandleMvcErrorAsync(context, 400, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleMvcErrorAsync(context, 500, null);
        }
    }

    private static async Task HandleMvcErrorAsync(HttpContext context, int statusCode, string? message)
    {
        if (context.Response.HasStarted)
            return; // Too late to change headers

        context.Response.StatusCode = statusCode;

        // For AJAX requests — return JSON
        if (IsAjaxRequest(context))
        {
            context.Response.ContentType = "application/json";
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                success = false,
                error   = message ?? GetDefaultMessage(statusCode)
            });
            await context.Response.WriteAsync(json);
            return;
        }

        // For MVC requests — redirect to error page
        var redirectPath = statusCode switch
        {
            404 => "/Home/Error?code=404",
            403 => "/Identity/Account/AccessDenied",
            _   => "/Home/Error"
        };

        context.Response.Redirect(redirectPath);
    }

    private static bool IsAjaxRequest(HttpContext context)
        => context.Request.Headers.XRequestedWith == "XMLHttpRequest"
        || context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        404 => "The requested resource was not found.",
        403 => "You do not have permission to access this resource.",
        400 => "The request was invalid.",
        _   => "An unexpected error occurred. Please try again."
    };
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
