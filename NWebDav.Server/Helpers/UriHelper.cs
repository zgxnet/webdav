using System;

namespace NWebDav.Server.Helpers;

public static class UriHelper
{
    public static Uri Combine(Uri baseUri, string path)
    {
        var uriText = baseUri.OriginalString;
        if (uriText.EndsWith("/"))
            uriText = uriText[..^1];
        return new Uri($"{uriText}/{path}", UriKind.Absolute);
    }

    public static string ToEncodedString(Uri entryUri)
    {
        return entryUri
            .AbsoluteUri
            .Replace("#", "%23")
            .Replace("[", "%5B")
            .Replace("]", "%5D");
    }

    public static string GetDecodedPath(Uri uri)
    {
        return uri.LocalPath + Uri.UnescapeDataString(uri.Fragment);
    }
    
    /// <summary>
    /// Combine a base URI with a path string to create a new URI.
    /// This is used for generating URIs in responses (e.g., href elements).
    /// </summary>
    public static Uri CombineWithPath(Uri baseUri, string relativePath)
    {
        // Ensure the path starts with /
        if (!relativePath.StartsWith("/"))
            relativePath = "/" + relativePath;
            
        return new Uri(baseUri, relativePath);
    }
}
