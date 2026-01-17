# WebDAV Server - Project Summary

## Project Structure

```
webdav/
├── Handlers/
│   └── WebDavRequestHandler.cs       # Core WebDAV request handler
├── Middleware/
│   ├── WebDavAuthenticationMiddleware.cs  # Basic auth middleware
│   └── WebDavPermissionMiddleware.cs      # Permission checking
├── Models/
│   └── WebDavConfig.cs              # Configuration models
├── Services/
│   ├── PermissionService.cs         # Permission logic (CRUD)
│   └── UserService.cs               # User management & BCrypt
├── Program.cs                       # Application entry point
├── appsettings.json                 # Production configuration
├── appsettings.Development.json     # Development configuration
├── webdav.csproj                    # Project file
├── Dockerfile                       # Docker container definition
├── README.md                        # Full documentation
├── QUICKSTART.md                    # Quick start guide
├── config.example.yml               # YAML config example
└── .gitignore                       # Git ignore rules
```

## Key Components

### 1. **Program.cs**
- Application bootstrap and configuration
- Middleware pipeline setup
- Kestrel web server configuration
- Logging setup
- Route mapping for WebDAV methods

### 2. **WebDavAuthenticationMiddleware**
- HTTP Basic Authentication
- User credential validation
- BCrypt password support
- X-Forwarded-For proxy support
- Stores authenticated user in HttpContext

### 3. **WebDavPermissionMiddleware**
- Path-based permission checking
- Method-based access control (GET, PUT, DELETE, etc.)
- Rule evaluation (path and regex)
- File existence checking

### 4. **WebDavRequestHandler**
- NWebDav.Server integration
- Per-user directory isolation
- GET→PROPFIND conversion for directories
- WebDAV protocol dispatch

### 5. **PermissionService**
- CRUD permission model (Create, Read, Update, Delete)
- Rule matching (path prefix & regex)
- Method→Permission mapping
- Destination checking for COPY/MOVE

### 6. **UserService**
- User credential storage
- BCrypt password verification
- Environment variable resolution
- Per-user directory & permission configuration
- Rule inheritance (append/overwrite)

## Configuration Flow

```
appsettings.json
    ↓
WebDavConfig
    ↓
UserService (builds user list with permissions)
    ↓
Program.cs (sets up middleware)
    ↓
Request Pipeline:
    1. Authentication Middleware (validates user)
    2. Permission Middleware (checks access)
    3. WebDavRequestHandler (processes WebDAV request)
```

## Supported WebDAV Methods

| Method    | Purpose                          | Required Permission |
|-----------|----------------------------------|---------------------|
| GET       | Download file/list directory     | Read (R)            |
| HEAD      | Get headers only                 | Read (R)            |
| PUT       | Upload/update file               | Create/Update (C/U) |
| DELETE    | Delete file/directory            | Delete (D)          |
| PROPFIND  | List properties/directory        | Read (R)            |
| PROPPATCH | Modify properties                | Update (U)          |
| MKCOL     | Create directory                 | Create (C)          |
| COPY      | Copy file/directory              | Read + Create (RC)  |
| MOVE      | Move/rename file/directory       | Read + Update (RU)  |
| LOCK      | Lock resource                    | Update (U)          |
| UNLOCK    | Unlock resource                  | Update (U)          |
| OPTIONS   | Get server capabilities          | Read (R)            |

## Permission System

### Permission Characters
- **C** - Create (PUT new, POST, MKCOL)
- **R** - Read (GET, HEAD, PROPFIND, OPTIONS)
- **U** - Update (PUT existing, PATCH, PROPPATCH)
- **D** - Delete (DELETE)

### Rule Evaluation
1. Check destination (for COPY/MOVE)
2. Check source path against rules (last match wins)
3. Fall back to default permissions if no rule matches

### Example Rules
```json
{
  "Permissions": "R",  // Default: read-only
  "Rules": [
    {
      "Path": "/public",
      "Permissions": "R"
    },
    {
      "Path": "/uploads",
      "Permissions": "CRUD"
    },
    {
      "Regex": "^/private/.*\\.txt$",
      "Permissions": "RU"
    }
  ]
}
```

## Dependencies

| Package                       | Version | Purpose                           |
|------------------------------|---------|-----------------------------------|
| NWebDav.Server.AspNetCore    | 0.1.36  | WebDAV protocol implementation    |
| BCrypt.Net-Next              | 4.0.3   | Password hashing & verification   |

## Feature Parity with Go Version

✅ **Implemented:**
- Basic authentication
- BCrypt password support
- Multiple users with individual directories
- CRUD permission system
- Path & regex-based rules
- Rule inheritance (append/overwrite)
- Environment variable support ({env})
- CORS configuration
- TLS/HTTPS support
- Proxy awareness (X-Forwarded-For)
- Debug logging
- Configurable prefix
- GET→PROPFIND conversion for directories

## Extension Points

### Adding Custom Authentication
Implement a new middleware before `UseWebDavAuthentication`:
```csharp
app.UseMiddleware<MyCustomAuthMiddleware>();
app.UseWebDavAuthentication(userService, behindProxy);
```

### Custom Permission Logic
Extend `PermissionService.UserPermissions`:
```csharp
public class CustomUserPermissions : PermissionService.UserPermissions
{
    public override bool IsAllowed(string method, string path, ...) 
    {
        // Custom logic
        return base.IsAllowed(method, path, ...);
    }
}
```

### Custom Storage Backend
Replace `DiskStore` with a custom `IStore` implementation:
```csharp
public class S3Store : IStore
{
    // Implement WebDAV operations against S3
}
```

## Testing Checklist

- [x] Project builds successfully
- [ ] Server starts and listens on configured port
- [ ] Anonymous access denied when users configured
- [ ] Basic auth works with correct credentials
- [ ] Basic auth rejects wrong credentials
- [ ] BCrypt passwords work
- [ ] File upload (PUT)
- [ ] File download (GET)
- [ ] Directory listing (PROPFIND)
- [ ] Directory creation (MKCOL)
- [ ] File deletion (DELETE)
- [ ] File copy (COPY)
- [ ] File move (MOVE)
- [ ] Permission enforcement (read-only user cannot write)
- [ ] Rule-based permissions work
- [ ] Regex rules work
- [ ] Per-user directories isolated
- [ ] Environment variables resolved
- [ ] CORS headers present (when enabled)
- [ ] TLS works (when configured)

## Deployment Options

### 1. Standalone
```bash
dotnet publish -c Release
cd bin/Release/net9.0/publish
./webdav
```

### 2. Docker
```bash
docker build -t webdav .
docker run -p 6065:6065 -v $(pwd)/data:/data webdav
```

### 3. Systemd Service (Linux)
Create `/etc/systemd/system/webdav.service`:
```ini
[Unit]
Description=WebDAV Server
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/webdav
ExecStart=/usr/bin/dotnet /opt/webdav/webdav.dll
Restart=always
User=webdav

[Install]
WantedBy=multi-user.target
```

### 4. Windows Service
Use NSSM or create a Windows Service wrapper.

### 5. Reverse Proxy (nginx)
```nginx
location / {
    proxy_pass http://localhost:6065;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header Host $host;
    client_max_body_size 500M;
}
```

## Performance Characteristics

- **Throughput**: Limited by disk I/O and network bandwidth
- **Concurrency**: ASP.NET Core handles thousands of concurrent connections
- **Memory**: Scales with number of concurrent requests (~10-50MB baseline)
- **CPU**: Minimal for file operations, BCrypt hashing is CPU-intensive

## Security Considerations

1. **Authentication**: BCrypt work factor = 10 (adjustable)
2. **TLS**: Supports TLS 1.2+ when configured
3. **Path Traversal**: Protected by .NET path sanitization
4. **Injection**: No SQL/command injection risk (file-based)
5. **DoS**: Consider rate limiting in reverse proxy
6. **Secrets**: Use environment variables or Azure Key Vault

## Future Enhancements

Potential improvements:
- [ ] Database-backed user storage
- [ ] JWT token authentication
- [ ] WebSocket support for real-time updates
- [ ] S3/Azure Blob storage backend
- [ ] Rate limiting middleware
- [ ] Audit logging
- [ ] Quota management
- [ ] File versioning
- [ ] Web UI for administration
- [ ] Multi-factor authentication
- [ ] LDAP/Active Directory integration

## Related Projects

- Original Go implementation: https://github.com/hacdias/webdav
- NWebDav library: https://github.com/ramondeklein/nwebdav

## Architecture

### Request Routing

The application uses ASP.NET Core's routing to separate concerns:

1. **Blazor UI** (`/`): Serves the web-based file manager interface
   - Uses Blazor Server with SignalR for real-time updates
   - Provides a user-friendly GUI for file operations
   - Mapped via `MapBlazorHub()` and `MapFallbackToPage("/_Host")`

2. **WebDAV Endpoint** (`/dav`): Provides WebDAV protocol access
   - Handles WebDAV methods (PROPFIND, MKCOL, PUT, DELETE, etc.)
   - Supports mounting as network drive
   - Isolated using `app.Map(webDavConfig.Prefix, ...)`
   - Authentication and permission middleware applied only to this path

This separation ensures:
- GET requests to `/` serve the Blazor UI
- WebDAV operations to `/dav` are handled by NWebDav
- No routing conflicts between UI and WebDAV protocol

### Middleware Pipeline

The middleware pipeline is organized as follows:

```
Request
  ↓
CORS (if enabled)
  ↓
Static Files (CSS, JS)
  ↓
Routing
  ↓
├─ Blazor Routes (/, /filemanager)
│    ↓
│    Blazor Hub → Razor Components
│
└─ WebDAV Routes (/dav/*)
     ↓
     WebDav Authentication
     ↓
     WebDav Permissions
     ↓
     NWebDav Handler
