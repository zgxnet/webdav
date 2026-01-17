using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NWebDav.Server.Helpers;

namespace NWebDav.Server.Stores;

public abstract class DiskStoreBase : IStore
{
    private readonly DiskStoreCollectionPropertyManager _diskStoreCollectionPropertyManager;
    private readonly DiskStoreItemPropertyManager _diskStoreItemPropertyManager;
    private readonly ILoggerFactory _loggerFactory;

    protected DiskStoreBase(DiskStoreCollectionPropertyManager diskStoreCollectionPropertyManager, DiskStoreItemPropertyManager diskStoreItemPropertyManager, ILoggerFactory loggerFactory)
    {
        _diskStoreCollectionPropertyManager = diskStoreCollectionPropertyManager;
        _diskStoreItemPropertyManager = diskStoreItemPropertyManager;
        _loggerFactory = loggerFactory;
    }

    public abstract bool IsWritable { get; }
    public abstract string BaseDirectory { get; }

    public Task<IStoreItem?> GetItemAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
            
        var fullPath = GetFullPathFromRequestPath(path);
        var item = CreateFromPath(fullPath);
        return Task.FromResult(item);
    }

    public Task<IStoreCollection?> GetCollectionAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
            
        // Determine the full path from the request path
        var fullPath = GetFullPathFromRequestPath(path);
        if (!Directory.Exists(fullPath))
            return Task.FromResult<IStoreCollection?>(null);

        // Return the item
        return Task.FromResult<IStoreCollection?>(CreateCollection(new DirectoryInfo(fullPath)));
    }

    private string GetFullPathFromRequestPath(string requestPath)
    {
        // Remove leading slash and convert to system path separators
        var relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        // Determine the full path
        var fullPath = string.IsNullOrEmpty(relativePath) 
            ? BaseDirectory 
            : Path.GetFullPath(Path.Combine(BaseDirectory, relativePath));

        // Make sure we're still inside the specified directory
        if (fullPath != BaseDirectory && !fullPath.StartsWith(BaseDirectory + Path.DirectorySeparatorChar))
            throw new SecurityException($"Path '{requestPath}' is outside the '{BaseDirectory}' directory.");

        // Return the combined path
        return fullPath;
    }

    internal IStoreItem? CreateFromPath(string path)
    {
        // Check if it's a directory
        if (Directory.Exists(path))
            return CreateCollection(new DirectoryInfo(path));

        // Check if it's a file
        if (File.Exists(path))
            return CreateItem(new FileInfo(path));

        // The item doesn't exist
        return null;
    }

    internal DiskStoreCollection CreateCollection(DirectoryInfo directoryInfo) =>
        new(this, _diskStoreCollectionPropertyManager, directoryInfo, _loggerFactory.CreateLogger<DiskStoreCollection>());

    internal DiskStoreItem CreateItem(FileInfo file) =>
        new(this, _diskStoreItemPropertyManager, file, _loggerFactory.CreateLogger<DiskStoreItem>());
}