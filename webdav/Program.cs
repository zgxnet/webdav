using WebDav.Models;
using WebDav.Services;
using WebDav.Middleware;
using NWebDav.Server;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}

// Load WebDAV configuration
var webDavConfig = builder.Configuration.GetSection("WebDav").Get<WebDavConfig>() ?? new WebDavConfig();

// Validate and setup directory
var rootDirectory = Path.GetFullPath(webDavConfig.Directory);
if (!Directory.Exists(rootDirectory))
{
    Directory.CreateDirectory(rootDirectory);
    Console.WriteLine($"Created directory: {rootDirectory}");
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

// Configure DiskStore with the root directory
builder.Services.AddDiskStore(options =>
{
    options.BaseDirectory = rootDirectory;
    options.IsWritable = true;
});

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
logger.LogInformation("Serving directory: {Directory}", rootDirectory);
logger.LogInformation("Blazor file manager UI available at {Protocol}://{Address}:{Port}/", 
    protocol, webDavConfig.Address, webDavConfig.Port);
logger.LogInformation("WebDAV endpoint available at {Protocol}://{Address}:{Port}{Prefix}", 
    protocol, webDavConfig.Address, webDavConfig.Port, webDavConfig.Prefix);

app.Run();
