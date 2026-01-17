# Quick Start Guide

## Running the WebDAV Server

### 1. Development Mode

```bash
cd webdav
dotnet run
```

The server will start on `http://localhost:6065` with:
- **Blazor UI**: `http://localhost:6065/` - Web file manager
- **WebDAV endpoint**: `http://localhost:6065/dav` - For mounting as network drive
- Username: `admin`
- Password: `admin`
- Permissions: Full access (CRUD)
- Directory: `./data`

### 2. Production Mode

Update `appsettings.json` with your configuration:

```json
{
  "WebDav": {
    "Port": 6065,
    "Prefix": "/dav",
    "Directory": "/var/webdav/data",
    "Users": [
      {
        "Username": "myuser",
        "Password": "{bcrypt}$2a$10$YOUR_BCRYPT_HASH",
        "Permissions": "CRUD"
      }
    ]
  }
}
```

Run:
```bash
dotnet run --environment Production
```

### 3. Testing the Server

**Note**: All WebDAV operations use the `/dav` prefix.

#### Windows Command Line:
```cmd
curl -u admin:admin http://localhost:6065/dav/
```

#### PowerShell:
```powershell
$credential = Get-Credential -UserName admin -Message "Enter password"
Invoke-WebRequest -Uri http://localhost:6065/dav/ -Method OPTIONS -Credential $credential
```

#### Upload a file:
```bash
curl -u admin:admin -T myfile.txt http://localhost:6065/dav/myfile.txt
```

#### Download a file:
```bash
curl -u admin:admin http://localhost:6065/dav/myfile.txt -o downloaded.txt
```

#### Create a directory:
```bash
curl -u admin:admin -X MKCOL http://localhost:6065/dav/newfolder
```

#### List directory (PROPFIND):
```bash
curl -u admin:admin -X PROPFIND http://localhost:6065/dav/
```

### 4. Mount as Network Drive

**Important**: Use the `/dav` path when mounting.

#### Windows:
1. Open File Explorer
2. Right-click "This PC" → "Map network drive"
3. Enter: `http://localhost:6065/dav`
4. Check "Connect using different credentials"
5. Enter username: `admin`, password: `admin`

Or use command line:
```cmd
net use Z: http://localhost:6065/dav /user:admin admin
```

#### macOS:
1. Finder → Go → Connect to Server (Cmd+K)
2. Enter: `http://localhost:6065/dav`
3. Click Connect
4. Enter credentials

Or use command line:
```bash
mkdir ~/webdav
mount_webdav -S -i http://localhost:6065/dav ~/webdav
```

#### Linux:
```bash
# Install davfs2
sudo apt-get install davfs2

# Create mount point
sudo mkdir -p /mnt/webdav

# Mount
sudo mount -t davfs http://localhost:6065/dav /mnt/webdav
# Enter username: admin
# Enter password: admin
```

### 5. Using the Web UI

Open your browser and navigate to: `http://localhost:6065/`

The Blazor-based file manager provides a graphical interface to:
- Browse files and directories
- Upload and download files
- Create new folders
- Delete files and folders
- All operations respect the same authentication and permissions as WebDAV

### 6. Generating BCrypt Passwords

Using PowerShell with BCrypt.Net:
```powershell
dotnet tool install --global dotnet-script
dotnet script eval "Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(\"yourpassword\"))"
```

Or use an online tool: https://bcrypt-generator.com/

Then use in config:
```json
"Password": "{bcrypt}$2a$10$YOUR_GENERATED_HASH"
```

### 7. Environment Variables

For sensitive data, use environment variables:

```json
{
  "Users": [
    {
      "Username": "{env}WEBDAV_USER",
      "Password": "{env}WEBDAV_PASS"
    }
  ]
}
```

Set variables:
```bash
# Linux/macOS
export WEBDAV_USER=admin
export WEBDAV_PASS=secretpassword

# Windows PowerShell
$env:WEBDAV_USER="admin"
$env:WEBDAV_PASS="secretpassword"

# Windows CMD
set WEBDAV_USER=admin
set WEBDAV_PASS=secretpassword
```

### 8. Common Issues

#### Port in use:
```
Error: Address already in use
```
Change the port in `appsettings.json` or `appsettings.Development.json`

#### Permission denied:
```
Error: Access to the path is denied
```
Ensure the application has permissions to read/write the configured directory.

#### Connection refused:
- Check if firewall is blocking the port
- Verify the server is running: `netstat -an | findstr 6065` (Windows) or `netstat -an | grep 6065` (Linux)

### 9. Performance Tips

- Use SSDs for better I/O performance
- Increase file upload limits in `appsettings.json`:
  ```json
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 524288000
    }
  }
  ```
- Enable response compression for large files
- Use reverse proxy (nginx, Apache) for production deployments

### 10. Security Checklist

- [ ] Use BCrypt passwords
- [ ] Enable TLS/HTTPS
- [ ] Set `NoPassword: false`
- [ ] Use strong passwords
- [ ] Limit permissions (avoid CRUD for read-only users)
- [ ] Run behind reverse proxy
- [ ] Enable firewall rules
- [ ] Regular security updates
- [ ] Monitor access logs
- [ ] Use environment variables for secrets
