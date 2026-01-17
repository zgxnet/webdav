using System.Net;
using System.Text;
using WebDav.Services;

namespace WebDav.Middleware;

public class BasicAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly UserService _userService;
    private readonly ILogger<BasicAuthenticationMiddleware> _logger;
    private readonly bool _behindProxy;
    private readonly string _realm;

    public BasicAuthenticationMiddleware(
        RequestDelegate next,
        UserService userService,
        ILogger<BasicAuthenticationMiddleware> logger,
        bool behindProxy,
        string realm = "Restricted")
    {
        _next = next;
        _userService = userService;
        _logger = logger;
        _behindProxy = behindProxy;
        _realm = realm;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _logger.LogDebug("BasicAuthenticationMiddleware invoked for {Method} {Path}", 
            context.Request.Method, context.Request.Path);

        // If no users configured, skip authentication
        if (!_userService.HasUsers)
        {
            _logger.LogDebug("No users configured, skipping authentication");
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers["Authorization"].ToString();
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("No valid Authorization header found");
            SetUnauthorizedResponse(context);
            return;
        }

        try
        {
            var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var parts = credentials.Split(':', 2);

            if (parts.Length != 2)
            {
                SetUnauthorizedResponse(context);
                return;
            }

            var username = parts[0];
            var password = parts[1];

            var user = _userService.GetUser(username);
            if (user == null)
            {
                LogInfo(context, $"Invalid username: {username}");
                SetUnauthorizedResponse(context);
                return;
            }

            if (!_userService.NoPassword && !user.CheckPassword(password))
            {
                LogInfo(context, $"Invalid password for user: {username}");
                SetUnauthorizedResponse(context);
                return;
            }

            LogInfo(context, $"User authorized: {username}");
            
            // Store user info in context for later use
            context.Items["WebDavUser"] = user;
            
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication");
            SetUnauthorizedResponse(context);
        }
    }

    private void SetUnauthorizedResponse(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        context.Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{_realm}\"";
    }

    private void LogInfo(HttpContext context, string message)
    {
        var remoteAddr = GetRealRemoteIP(context);
        _logger.LogInformation("{Message} - Remote: {RemoteAddr}", message, remoteAddr);
    }

    private string GetRealRemoteIP(HttpContext context)
    {
        if (_behindProxy)
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

public static class BasicAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseBasicAuthentication(
        this IApplicationBuilder builder,
        UserService userService,
        bool behindProxy,
        string realm = "Restricted")
    {
        return builder.UseMiddleware<BasicAuthenticationMiddleware>(userService, behindProxy, realm);
    }
}
