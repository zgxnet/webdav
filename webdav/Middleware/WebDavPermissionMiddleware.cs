using NWebDav.Server;
using NWebDav.Server.Helpers;
using NWebDav.Server.Stores;
using WebDav.Services;

namespace WebDav.Middleware;

public class WebDavPermissionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WebDavPermissionMiddleware> _logger;

    public WebDavPermissionMiddleware(
        RequestDelegate next,
        ILogger<WebDavPermissionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            var user = context.Items["WebDavUser"] as UserService.UserInfo;
            
            // If no user (anonymous access), check default permissions
            if (user != null)
            {
                // Path is already stripped of prefix by PathPrefixMiddleware
                var path = context.Request.Path.Value ?? "/";
                var method = context.Request.Method;
                var destination = context.Request.Headers["Destination"].ToString();
                
                if (!string.IsNullOrEmpty(destination))
                {
                    try
                    {
                        var uri = new Uri(destination);
                        var destinationPath = uri.AbsolutePath;
                        
                        // Strip PathBase from destination if present
                        var pathBase = context.Request.PathBase.Value ?? "";
                        if (!string.IsNullOrEmpty(pathBase) && destinationPath.StartsWith(pathBase))
                        {
                            destinationPath = destinationPath.Substring(pathBase.Length);
                        }
                        if (string.IsNullOrEmpty(destinationPath))
                        {
                            destinationPath = "/";
                        }
                        destination = destinationPath;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse destination URI: {Destination}", destination);
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Bad Request: Invalid destination URI");
                        return;
                    }
                }

                bool FileExists(string filePath)
                {
                    try
                    {
                        var fullPath = Path.Combine(user.Directory, filePath.TrimStart('/'));
                        return File.Exists(fullPath) || Directory.Exists(fullPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking file existence for path: {FilePath}", filePath);
                        return false;
                    }
                }

                var allowed = user.Permissions.IsAllowed(method, path, destination, FileExists);
                
                _logger.LogDebug("Permission check: Method={Method}, Path={Path}, Destination={Destination}, User={User}, Allowed={Allowed}", 
                    method, path, destination ?? "(none)", user.Username, allowed);

                if (!allowed)
                {
                    _logger.LogWarning("Permission denied: User={User}, Method={Method}, Path={Path}, Destination={Destination}", 
                        user.Username, method, path, destination ?? "(none)");
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Forbidden");
                    return;
                }
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebDavPermissionMiddleware for path: {Path}", context.Request.Path);
            
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal Server Error");
            }
            else
            {
                _logger.LogWarning("Cannot set 500 status code, response already started for path: {Path}", context.Request.Path);
            }
        }
    }
}

public static class WebDavPermissionMiddlewareExtensions
{
    public static IApplicationBuilder UseWebDavPermissions(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<WebDavPermissionMiddleware>();
    }
}
