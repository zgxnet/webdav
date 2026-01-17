using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NWebDav.Server.Helpers;
using NWebDav.Server.Stores;

namespace NWebDav.Server.Handlers;

/// <summary>
/// Implementation of the PUT method.
/// </summary>
/// <remarks>
/// The specification of the WebDAV PUT method can be found in the
/// <see href="http://www.webdav.org/specs/rfc2518.html#METHOD_PUT">
/// WebDAV specification
/// </see>.
/// </remarks>
public class PutHandler : IRequestHandler
{
    private readonly IStore _store;

    public PutHandler(IStore store)
    {
        _store = store;
    }
    
    /// <summary>
    /// Handle a PUT request.
    /// </summary>
    /// <param name="httpContext">
    /// The HTTP context of the request.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous PUT operation. The task
    /// will always return <see langword="true"/> upon completion.
    /// </returns>
    public async Task<bool> HandleRequestAsync(HttpContext httpContext)
    {
        // Obtain request and response
        var request = httpContext.Request;
        var response = httpContext.Response;

        // Get the request path and split it
        var requestPath = request.Path.Value ?? "/";
        
        // Determine parent path and item name
        var lastSlash = requestPath.TrimEnd('/').LastIndexOf('/');
        var parentPath = lastSlash > 0 ? requestPath.Substring(0, lastSlash) : "/";
        var itemName = lastSlash >= 0 ? requestPath.Substring(lastSlash + 1) : requestPath.TrimStart('/');

        // Obtain collection
        var collection = await _store.GetCollectionAsync(parentPath, httpContext.RequestAborted).ConfigureAwait(false);
        if (collection == null)
        {
            // Source not found
            response.SetStatus(DavStatusCode.Conflict);
            return true;
        }

        // Obtain the item
        var result = await collection.CreateItemAsync(itemName, request.Body, true, httpContext.RequestAborted).ConfigureAwait(false);
        response.SetStatus(result.Result);
        return true;
    }
}
