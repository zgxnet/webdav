using Microsoft.Extensions.Logging;
using WebDav.Models;

namespace WebDav.Services;

public class UserService
{
    private readonly Dictionary<string, UserInfo> _users = new();
    private readonly bool _noPassword;
    private readonly ILogger<UserService> _logger;

    public class UserInfo
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public PermissionService.UserPermissions Permissions { get; set; } = new();

        public bool CheckPassword(string input)
        {
            if (Password.StartsWith("{bcrypt}"))
            {
                var savedPassword = Password.Substring("{bcrypt}".Length);
                return BCrypt.Net.BCrypt.Verify(input, savedPassword);
            }

            return Password == input;
        }
    }

    public UserService(WebDavConfig config, ILogger<UserService> logger)
    {
        _logger = logger;
        _noPassword = config.NoPassword;

        foreach (var user in config.Users)
        {
            try
            {
                var userInfo = new UserInfo
                {
                    Username = user.Username,
                    Password = ResolveEnvVariable(user.Password),
                    Directory = Path.GetFullPath(user.Directory ?? config.Directory),
                    Permissions = BuildUserPermissions(user, config, logger)
                };

                _users[user.Username] = userInfo;
                _logger.LogInformation("User configured: {Username} with directory: {Directory}",
                    user.Username, userInfo.Directory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure user: {Username}", user.Username);
                throw;
            }
        }
    }

    private string ResolveEnvVariable(string value)
    {
        try
        {
            if (value.StartsWith("{env}"))
            {
                var envVar = value.Substring("{env}".Length);
                var resolved = Environment.GetEnvironmentVariable(envVar);

                if (resolved == null)
                {
                    _logger.LogWarning("Environment variable not found: {EnvVar}, using original value", envVar);
                    return value;
                }

                return resolved;
            }
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving environment variable from value: {Value}", value);
            return value;
        }
    }

    private PermissionService.UserPermissions BuildUserPermissions(UserConfig user, WebDavConfig config, ILogger logger)
    {
        try
        {
            var permissions = new PermissionService.UserPermissions(logger)
            {
                DefaultPermissions = PermissionService.Permission.Parse(user.Permissions ?? config.Permissions)
            };

            var rules = new List<PermissionService.Rule>();

            // Handle rules behavior
            if (user.RulesBehavior == "append" || (user.RulesBehavior == null && config.RulesBehavior == "append"))
            {
                // Add global rules first
                if (config.Rules != null)
                {
                    foreach (var rule in config.Rules)
                    {
                        try
                        {
                            rules.Add(CreateRule(rule));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to create global rule for user {Username}: {Rule}",
                                user.Username, System.Text.Json.JsonSerializer.Serialize(rule));
                        }
                    }
                }
            }

            // Add user-specific rules
            if (user.Rules != null)
            {
                foreach (var rule in user.Rules)
                {
                    try
                    {
                        rules.Add(CreateRule(rule));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create user-specific rule for user {Username}: {Rule}",
                            user.Username, System.Text.Json.JsonSerializer.Serialize(rule));
                    }
                }
            }
            else if (config.Rules != null && user.RulesBehavior != "overwrite")
            {
                foreach (var rule in config.Rules)
                {
                    try
                    {
                        rules.Add(CreateRule(rule));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create default rule for user {Username}: {Rule}",
                            user.Username, System.Text.Json.JsonSerializer.Serialize(rule));
                    }
                }
            }

            permissions.Rules = rules;
            return permissions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build permissions for user: {Username}", user.Username);
            throw;
        }
    }

    private static PermissionService.Rule CreateRule(RuleConfig config)
    {
        var rule = new PermissionService.Rule
        {
            Permissions = PermissionService.Permission.Parse(config.Permissions)
        };

        if (!string.IsNullOrEmpty(config.Regex))
        {
            try
            {
                rule.Regex = new System.Text.RegularExpressions.Regex(config.Regex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid regex pattern: {config.Regex}", ex);
            }
        }
        else if (!string.IsNullOrEmpty(config.Path))
        {
            rule.Path = config.Path;
        }

        return rule;
    }

    public UserInfo? GetUser(string username)
    {
        try
        {
            return _users.TryGetValue(username, out var user) ? user : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user: {Username}", username);
            return null;
        }
    }

    public bool HasUsers => _users.Count > 0;
    public bool NoPassword => _noPassword;
}
