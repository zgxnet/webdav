using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using WebDav.Models;
using WebDav.Services;
using WebDav.Middleware;
using NWebDav.Server;
using NWebDav.Server.Stores;

const string windowsServiceSwitch = "--windows-service";
const string webUiCookieScheme = "WebUiCookie";
var runAsWindowsService = args.Any(argument =>
    string.Equals(argument, windowsServiceSwitch, StringComparison.OrdinalIgnoreCase));
var applicationArgs = args
    .Where(argument => !string.Equals(argument, windowsServiceSwitch, StringComparison.OrdinalIgnoreCase))
    .ToArray();

var builder = WebApplication.CreateBuilder(applicationArgs);

if (runAsWindowsService && OperatingSystem.IsWindows())
{
    builder.Host.UseWindowsService();
}

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (runAsWindowsService && OperatingSystem.IsWindows())
{
    WebDav.WindowsServiceLogging.Add(builder.Logging);
}
if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}

// Load WebDAV configuration
var webDavConfig = builder.Configuration.GetSection("WebDav").Get<WebDavConfig>() ?? new WebDavConfig();

// Validate and setup directories
var rootDirectory = Path.GetFullPath(webDavConfig.Directory);
var rootPoints = webDavConfig.RootPoints
    .Select(point => new DiskStoreRootPoint
    {
        Path = point.Path,
        Directory = Path.GetFullPath(point.Directory)
    })
    .ToList();

if (rootPoints.Count == 0 && !Directory.Exists(rootDirectory))
{
    Directory.CreateDirectory(rootDirectory);
    Console.WriteLine($"Created directory: {rootDirectory}");
}
else
{
    foreach (var rootPoint in rootPoints)
    {
        if (!Directory.Exists(rootPoint.Directory))
        {
            Directory.CreateDirectory(rootPoint.Directory);
            Console.WriteLine($"Created directory for '{rootPoint.Path}': {rootPoint.Directory}");
        }
    }
}

// Configure services
builder.Services.AddSingleton(webDavConfig);
builder.Services.AddSingleton<UserService>(sp => 
{
    var logger = sp.GetRequiredService<ILogger<UserService>>();
    return new UserService(webDavConfig, logger);
});

// Add Blazor Server services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<FileManagerService>();
builder.Services
    .AddAuthentication(webUiCookieScheme)
    .AddCookie(webUiCookieScheme, options =>
    {
        options.Cookie.Name = "WebDav.UiAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAntiforgery();

// Configure NWebDav services
builder.Services.AddNWebDav(options =>
{
    options.RequireAuthentication = false; // We handle auth in our custom middleware
});

// Configure the store with either one directory or multiple DAV root points
if (rootPoints.Count == 0)
{
    builder.Services.AddDiskStore(options =>
    {
        options.BaseDirectory = rootDirectory;
        options.IsWritable = true;
    });
}
else
{
    builder.Services.AddSingleton<DiskStoreCollectionPropertyManager>();
    builder.Services.AddSingleton<DiskStoreItemPropertyManager>();
    builder.Services.AddSingleton<IStore>(sp => new MultiRootDiskStore(
        new MultiRootDiskStoreOptions { RootPoints = rootPoints, IsWritable = true },
        sp.GetRequiredService<DiskStoreCollectionPropertyManager>(),
        sp.GetRequiredService<DiskStoreItemPropertyManager>(),
        sp.GetRequiredService<ILoggerFactory>()));
}

// Configure CORS if enabled
if (webDavConfig.Cors.Enabled)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (webDavConfig.Cors.AllowedHosts.Contains("*"))
                policy.AllowAnyOrigin();
            else
                policy.WithOrigins(webDavConfig.Cors.AllowedHosts.ToArray());

            if (webDavConfig.Cors.AllowedMethods.Contains("*"))
                policy.AllowAnyMethod();
            else
                policy.WithMethods(webDavConfig.Cors.AllowedMethods.ToArray());

            if (webDavConfig.Cors.AllowedHeaders.Contains("*"))
                policy.AllowAnyHeader();
            else
                policy.WithHeaders(webDavConfig.Cors.AllowedHeaders.ToArray());

            if (webDavConfig.Cors.Credentials)
                policy.AllowCredentials();

            if (webDavConfig.Cors.ExposedHeaders.Count > 0)
                policy.WithExposedHeaders(webDavConfig.Cors.ExposedHeaders.ToArray());
        });
    });
}

string protocol = webDavConfig.Tls ? "https" : "http";

// Configure Kestrel for custom address/port
builder.WebHost.ConfigureKestrel(options =>
{
    var address = System.Net.IPAddress.Parse(webDavConfig.Address);
    options.Listen(address, webDavConfig.Port, listenOptions =>
    {
        if (webDavConfig.Tls)
        {
            if (!string.IsNullOrEmpty(webDavConfig.Cert))
            {
                listenOptions.UseHttps(webDavConfig.Cert, webDavConfig.Key);
            }
            else
            {
                protocol = "http";
                Console.WriteLine("TLS enabled but certificate/key not properly configured");
            }
        }
    });
});

var app = builder.Build();

// Log configuration warnings
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var userService = app.Services.GetRequiredService<UserService>();

if (!userService.HasUsers)
{
    logger.LogWarning("UNPROTECTED CONFIG: No users have been set, so no authentication will be used");
}

if (webDavConfig.NoPassword)
{
    logger.LogWarning("UNPROTECTED CONFIG: Password check is disabled");
}

// Configure middleware pipeline
if (webDavConfig.Cors.Enabled)
{
    app.UseCors();
}

// Add static files and routing for Blazor
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

app.MapGet("/api/thumbnail", async (
    string path,
    FileManagerService fileService,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    fileService.SetCurrentUser((context.Items["WebDavUser"] as UserService.UserInfo)?.Username);
    var thumbnail = await fileService.CreateThumbnailAsync(path, cancellationToken);
    if (thumbnail == null)
        return Results.NotFound();

    context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
    return Results.File(thumbnail.Content, thumbnail.ContentType);
});

app.MapGet("/api/image", (
    string path,
    int? maxWidth,
    int? maxHeight,
    FileManagerService fileService,
    HttpContext context) =>
{
    fileService.SetCurrentUser((context.Items["WebDavUser"] as UserService.UserInfo)?.Username);
    var image = fileService.GetImageFile(path);
    if (image == null)
        return Results.NotFound();

    if (maxWidth is > 0 && maxHeight is > 0)
    {
        var fittedPreview = fileService.CreateImagePreview(path, maxWidth.Value, maxHeight.Value);
        if (fittedPreview != null)
            return Results.File(fittedPreview.Content, fittedPreview.ContentType);
    }

    return Results.File(image.FullPath, image.ContentType, enableRangeProcessing: true);
});

app.MapGet("/api/pdf", (
    string path,
    FileManagerService fileService,
    HttpContext context) =>
{
    fileService.SetCurrentUser((context.Items["WebDavUser"] as UserService.UserInfo)?.Username);
    var pdf = fileService.GetPdfFile(path);
    if (pdf == null)
        return Results.NotFound();

    context.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
    return Results.File(pdf.FullPath, pdf.ContentType, enableRangeProcessing: true);
});

app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!userService.HasUsers || context.User.Identity?.IsAuthenticated == true)
        return Results.LocalRedirect("/");

    var returnUrl = GetLocalReturnUrl(context.Request.Query["returnUrl"]);
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Content(RenderLoginPage(returnUrl, tokens.RequestToken), "text/html");
});

app.MapPost("/login", async (HttpContext context, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";

    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest("The login form has expired. Reload the login page and try again.");
    }

    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var returnUrl = GetLocalReturnUrl(form["returnUrl"]);
    var user = userService.GetUser(username);

    if (user == null || (!userService.NoPassword && !user.CheckPassword(password)))
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Content(
            RenderLoginPage(returnUrl, tokens.RequestToken, "Invalid username or password."),
            "text/html",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, user.Username) },
        webUiCookieScheme));
    await context.SignInAsync(webUiCookieScheme, principal);
    return Results.LocalRedirect(returnUrl);
});

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(webUiCookieScheme);
    return Results.LocalRedirect("/login");
});

// Protect the web UI with cookie authentication. WebDAV clients continue to
// authenticate with HTTP Basic in the separate branch below.
app.UseWhen(
    context => userService.HasUsers &&
        !context.Request.Path.StartsWithSegments(webDavConfig.Prefix) &&
        context.Request.Path != "/login" &&
        context.Request.Path != "/logout",
    blazorApp =>
    {
        blazorApp.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                await context.ChallengeAsync(
                    webUiCookieScheme,
                    new AuthenticationProperties { RedirectUri = returnUrl });
                return;
            }

            var user = userService.GetUser(context.User.Identity.Name ?? string.Empty);
            if (user == null)
            {
                await context.SignOutAsync(webUiCookieScheme);
                await context.ChallengeAsync(webUiCookieScheme);
                return;
            }

            context.Items["WebDavUser"] = user;
            await next(context);
        });
    });

// Apply WebDAV middleware conditionally based on path prefix
app.UseWhen(
    context => context.Request.Path.StartsWithSegments(webDavConfig.Prefix),
    davApp =>
    {
        davApp.UsePathPrefixRewrite(webDavConfig.Prefix);
        davApp.UseBasicAuthentication(userService, webDavConfig.BehindProxy, "Restricted");
        davApp.UseWebDavPermissions();
        davApp.UseNWebDav();
    });

// Map Blazor endpoints (for UI at root)
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

logger.LogInformation("WebDAV server starting on {Address}:{Port} with prefix '{Prefix}'", 
    webDavConfig.Address, webDavConfig.Port, webDavConfig.Prefix);
if (rootPoints.Count == 0)
    logger.LogInformation("Serving directory: {Directory}", rootDirectory);
else
    logger.LogInformation("Serving {RootPointCount} DAV root points: {RootPoints}", rootPoints.Count, string.Join(", ", rootPoints.Select(point => $"{point.Path}={point.Directory}")));
logger.LogInformation("Blazor file manager UI available at {Protocol}://{Address}:{Port}/", 
    protocol, webDavConfig.Address, webDavConfig.Port);
logger.LogInformation("WebDAV endpoint available at {Protocol}://{Address}:{Port}{Prefix}", 
    protocol, webDavConfig.Address, webDavConfig.Port, webDavConfig.Prefix);

app.Run();

static string GetLocalReturnUrl(string? returnUrl)
{
    return !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}

static string RenderLoginPage(string returnUrl, string? requestToken, string? error = null)
{
    var encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);
    var encodedToken = WebUtility.HtmlEncode(requestToken ?? string.Empty);
    var errorHtml = error == null
        ? string.Empty
        : $"<div class=\"error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";

    return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Sign in - WebDAV File Manager</title>
            <style>
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f4f6f8; font-family: "Segoe UI", sans-serif; color: #212529; }
                main { width: min(24rem, calc(100% - 2rem)); padding: 2rem; background: white; border: 1px solid #dee2e6; border-radius: .5rem; box-shadow: 0 .5rem 1.5rem rgba(0,0,0,.08); }
                h1 { margin: 0 0 1.5rem; font-size: 1.5rem; }
                label { display: block; margin: 1rem 0 .35rem; font-weight: 600; }
                input { width: 100%; padding: .65rem .75rem; border: 1px solid #adb5bd; border-radius: .3rem; font: inherit; }
                input:focus { border-color: #0d6efd; outline: 3px solid rgba(13,110,253,.2); }
                button { width: 100%; margin-top: 1.25rem; padding: .7rem; border: 0; border-radius: .3rem; background: #0d6efd; color: white; font: inherit; font-weight: 600; cursor: pointer; }
                button:hover { background: #0b5ed7; }
                .error { padding: .65rem .75rem; margin-bottom: 1rem; border-radius: .3rem; background: #f8d7da; color: #842029; }
            </style>
        </head>
        <body>
            <main>
                <h1>WebDAV File Manager</h1>
                {{errorHtml}}
                <form method="post" action="/login">
                    <input type="hidden" name="__RequestVerificationToken" value="{{encodedToken}}">
                    <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}">
                    <label for="username">Username</label>
                    <input id="username" name="username" autocomplete="username" autofocus required>
                    <label for="password">Password</label>
                    <input id="password" name="password" type="password" autocomplete="current-password">
                    <button type="submit">Sign in</button>
                </form>
            </main>
        </body>
        </html>
        """;
}
