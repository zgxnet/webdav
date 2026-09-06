using System.Security;
using SkiaSharp;
using WebDav.Models;

namespace WebDav.Services;

public class FileManagerService
{
    private const long ThumbnailSourceLimit = 100 * 1024;
    private const int ThumbnailMaxDimension = 240;
    private readonly WebDavConfig _config;
    private readonly ILogger<FileManagerService> _logger;
    private readonly UserService _userService;
    private UserService.UserInfo? _currentUser;

    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }

    public FileManagerService(
        WebDavConfig config,
        UserService userService,
        ILogger<FileManagerService> logger)
    {
        _config = config;
        _userService = userService;
        _logger = logger;
    }

    public void SetCurrentUser(string? username)
    {
        if (!_userService.HasUsers)
        {
            _currentUser = null;
            return;
        }

        _currentUser = string.IsNullOrWhiteSpace(username)
            ? null
            : _userService.GetUser(username);

        if (_currentUser == null)
            throw new SecurityException("The current user could not be authenticated");
    }

    public bool CanCreateIn(string relativePath)
    {
        return IsAllowed("MKCOL", Path.Combine(relativePath, ".permission-check"), fileExists: false);
    }

    public bool CanUploadTo(string relativePath)
    {
        var permissionPath = Path.Combine(relativePath, ".permission-check");
        return IsAllowed("PUT", permissionPath, fileExists: false) ||
               IsAllowed("PUT", permissionPath, fileExists: true);
    }

    public bool CanDelete(string relativePath)
    {
        return !IsRootPoint(relativePath) && IsAllowed("DELETE", relativePath);
    }

    public async Task<List<FileItem>> GetFilesAsync(string relativePath = "")
    {
        try
        {
            EnsureAllowed("PROPFIND", relativePath);

            if (UsesRootPoints && string.IsNullOrWhiteSpace(relativePath))
            {
                return _config.RootPoints
                    .Where(point => !_userService.HasUsers || point.IsUserAllowed(_currentUser?.Username))
                    .Select(point =>
                    {
                        var directory = new DirectoryInfo(Path.GetFullPath(point.Directory));
                        return new FileItem
                        {
                            Name = point.Path.Trim('/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            Path = point.Path.Trim('/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            IsDirectory = true,
                            LastModified = directory.Exists ? directory.LastWriteTime : DateTime.MinValue
                        };
                    })
                    .OrderBy(point => point.Name)
                    .ToList();
            }

            var fullPath = GetFullPath(relativePath);
            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("Directory does not exist: {Path}", fullPath);
                return new List<FileItem>();
            }

            var items = new List<FileItem>();
            
            // Add directories
            var directories = Directory.GetDirectories(fullPath);
            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                items.Add(new FileItem
                {
                    Name = dirInfo.Name,
                    Path = Path.Combine(relativePath, dirInfo.Name),
                    IsDirectory = true,
                    LastModified = dirInfo.LastWriteTime
                });
            }

            // Add files
            var files = Directory.GetFiles(fullPath);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                items.Add(new FileItem
                {
                    Name = fileInfo.Name,
                    Path = Path.Combine(relativePath, fileInfo.Name),
                    IsDirectory = false,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                });
            }

            return await Task.FromResult(items.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting files from path: {Path}", relativePath);
            throw;
        }
    }

    public async Task<bool> CreateDirectoryAsync(string relativePath, string directoryName)
    {
        try
        {
            var targetPath = Path.Combine(relativePath, directoryName);
            EnsureAllowed("MKCOL", targetPath);
            var fullPath = GetFullPath(targetPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                _logger.LogInformation("Directory created: {Path}", fullPath);
                return await Task.FromResult(true);
            }
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating directory: {Path}", relativePath);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string relativePath)
    {
        try
        {
            EnsureAllowed("DELETE", relativePath);

            if (IsRootPoint(relativePath))
                throw new SecurityException("Deleting a configured root point is not allowed");

            var fullPath = GetFullPath(relativePath);
            
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
                _logger.LogInformation("Directory deleted: {Path}", fullPath);
                return await Task.FromResult(true);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("File deleted: {Path}", fullPath);
                return await Task.FromResult(true);
            }
            
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting: {Path}", relativePath);
            throw;
        }
    }

    public async Task<bool> UploadFileAsync(string relativePath, string fileName, Stream fileStream)
    {
        try
        {
            var targetPath = Path.Combine(relativePath, fileName);
            EnsureAllowed("PUT", targetPath);
            var fullPath = GetFullPath(targetPath);
            var directory = Path.GetDirectoryName(fullPath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStreamOut = File.Create(fullPath);
            await fileStream.CopyToAsync(fileStreamOut);
            
            _logger.LogInformation("File uploaded: {Path}", fullPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {Path}/{FileName}", relativePath, fileName);
            throw;
        }
    }

    public async Task<Stream?> DownloadFileAsync(string relativePath)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            
            if (File.Exists(fullPath))
            {
                var memoryStream = new MemoryStream();
                using var fileStream = File.OpenRead(fullPath);
                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {Path}", relativePath);
            throw;
        }
    }

    public string GetFullPath(string relativePath)
    {
        try
        {
            if (_currentUser is { UsesRootPoints: false })
                return ResolvePathUnderRoot(_currentUser.Directory, relativePath);

            EnsureRootPointAccess(relativePath);
            return _config.ResolvePath(relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving full path for: {Path}", relativePath);
            throw;
        }
    }

    private void EnsureRootPointAccess(string relativePath)
    {
        if (!_userService.HasUsers || string.IsNullOrWhiteSpace(relativePath))
            return;

        var point = _config.FindRootPoint(relativePath);
        if (point != null && !point.IsUserAllowed(_currentUser?.Username))
        {
            _logger.LogWarning(
                "File manager root point access denied: User={User}, Path={Path}",
                _currentUser?.Username ?? "(unknown)",
                relativePath);
            throw new SecurityException("You do not have permission to perform this operation");
        }
    }

    private bool IsRootPoint(string relativePath)
    {
        var normalizedPath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);

        return _config.RootPoints.Any(point => string.Equals(
            point.Path.Trim('/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            normalizedPath,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool UsesRootPoints =>
        _currentUser?.UsesRootPoints ?? (_userService.HasUsers ? false : _config.RootPoints.Count > 0);

    private bool IsAllowed(string method, string relativePath, bool? fileExists = null)
    {
        if (!_userService.HasUsers)
            return true;

        if (_currentUser == null)
            return false;

        var davPath = ToDavPath(relativePath);
        return _currentUser.Permissions.IsAllowed(
            method,
            davPath,
            destination: null,
            _ => fileExists ?? FileExists(relativePath));
    }

    private void EnsureAllowed(string method, string relativePath)
    {
        if (IsAllowed(method, relativePath))
            return;

        _logger.LogWarning(
            "File manager permission denied: User={User}, Method={Method}, Path={Path}",
            _currentUser?.Username ?? "(unknown)",
            method,
            ToDavPath(relativePath));
        throw new SecurityException("You do not have permission to perform this operation");
    }

    private bool FileExists(string relativePath)
    {
        try
        {
            var fullPath = GetFullPath(relativePath);
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to check path for permission evaluation: {Path}", relativePath);
            return false;
        }
    }

    private static string ToDavPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized) ? "/" : $"/{normalized}";
    }

    private static string ResolvePathUnderRoot(string root, string relativePath)
    {
        var normalizedPath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var rootPath = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedPath));

        if (!string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Access to path '{relativePath}' is denied");
        }

        return fullPath;
    }

    public string FormatFileSize(long bytes)
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

    public bool IsPreviewable(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var previewableExtensions = new[]
        {
            ".txt", ".md", ".markdown", ".json", ".xml", ".csv", 
            ".html", ".htm", ".css", ".js", ".ts", ".cs", ".java", 
            ".py", ".rb", ".go", ".rs", ".php", ".yml", ".yaml", 
            ".ini", ".conf", ".config", ".log", ".sh", ".bat", ".ps1",
            // Image extensions
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico",
            // Documents
            ".pdf"
        };
        return previewableExtensions.Contains(extension);
    }

    public bool IsImage(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var imageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico"
        };
        return imageExtensions.Contains(extension);
    }

    public bool IsPdf(string fileName)
    {
        return string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<byte[]?> ReadFileAsBytesAsync(string relativePath, int maxSizeMB = 10)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File does not exist: {Path}", fullPath);
                return null;
            }

            var fileInfo = new FileInfo(fullPath);
            
            // Check file size limit (default 10MB for images)
            if (fileInfo.Length > maxSizeMB * 1024 * 1024)
            {
                _logger.LogWarning("File is too large: {Path} ({Size})", fullPath, FormatFileSize(fileInfo.Length));
                return null;
            }

            return await File.ReadAllBytesAsync(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file as bytes: {Path}", relativePath);
            return null;
        }
    }

    public sealed record ThumbnailResult(byte[] Content, string ContentType);

    public sealed record ImageFileResult(string FullPath, string ContentType);

    public sealed record PdfFileResult(string FullPath, string ContentType);

    public sealed record ImageInfoResult(int Width, int Height);

    public ImageFileResult? GetImageFile(string relativePath)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            return File.Exists(fullPath) && IsImage(fullPath)
                ? new ImageFileResult(fullPath, GetMimeType(fullPath))
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve image preview for: {Path}", relativePath);
            return null;
        }
    }

    public PdfFileResult? GetPdfFile(string relativePath)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            return File.Exists(fullPath) && IsPdf(fullPath)
                ? new PdfFileResult(fullPath, "application/pdf")
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve PDF preview for: {Path}", relativePath);
            return null;
        }
    }

    public ImageInfoResult? GetImageInfo(string relativePath)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (!File.Exists(fullPath) || extension is ".svg" or ".gif")
                return null;

            using var stream = File.OpenRead(fullPath);
            using var codec = SKCodec.Create(stream);
            return codec == null
                ? null
                : new ImageInfoResult(codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read image dimensions for: {Path}", relativePath);
            return null;
        }
    }

    public ThumbnailResult? CreateImagePreview(string relativePath, int maxWidth, int maxHeight)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (!File.Exists(fullPath) || !IsImage(fullPath) || extension is ".svg" or ".gif")
                return null;

            maxWidth = Math.Clamp(maxWidth, 64, 4096);
            maxHeight = Math.Clamp(maxHeight, 64, 4096);

            using var source = SKBitmap.Decode(fullPath);
            if (source == null || source.Width <= 0 || source.Height <= 0)
                return null;

            // Avoid recompressing when the original is already close to the display size.
            if (source.Width <= maxWidth * 1.25 && source.Height <= maxHeight * 1.25)
                return null;

            var scale = Math.Min((float)maxWidth / source.Width, (float)maxHeight / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var resized = ResizeImage(source, width, height);
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Webp, 82);
            return data == null ? null : new ThumbnailResult(data.ToArray(), "image/webp");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to create fitted image preview for: {Path}", relativePath);
            return null;
        }
    }

    public async Task<ThumbnailResult?> CreateThumbnailAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            if (!File.Exists(fullPath) || !IsImage(fullPath))
                return null;

            var fileInfo = new FileInfo(fullPath);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();

            // Small images and vector images need no raster thumbnail.
            if (fileInfo.Length <= ThumbnailSourceLimit || extension == ".svg")
            {
                return new ThumbnailResult(
                    await File.ReadAllBytesAsync(fullPath, cancellationToken),
                    GetMimeType(fullPath));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var source = SKBitmap.Decode(fullPath);
            if (source == null || source.Width <= 0 || source.Height <= 0)
                return null;

            var scale = Math.Min(
                1f,
                Math.Min((float)ThumbnailMaxDimension / source.Width, (float)ThumbnailMaxDimension / source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var resized = ResizeImage(source, width, height);

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Webp, 75);
            return data == null ? null : new ThumbnailResult(data.ToArray(), "image/webp");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unable to create thumbnail for: {Path}", relativePath);
            return null;
        }
    }

    private static SKBitmap ResizeImage(SKBitmap source, int width, int height)
    {
        var resized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(resized);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(
            source,
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        return resized;
    }

    public string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    public async Task<string?> ReadFileContentAsync(string relativePath, int maxSizeKB = 500)
    {
        try
        {
            EnsureAllowed("GET", relativePath);
            var fullPath = GetFullPath(relativePath);
            
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File does not exist: {Path}", fullPath);
                return null;
            }

            var fileInfo = new FileInfo(fullPath);
            
            // Check file size limit (default 500KB)
            if (fileInfo.Length > maxSizeKB * 1024)
            {
                return $"File is too large to preview ({FormatFileSize(fileInfo.Length)}). Maximum preview size is {maxSizeKB} KB.";
            }

            // Try to read as text
            var content = await File.ReadAllTextAsync(fullPath);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file content: {Path}", relativePath);
            return $"Error reading file: {ex.Message}";
        }
    }

    public string GetFileExtension(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant().TrimStart('.');
    }
}
