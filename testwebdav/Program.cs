using System.Net;
using WebDav;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

Console.WriteLine("WebDAV Client - List Root Directory");
Console.WriteLine("====================================\n");

// Get URL from command line arguments or prompt
string url;
if (args.Length > 0)
{
    url = args[0];
}
else
{
    Console.Write("Enter WebDAV URL: ");
    url = Console.ReadLine()?.Trim() ?? "";
}

if (string.IsNullOrEmpty(url))
{
    Console.WriteLine("Error: URL is required");
    Console.WriteLine("Usage: testwebdav <url> [username] [password]");
    return 1;
}

// Ensure URL ends with /
if (!url.EndsWith('/'))
{
    url += '/';
}

// Get credentials from command line or prompt
string? username = null;
string? password = null;

if (args.Length > 1)
{
    username = args[1];
}

if (args.Length > 2)
{
    password = args[2];
}
else if (!string.IsNullOrEmpty(username))
{
    Console.Write("Enter password: ");
    password = ReadPassword();
    Console.WriteLine();
}

try
{
    // Create WebDAV client parameters
    var clientParams = new WebDavClientParams();
    
    if (!string.IsNullOrEmpty(username))
    {
        clientParams.Credentials = new NetworkCredential(username, password);
        Console.WriteLine($"Connecting as user: {username}");
    }
    else
    {
        Console.WriteLine("Connecting without authentication");
    }

    // Create WebDAV client
    using var client = new WebDavClient(clientParams);

    Console.WriteLine($"Listing directory: {url}\n");

    // List root directory
    var result = await client.Propfind(url);

    if (result.IsSuccessful)
    {
        Console.WriteLine($"Found {result.Resources.Count} items:\n");
        Console.WriteLine("{0,-50} {1,15} {2}", "Name", "Size", "Type");
        Console.WriteLine(new string('-', 70));

        foreach (var resource in result.Resources)
        {
            var uri = new Uri(resource.Uri);
            var name = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/'));
            
            // Get just the name (last segment)
            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var displayName = segments.Length > 0 ? segments[^1] : "/";
            
            // Skip the root itself
            if (resource.Uri == url)
            {
                displayName = ". (current directory)";
            }

            var type = resource.IsCollection ? "Directory" : "File";
            var size = resource.ContentLength.HasValue && !resource.IsCollection 
                ? FormatSize(resource.ContentLength.Value) 
                : "";

            Console.WriteLine("{0,-50} {1,15} {2}", 
                displayName.Length > 50 ? displayName.Substring(0, 47) + "..." : displayName,
                size,
                type);
        }

        Console.WriteLine();
        return 0;
    }
    else
    {
        Console.WriteLine($"Error: Failed to list directory");
        Console.WriteLine($"Status: {result.StatusCode}");
        Console.WriteLine($"Description: {result.Description}");
        return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string ReadPassword()
{
    var password = "";
    ConsoleKeyInfo key;
    
    do
    {
        key = Console.ReadKey(true);
        
        if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
        {
            password += key.KeyChar;
            Console.Write("*");
        }
        else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password.Substring(0, password.Length - 1);
            Console.Write("\b \b");
        }
    }
    while (key.Key != ConsoleKey.Enter);
    
    return password;
}

static string FormatSize(long bytes)
{
    string[] sizes = { "B", "KB", "MB", "GB", "TB" };
    double len = bytes;
    int order = 0;
    
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len = len / 1024;
    }
    
    return $"{len:0.##} {sizes[order]}";
}
