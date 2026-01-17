using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using NWebDav.Server.Helpers;
using NWebDav.Server.Stores;
using UriHelper = NWebDav.Server.Helpers.UriHelper;

namespace NWebDav.Server.Handlers;

/// <summary>
/// Implementation of the COPY method.
/// </summary>
/// <remarks>
/// The specification of the WebDAV COPY method can be found in the
/// <see href="http://www.webdav.org/specs/rfc2518.html#METHOD_COPY">
/// WebDAV specification
/// </see>.
/// </remarks>
public class CopyHandler : IRequestHandler
{
    private readonly IXmlReaderWriter _xmlReaderWriter;
    private readonly IStore _store;

    public CopyHandler(IXmlReaderWriter xmlReaderWriter, IStore store)
    {
        _xmlReaderWriter = xmlReaderWriter;
        _store = store;
    }
    
    /// <summary>
    /// Handle a COPY request.
    /// </summary>
    /// <param name="httpContext">
    /// The HTTP context of the request.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous COPY operation. The task
    /// will always return <see langword="true"/> upon completion.
    /// </returns>
    public async Task<bool> HandleRequestAsync(HttpContext httpContext)
    {
        // Obtain request and response
        var request = httpContext.Request;
        var response = httpContext.Response;
        
        // Obtain the destination
        var destinationUri = request.GetDestinationUri();
        if (destinationUri == null)
        {
            // Bad request
            response.SetStatus(DavStatusCode.BadRequest, "Destination header is missing.");
            return true;
        }

        // Get source and destination paths
        var sourcePath = request.Path.Value ?? "/";
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

        // Check if the Overwrite header is set
        var overwrite = request.GetOverwrite();

        // Split destination path
        var destLastSlash = destinationPath.TrimEnd('/').LastIndexOf('/');
        var destParentPath = destLastSlash > 0 ? destinationPath.Substring(0, destLastSlash) : "/";
        var destName = destLastSlash >= 0 ? destinationPath.Substring(destLastSlash + 1).TrimEnd('/') : destinationPath.TrimStart('/');

        // Obtain the destination collection
        var destinationCollection = await _store.GetCollectionAsync(destParentPath, httpContext.RequestAborted).ConfigureAwait(false);
        if (destinationCollection == null)
        {
            // Source not found
            response.SetStatus(DavStatusCode.Conflict, "Destination cannot be found or is not a collection.");
            return true;
        }

        // Obtain the source item
        var sourceItem = await _store.GetItemAsync(sourcePath, httpContext.RequestAborted).ConfigureAwait(false);
        if (sourceItem == null)
        {
            // Source not found
            response.SetStatus(DavStatusCode.NotFound, "Source cannot be found.");
            return true;
        }

        // Determine depth
        var depth = request.GetDepth();

        // Keep track of all errors
        var errors = new UriResultCollection();
        
        // Build base URI for error reporting
        var baseUri = request.GetUri();

        // Copy collection
        await CopyAsync(sourceItem, destinationCollection, destName, overwrite, depth, baseUri, destParentPath, errors, httpContext.RequestAborted).ConfigureAwait(false);

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

    private async Task CopyAsync(IStoreItem source, IStoreCollection destinationCollection, string name, bool overwrite, int depth, Uri baseUri, string basePath, UriResultCollection errors, CancellationToken cancellationToken)
    {
        // Determine the new paths
        var newPath = basePath.TrimEnd('/') + "/" + name;
        var newUri = UriHelper.CombineWithPath(baseUri, newPath);

        // Copy the item
        var copyResult = await source.CopyAsync(destinationCollection, name, overwrite, cancellationToken).ConfigureAwait(false);
        if (copyResult.Result != DavStatusCode.Created && copyResult.Result != DavStatusCode.NoContent)
        {
            errors.AddResult(newUri, copyResult.Result);
            return;
        }

        // Check if the source is a collection and we are requested to copy recursively
        if (source is IStoreCollection sourceCollection && depth > 0)
        {
            // The result should also contain a collection
            var newCollection = (IStoreCollection)copyResult.Item!;

            // Copy all children of the source collection
            await foreach (var entry in sourceCollection.GetItemsAsync(cancellationToken).ConfigureAwait(false))
                await CopyAsync(entry, newCollection, entry.Name, overwrite, depth - 1, baseUri, newPath, errors, cancellationToken).ConfigureAwait(false);
        }
    }
}
