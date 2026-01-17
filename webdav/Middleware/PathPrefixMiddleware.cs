using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace WebDav.Middleware;

/// <summary>
/// Middleware that strips a path prefix from incoming requests before they reach NWebDav,
/// allowing NWebDav to work as if it's mounted at the root.
/// </summary>
public class PathPrefixMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _prefix;
    private readonly ILogger<PathPrefixMiddleware> _logger;

    public PathPrefixMiddleware(
        RequestDelegate next,
        string prefix,
        ILogger<PathPrefixMiddleware> logger)
    {
        _next = next;
        _prefix = prefix.TrimEnd('/');
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalPath = context.Request.Path;
        var originalPathBase = context.Request.PathBase;

        // Strip the prefix from the path and add it to PathBase
        if (originalPath.StartsWithSegments(_prefix, out var remainingPath))
        {
            context.Request.Path = remainingPath.Value?.Length > 0 ? remainingPath : "/";
            context.Request.PathBase = originalPathBase.Add(_prefix);

            _logger.LogDebug("Rewrote path: {OriginalPath} -> PathBase: {PathBase}, Path: {Path}",
                originalPath, context.Request.PathBase, context.Request.Path);

            try
            {
                await _next(context);
            }
            finally
            {
                // Restore original values
                context.Request.Path = originalPath;
                context.Request.PathBase = originalPathBase;
            }
        }
        else
        {
            // Path doesn't match prefix, pass through
            await _next(context);
        }
    }
}

public static class PathPrefixMiddlewareExtensions
{
    public static IApplicationBuilder UsePathPrefixRewrite(
        this IApplicationBuilder builder,
        string prefix)
    {
        return builder.UseMiddleware<PathPrefixMiddleware>(prefix);
    }
}
