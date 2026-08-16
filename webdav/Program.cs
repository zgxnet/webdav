using WebDav.Models;
using WebDav.Services;
using WebDav.Middleware;
using NWebDav.Server;
using NWebDav.Server.Stores;

const string windowsServiceSwitch = "--windows-service";
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

app.MapGet("/api/thumbnail", async (
    string path,
    FileManagerService fileService,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var thumbnail = await fileService.CreateThumbnailAsync(path, cancellationToken);
    if (thumbnail == null)
        return Results.NotFound();

    context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
    return Results.File(thumbnail.Content, thumbnail.ContentType);
});

app.MapGet("/api/image", (
    string path,
    FileManagerService fileService) =>
{
    var image = fileService.GetImageFile(path);
    return image == null
        ? Results.NotFound()
        : Results.File(image.FullPath, image.ContentType, enableRangeProcessing: true);
});

// Apply Blazor authentication for non-WebDAV paths
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments(webDavConfig.Prefix),
    blazorApp =>
    {
        blazorApp.UseBasicAuthentication(userService, webDavConfig.BehindProxy, "WebDAV File Manager");
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
