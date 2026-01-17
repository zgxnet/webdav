using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WebDav.Models;

namespace WebDav.Services;

public class PermissionService
{
    private readonly ILogger<PermissionService>? _logger;

    public PermissionService(ILogger<PermissionService>? logger = null)
    {
        _logger = logger;
    }

    public class Permission
    {
        public bool CanCreate { get; set; }
        public bool CanRead { get; set; }
        public bool CanUpdate { get; set; }
        public bool CanDelete { get; set; }

        public static Permission Parse(string permissions)
        {
            var upper = permissions.ToUpperInvariant();
            return new Permission
            {
                CanCreate = upper.Contains('C'),
                CanRead = upper.Contains('R'),
                CanUpdate = upper.Contains('U'),
                CanDelete = upper.Contains('D')
            };
        }

        public bool IsAllowed(string method, bool fileExists)
        {
            return method.ToUpperInvariant() switch
            {
                "GET" or "HEAD" or "PROPFIND" or "OPTIONS" => CanRead,
                "PUT" => fileExists ? CanUpdate : CanCreate,
                "PATCH" or "PROPPATCH" => CanUpdate,
                "POST" or "MKCOL" => CanCreate,
                "DELETE" => CanDelete,
                "COPY" or "MOVE" => fileExists ? (CanRead && CanUpdate) : (CanRead && CanCreate),
                _ => false
            };
        }
    }

    public class Rule
    {
        public Permission Permissions { get; set; } = new();
        public string? Path { get; set; }
        public Regex? Regex { get; set; }

        public bool Matches(string path)
        {
            if (Regex != null)
                return Regex.IsMatch(path);
            
            if (Path != null)
                return path.StartsWith(Path, StringComparison.OrdinalIgnoreCase);
            
            return false;
        }
    }

    public class UserPermissions
    {
        private readonly ILogger? _logger;

        public UserPermissions(ILogger? logger = null)
        {
            _logger = logger;
        }

        public Permission DefaultPermissions { get; set; } = new();
        public List<Rule> Rules { get; set; } = new();

        public bool IsAllowed(string method, string path, string? destination, Func<string, bool> fileExists)
        {
            try
            {
                // Check destination for COPY and MOVE
                if ((method == "COPY" || method == "MOVE") && !string.IsNullOrEmpty(destination))
                {
                    bool ruleMatched = false;
                    for (int i = Rules.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            if (Rules[i].Matches(destination))
                            {
                                ruleMatched = true;
                                bool destExists = fileExists(destination);
                                if (!Rules[i].Permissions.IsAllowed(method, destExists))
                                    return false;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error matching rule for destination path: {Destination}", destination);
                            return false;
                        }
                    }

                    if (!ruleMatched)
                    {
                        try
                        {
                            bool destExists = fileExists(destination);
                            if (!DefaultPermissions.IsAllowed(method, destExists))
                                return false;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error checking destination file existence: {Destination}", destination);
                            return false;
                        }
                    }
                }

                // Check source permissions
                for (int i = Rules.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (Rules[i].Matches(path))
                        {
                            bool exists = fileExists(path);
                            return Rules[i].Permissions.IsAllowed(method, exists);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error matching rule for path: {Path}", path);
                        return false;
                    }
                }

                try
                {
                    bool fileExistsAtPath = fileExists(path);
                    return DefaultPermissions.IsAllowed(method, fileExistsAtPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking file existence: {Path}", path);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error in permission check for method {Method}, path {Path}", method, path);
                return false;
            }
        }
    }
}
