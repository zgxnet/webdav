# WebDAV Server (.NET)

A simple and standalone WebDAV server implementation in C# ASP.NET Core, based on the [hacdias/webdav](https://github.com/hacdias/webdav) Go project.

## Features

- ✅ Full WebDAV support (GET, PUT, DELETE, PROPFIND, MKCOL, COPY, MOVE, etc.)
- ✅ Blazor file manager web UI
- ✅ Basic authentication with multiple users
- ✅ BCrypt password support
- ✅ Fine-grained permission control (Create, Read, Update, Delete)
- ✅ Path-based and regex-based access rules
- ✅ CORS support
- ✅ TLS/HTTPS support
- ✅ Environment variable support for sensitive data
- ✅ Configurable via JSON/YAML

## Installation

### Prerequisites

- .NET 9.0 SDK or later

### Build from Source

```bash
cd webdav
dotnet build
dotnet run
```

## Usage

### URL Structure

The server provides two endpoints:

- **Blazor UI**: `http://localhost:6065/` - Web-based file manager interface
- **WebDAV**: `http://localhost:6065/dav` - WebDAV protocol endpoint for mounting as network drive

### Configuration

The server can be configured via `appsettings.json` or `appsettings.Development.json`. Here's a complete configuration example:

```json
{
  "WebDav": {
    "Address": "0.0.0.0",
    "Port": 6065,
    "Tls": false,
    "Cert": null,
    "Key": null,
    "Prefix": "/dav",
    "Debug": false,
    "NoSniff": false,
    "NoPassword": false,
    "BehindProxy": false,
    "Directory": "./data",
    "Permissions": "R",
    "RulesBehavior": "overwrite",
    "Users": [
      {
        "Username": "admin",
        "Password": "admin",
        "Directory": "./data",
        "Permissions": "CRUD",
        "Rules": []
      }
    ],
    "Rules": [],
    "Cors": {
      "Enabled": false,
      "Credentials": false,
      "AllowedHeaders": ["*"],
      "AllowedHosts": ["*"],
      "AllowedMethods": ["*"],
      "ExposedHeaders": []
    }
  }
}
```

### Configuration Options

- **Address**: IP address to bind to (default: `0.0.0.0`)
- **Port**: Port to listen on (default: `6065`)
- **Tls**: Enable TLS/HTTPS (default: `false`)
- **Cert**: Path to TLS certificate file
- **Key**: Path to TLS key file
- **Prefix**: URL prefix for WebDAV endpoint (default: `/dav`)
- **Debug**: Enable debug logging (default: `false`)
- **NoSniff**: Disable content-type sniffing (default: `false`)
- **NoPassword**: Disable password checking (default: `false`)
- **BehindProxy**: Trust X-Forwarded-For header (default: `false`)
- **Directory**: Root directory to serve (default: `./data`)
- **Permissions**: Default permissions (default: `R`)
  - `C` - Create
  - `R` - Read
  - `U` - Update
  - `D` - Delete
  - Combine multiple: `CRUD` for full access
- **RulesBehavior**: How to handle rules (`overwrite` or `append`)

### User Configuration

Users can have individual directories and permissions:

```json
{
  "Users": [
    {
      "Username": "readonly",
      "Password": "password123",
      "Directory": "./data/readonly",
      "Permissions": "R"
    },
    {
      "Username": "admin",
      "Password": "{bcrypt}$2a$10$...",
      "Directory": "./data/admin",
      "Permissions": "CRUD"
    }
  ]
}
```

### BCrypt Passwords

For security, use BCrypt hashed passwords:

1. Generate a BCrypt hash (you can use online tools or .NET BCrypt library)
2. Prefix with `{bcrypt}`: `{bcrypt}$2a$10$...`

### Environment Variables

Use `{env}` prefix to load values from environment variables:

```json
{
  "Username": "{env}WEBDAV_USER",
  "Password": "{env}WEBDAV_PASS"
}
```

### Access Rules

Define fine-grained access control with rules:

```json
{
  "Rules": [
    {
      "Path": "/public",
      "Permissions": "R"
    },
    {
      "Regex": "^/private/.*\\.txt$",
      "Permissions": "CRUD"
    }
  ]
}
```

### CORS Configuration

Enable CORS for web-based access:

```json
{
  "Cors": {
    "Enabled": true,
    "Credentials": true,
    "AllowedHosts": ["https://example.com"],
    "AllowedMethods": ["GET", "PUT", "DELETE"],
    "AllowedHeaders": ["*"],
    "ExposedHeaders": ["DAV"]
  }
}
```

## Running with Docker

Build the Docker image:

```bash
docker build -t webdav-dotnet -f webdav/Dockerfile .
```

Run the container:

```bash
docker run -p 6065:6065 \
  -v $(pwd)/data:/data \
  -v $(pwd)/appsettings.json:/app/appsettings.json:ro \
  webdav-dotnet
```

## Development

For development, use the included `appsettings.Development.json`:

```bash
dotnet run --environment Development
```

Default development credentials:
- Username: `admin`
- Password: `admin`
- Permissions: Full access (CRUD)

## Connecting to WebDAV

**Important**: Use the `/dav` path prefix when connecting WebDAV clients.

### Windows

Map network drive: `\\localhost@6065\DavWWWRoot\dav\`

Or in File Explorer: `http://localhost:6065/dav`

### macOS

Finder → Go → Connect to Server: `http://localhost:6065/dav`

### Linux

```bash
# Mount with davfs2
sudo mount -t davfs http://localhost:6065/dav /mnt/webdav

# Or use cadaver
cadaver http://localhost:6065/dav
```

### Web UI

Access the Blazor file manager interface at: `http://localhost:6065/`

## Security Notes

⚠️ **Important Security Recommendations:**

1. Always use BCrypt passwords in production
2. Enable TLS for production deployments
3. Never use `NoPassword: true` in production
4. Use strong, unique passwords
5. Consider running behind a reverse proxy (nginx, Apache)
6. Regularly update dependencies

## License

This project is inspired by [hacdias/webdav](https://github.com/hacdias/webdav).

## Comparison with Go Version

This C# implementation provides feature parity with the original Go version:

| Feature | Go | C# |
|---------|----|----|
| Basic Auth | ✅ | ✅ |
| BCrypt Passwords | ✅ | ✅ |
| Multiple Users | ✅ | ✅ |
| Permission Control | ✅ | ✅ |
| Path/Regex Rules | ✅ | ✅ |
| CORS | ✅ | ✅ |
| TLS | ✅ | ✅ |
| Environment Variables | ✅ | ✅ |

## Troubleshooting

### Port already in use
Change the port in `appsettings.json`:
```json
"Port": 8080
```

### Permission denied
Ensure the application has read/write access to the configured directory.

### Cannot connect
Check firewall settings and ensure the correct address/port configuration.
