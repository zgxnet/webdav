using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using NWebDav.Server.Helpers;
using NWebDav.Server.Stores;

namespace NWebDav.Server.Handlers;

/// <summary>
/// Implementation of the MOVE method.
/// </summary>
/// <remarks>
/// The specification of the WebDAV MOVE method can be found in the
/// <see href="http://www.webdav.org/specs/rfc2518.html#METHOD_MOVE">
/// WebDAV specification
/// </see>.
/// </remarks>
public class MoveHandler : IRequestHandler
{
    private readonly IXmlReaderWriter _xmlReaderWriter;
    private readonly IStore _store;

    public MoveHandler(IXmlReaderWriter xmlReaderWriter, IStore store)
    {
        _xmlReaderWriter = xmlReaderWriter;
        _store = store;
    }
    
    /// <summary>
    /// Handle a MOVE request.
    /// </summary>
    /// <param name="httpContext">
    /// The HTTP context of the request.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous MOVE operation. The task
    /// will always return <see langword="true"/> upon completion.
    /// </returns>
    public async Task<bool> HandleRequestAsync(HttpContext httpContext)
    {
        // Obtain request and response
        var request = httpContext.Request;
        var response = httpContext.Response;
        
        // Get source path and split it
        var sourcePath = request.Path.Value ?? "/";
        var sourceLastSlash = sourcePath.TrimEnd('/').LastIndexOf('/');
        var sourceParentPath = sourceLastSlash > 0 ? sourcePath.Substring(0, sourceLastSlash) : "/";
        var sourceName = sourceLastSlash >= 0 ? sourcePath.Substring(sourceLastSlash + 1).TrimEnd('/') : sourcePath.TrimStart('/');

        // Obtain source collection
        var sourceCollection = await _store.GetCollectionAsync(sourceParentPath, httpContext.RequestAborted).ConfigureAwait(false);
        if (sourceCollection == null)
        {
            // Source not found
            response.SetStatus(DavStatusCode.NotFound);
            return true;
        }

        // Obtain the item to move
        var moveItem = await sourceCollection.GetItemAsync(sourceName, httpContext.RequestAborted).ConfigureAwait(false);
        if (moveItem == null)
        {
            // Source not found
            response.SetStatus(DavStatusCode.NotFound);
            return true;
        }

        // Obtain the destination
        var destinationUri = request.GetDestinationUri();
        if (destinationUri == null)
        {
            // Bad request
            response.SetStatus(DavStatusCode.BadRequest, "Destination header is missing.");
            return true;
        }

        // Get destination path
        var destinationPath = UriHelper.GetDecodedPath(destinationUri);
        
        // Strip PathBase from destination if present
        var pathBase = request.PathBase.Value ?? "";
        if (!string.IsNullOrEmpty(pathBase) && destinationPath.StartsWith(pathBase))
        {
            destinationPath = destinationPath.Substring(pathBase.Length);
        }
        if (string.IsNullOrEmpty(destinationPath))
        {
            destinationPath = "/";
        }

        // Make sure the source and destination are different
        if (sourcePath.Equals(destinationPath, StringComparison.CurrentCultureIgnoreCase))
        {
            // Forbidden
            response.SetStatus(DavStatusCode.Forbidden, "Source and destination cannot be the same.");
            return true;
        }

        // Split destination path
        var destLastSlash = destinationPath.TrimEnd('/').LastIndexOf('/');
        var destParentPath = destLastSlash > 0 ? destinationPath.Substring(0, destLastSlash) : "/";
        var destName = destLastSlash >= 0 ? destinationPath.Substring(destLastSlash + 1).TrimEnd('/') : destinationPath.TrimStart('/');

        // Obtain destination collection
        var destinationCollection = await _store.GetCollectionAsync(destParentPath, httpContext.RequestAborted).ConfigureAwait(false);
        if (destinationCollection == null)
        {
            // Source not found
            response.SetStatus(DavStatusCode.NotFound);
            return true;
        }

        // Check if the Overwrite header is set
        var overwrite = request.GetOverwrite();
        if (!overwrite)
        {
            // If overwrite is false and destination exist ==> Precondition Failed
            var destItem = await destinationCollection.GetItemAsync(destName, httpContext.RequestAborted).ConfigureAwait(false);
            if (destItem != null)
            {
                // Cannot overwrite destination item
                response.SetStatus(DavStatusCode.PreconditionFailed, "Cannot overwrite destination item.");
                return true;
            }
        }

        // Keep track of all errors
        var errors = new UriResultCollection();
        
        // Build base URI for error reporting
        var baseUri = request.GetUri();

        // Move collection
        await MoveAsync(sourceCollection, moveItem, destinationCollection, destName, overwrite, baseUri, destParentPath, errors, httpContext.RequestAborted).ConfigureAwait(false);

        // Check if there are any errors
        if (errors.HasItems)
        {
            // Obtain the status document
            var xDocument = new XDocument(errors.GetXmlMultiStatus());

            // Stream the document
            await _xmlReaderWriter.SendResponseAsync(response, DavStatusCode.MultiStatus, xDocument).ConfigureAwait(false);
        }
        else
        {
            // Set the response
            response.SetStatus(DavStatusCode.Ok);
        }

        return true;
    }

    private async Task MoveAsync(IStoreCollection sourceCollection, IStoreItem moveItem, IStoreCollection destinationCollection, string destinationName, bool overwrite, Uri baseUri, string basePath, UriResultCollection errors, CancellationToken cancellationToken)
    {
        // Determine the new paths
        var newPath = basePath.TrimEnd('/') + "/" + destinationName;
        var newUri = UriHelper.CombineWithPath(baseUri, newPath);

        // Obtain the actual item
        if (moveItem is IStoreCollection moveCollection && !moveCollection.SupportsFastMove(destinationCollection, destinationName, overwrite))
        {
            // Create a new collection
            var newCollectionResult = await destinationCollection.CreateCollectionAsync(destinationName, overwrite, cancellationToken).ConfigureAwait(false);
            if (newCollectionResult.Result != DavStatusCode.Created && newCollectionResult.Result != DavStatusCode.NoContent)
            {
                errors.AddResult(newUri, newCollectionResult.Result);
                return;
            }

            // Move all sub items
            await foreach (var entry in moveCollection.GetItemsAsync(cancellationToken).ConfigureAwait(false))
                await MoveAsync(moveCollection, entry, newCollectionResult.Collection, entry.Name, overwrite, baseUri, newPath, errors, cancellationToken).ConfigureAwait(false);

            // Delete the source collection
            var deleteResult = await sourceCollection.DeleteItemAsync(moveItem.Name, cancellationToken).ConfigureAwait(false);
            if (deleteResult != DavStatusCode.Ok)
                errors.AddResult(newUri, newCollectionResult.Result);
        }
        else
        {
            // Items should be moved directly
            var result = await sourceCollection.MoveItemAsync(moveItem.Name, destinationCollection, destinationName, overwrite, cancellationToken).ConfigureAwait(false);
            if (result.Result != DavStatusCode.Created && result.Result != DavStatusCode.NoContent)
                errors.AddResult(newUri, result.Result);
        }
    }
}
