using System.Security;
using WebDav.Models;

namespace WebDav.Services;

public class FileManagerService
{
    private readonly WebDavConfig _config;
    private readonly ILogger<FileManagerService> _logger;

    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }

    public FileManagerService(WebDavConfig config, ILogger<FileManagerService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<FileItem>> GetFilesAsync(string relativePath = "")
    {
        try
        {
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
            var fullPath = GetFullPath(Path.Combine(relativePath, directoryName));
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
            var fullPath = GetFullPath(Path.Combine(relativePath, fileName));
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
            var rootDirectory = Path.GetFullPath(_config.Directory);
            if (string.IsNullOrEmpty(relativePath))
                return rootDirectory;

            var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            
            // Security check: ensure the path is within the root directory
            if (!fullPath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Access to path '{relativePath}' is denied");
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving full path for: {Path}", relativePath);
            throw;
        }
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
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico"
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

    public async Task<byte[]?> ReadFileAsBytesAsync(string relativePath, int maxSizeMB = 10)
    {
        try
        {
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
            _ => "application/octet-stream"
        };
    }

    public async Task<string?> ReadFileContentAsync(string relativePath, int maxSizeKB = 500)
    {
        try
        {
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
