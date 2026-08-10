using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NWebDav.Server.Props;

namespace NWebDav.Server.Stores;

public sealed class MultiRootDiskStoreOptions
{
    public required IReadOnlyList<DiskStoreRootPoint> RootPoints { get; set; }
    public bool IsWritable { get; set; } = true;
}

public sealed class DiskStoreRootPoint
{
    public required string Path { get; set; }
    public required string Directory { get; set; }
}

public sealed class MultiRootDiskStore : IStore
{
    private static readonly XElement s_xDavCollection = new(WebDavNamespaces.DavNs + "collection");
    private readonly IReadOnlyDictionary<string, DiskStore> _stores;
    private readonly bool _isWritable;
    private readonly ILogger<MultiRootDiskStore> _logger;

    public MultiRootDiskStore(
        MultiRootDiskStoreOptions options,
        DiskStoreCollectionPropertyManager collectionPropertyManager,
        DiskStoreItemPropertyManager itemPropertyManager,
        ILoggerFactory loggerFactory)
    {
        _isWritable = options.IsWritable;
        _logger = loggerFactory.CreateLogger<MultiRootDiskStore>();
        _stores = options.RootPoints.ToDictionary(
            point => NormalizePointPath(point.Path),
            point => new DiskStore(point.Directory, options.IsWritable, collectionPropertyManager, itemPropertyManager, loggerFactory),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<IStoreItem?> GetItemAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = SplitPath(path);
        if (parts.Length == 0)
            return Task.FromResult<IStoreItem?>(new MultiRootCollection(this, _stores));

        if (!_stores.TryGetValue(parts[0], out var store))
            return Task.FromResult<IStoreItem?>(null);

        return store.GetItemAsync(JoinPath(parts.Skip(1)), cancellationToken)
            .ContinueWith(task => parts.Length == 1 ? Wrap(task.Result, parts[0]) : task.Result, cancellationToken);
    }

    public Task<IStoreCollection?> GetCollectionAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = SplitPath(path);
        if (parts.Length == 0)
            return Task.FromResult<IStoreCollection?>(new MultiRootCollection(this, _stores));

        if (!_stores.TryGetValue(parts[0], out var store))
            return Task.FromResult<IStoreCollection?>(null);

        return GetCollectionAsync(store, JoinPath(parts.Skip(1)), parts.Length == 1 ? parts[0] : null, cancellationToken);
    }

    private static async Task<IStoreCollection?> GetCollectionAsync(DiskStore store, string path, string? point, CancellationToken cancellationToken)
    {
        var collection = await store.GetCollectionAsync(path, cancellationToken).ConfigureAwait(false);
        return collection == null ? null : point == null ? collection : new MountedCollection(collection, point);
    }

    private static IStoreItem? Wrap(IStoreItem? item, string point) =>
        item is IStoreCollection collection ? new MountedCollection(collection, point) : item;

    private static string[] SplitPath(string path) =>
        path.Trim('/').Split('/', System.StringSplitOptions.RemoveEmptyEntries);

    private static string JoinPath(IEnumerable<string> parts) => "/" + string.Join('/', parts);

    private static string NormalizePointPath(string path) => path.Trim('/');

    private sealed class MultiRootCollection : IStoreCollection
    {
        private readonly MultiRootDiskStore _store;
        private readonly IReadOnlyDictionary<string, DiskStore> _stores;

        public MultiRootCollection(MultiRootDiskStore store, IReadOnlyDictionary<string, DiskStore> stores)
        {
            _store = store;
            _stores = stores;
            PropertyManager = new PropertyManager<MultiRootCollection>(GetProperties());
        }

        private static DavProperty<MultiRootCollection>[] GetProperties() =>
        [
            new DavDisplayName<MultiRootCollection> { Getter = _ => "/" },
            new DavGetResourceType<MultiRootCollection> { Getter = _ => [s_xDavCollection] },
            new DavExtCollectionChildCount<MultiRootCollection> { Getter = collection => collection._stores.Count },
            new DavExtCollectionIsFolder<MultiRootCollection> { Getter = _ => true },
            new DavExtCollectionIsHidden<MultiRootCollection> { Getter = _ => false },
            new DavExtCollectionIsStructuredDocument<MultiRootCollection> { Getter = _ => false },
            new DavExtCollectionHasSubs<MultiRootCollection> { Getter = collection => collection._stores.Count > 0 },
            new DavExtCollectionNoSubs<MultiRootCollection> { Getter = collection => collection._stores.Count == 0 },
            new DavExtCollectionObjectCount<MultiRootCollection> { Getter = _ => 0 },
            new DavExtCollectionReserved<MultiRootCollection> { Getter = _ => true },
            new DavExtCollectionVisibleCount<MultiRootCollection> { Getter = collection => collection._stores.Count }
        ];

        public string Name => string.Empty;
        public string UniqueKey => "multi-root";
        public IPropertyManager PropertyManager { get; }
        public InfiniteDepthMode InfiniteDepthMode => InfiniteDepthMode.Rejected;
        public Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<DavStatusCode> UploadFromStreamAsync(Stream source, CancellationToken cancellationToken) => Task.FromResult(DavStatusCode.Conflict);
        public Task<StoreItemResult> CopyAsync(IStoreCollection destination, string name, bool overwrite, CancellationToken cancellationToken) =>
            Task.FromResult(new StoreItemResult(DavStatusCode.Conflict));

        public Task<IStoreItem?> GetItemAsync(string name, CancellationToken cancellationToken) =>
            _store.GetItemAsync("/" + name, cancellationToken);

        public async IAsyncEnumerable<IStoreItem> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var pair in _stores)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var collection = await pair.Value.GetCollectionAsync("/", cancellationToken).ConfigureAwait(false);
                if (collection != null)
                    yield return new MountedCollection(collection, pair.Key);
            }
        }

        public Task<StoreItemResult> CreateItemAsync(string name, Stream stream, bool overwrite, CancellationToken cancellationToken) =>
            Task.FromResult(new StoreItemResult(DavStatusCode.Conflict));

        public Task<StoreCollectionResult> CreateCollectionAsync(string name, bool overwrite, CancellationToken cancellationToken) =>
            Task.FromResult(new StoreCollectionResult(DavStatusCode.Conflict));

        public bool SupportsFastMove(IStoreCollection destination, string destinationName, bool overwrite) => false;
        public Task<StoreItemResult> MoveItemAsync(string sourceName, IStoreCollection destination, string destinationName, bool overwrite, CancellationToken cancellationToken) =>
            Task.FromResult(new StoreItemResult(DavStatusCode.Conflict));
        public Task<DavStatusCode> DeleteItemAsync(string name, CancellationToken cancellationToken) => Task.FromResult(DavStatusCode.Conflict);
    }

    private sealed class MountedCollection : IStoreCollection
    {
        private readonly IStoreCollection _inner;
        private readonly string _point;

        public MountedCollection(IStoreCollection inner, string point)
        {
            _inner = inner;
            _point = point;
            PropertyManager = inner.PropertyManager == null
                ? null
                : new ForwardingPropertyManager(inner, inner.PropertyManager);
        }

        public string Name => _point == string.Empty ? _inner.Name : _point;
        public string UniqueKey => $"{_point}:{_inner.UniqueKey}";
        public IPropertyManager? PropertyManager { get; }
        public InfiniteDepthMode InfiniteDepthMode => _inner.InfiniteDepthMode;
        public Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken) => _inner.GetReadableStreamAsync(cancellationToken);
        public Task<DavStatusCode> UploadFromStreamAsync(Stream source, CancellationToken cancellationToken) => _inner.UploadFromStreamAsync(source, cancellationToken);
        public Task<StoreItemResult> CopyAsync(IStoreCollection destination, string name, bool overwrite, CancellationToken cancellationToken) =>
            destination is MountedCollection mounted
                ? _inner.CopyAsync(mounted._inner, name, overwrite, cancellationToken)
                : Task.FromResult(new StoreItemResult(DavStatusCode.Conflict));
        public Task<IStoreItem?> GetItemAsync(string name, CancellationToken cancellationToken) => _inner.GetItemAsync(name, cancellationToken);
        public IAsyncEnumerable<IStoreItem> GetItemsAsync(CancellationToken cancellationToken) => _inner.GetItemsAsync(cancellationToken);
        public Task<StoreItemResult> CreateItemAsync(string name, Stream stream, bool overwrite, CancellationToken cancellationToken) => _inner.CreateItemAsync(name, stream, overwrite, cancellationToken);
        public Task<StoreCollectionResult> CreateCollectionAsync(string name, bool overwrite, CancellationToken cancellationToken) => _inner.CreateCollectionAsync(name, overwrite, cancellationToken);
        public bool SupportsFastMove(IStoreCollection destination, string destinationName, bool overwrite) =>
            destination is MountedCollection mounted &&
            string.Equals(mounted._point, _point, System.StringComparison.OrdinalIgnoreCase) &&
            _inner.SupportsFastMove(mounted._inner, destinationName, overwrite);
        public Task<StoreItemResult> MoveItemAsync(string sourceName, IStoreCollection destination, string destinationName, bool overwrite, CancellationToken cancellationToken) =>
            destination is MountedCollection mounted
                ? _inner.MoveItemAsync(sourceName, mounted._inner, destinationName, overwrite, cancellationToken)
                : Task.FromResult(new StoreItemResult(DavStatusCode.Conflict));
        public Task<DavStatusCode> DeleteItemAsync(string name, CancellationToken cancellationToken) => _inner.DeleteItemAsync(name, cancellationToken);
    }

    private sealed class ForwardingPropertyManager : IPropertyManager
    {
        private readonly IStoreItem _innerItem;
        private readonly IPropertyManager _innerManager;

        public ForwardingPropertyManager(IStoreItem innerItem, IPropertyManager innerManager)
        {
            _innerItem = innerItem;
            _innerManager = innerManager;
        }

        public IList<PropertyInfo> Properties => _innerManager.Properties;

        public Task<object?> GetPropertyAsync(IStoreItem item, XName propertyName, bool skipExpensive = false, CancellationToken cancellationToken = default) =>
            _innerManager.GetPropertyAsync(_innerItem, propertyName, skipExpensive, cancellationToken);

        public Task<DavStatusCode> SetPropertyAsync(IStoreItem item, XName propertyName, object value, CancellationToken cancellationToken) =>
            _innerManager.SetPropertyAsync(_innerItem, propertyName, value, cancellationToken);
    }

    private sealed class DiskStore : DiskStoreBase
    {
        private readonly string _baseDirectory;
        private readonly bool _isWritable;

        public DiskStore(string baseDirectory, bool isWritable, DiskStoreCollectionPropertyManager collectionPropertyManager, DiskStoreItemPropertyManager itemPropertyManager, ILoggerFactory loggerFactory)
            : base(collectionPropertyManager, itemPropertyManager, loggerFactory)
        {
            _baseDirectory = Path.GetFullPath(baseDirectory);
            _isWritable = isWritable;
            Directory.CreateDirectory(_baseDirectory);
        }

        public override bool IsWritable => _isWritable;
        public override string BaseDirectory => _baseDirectory;
    }
}
