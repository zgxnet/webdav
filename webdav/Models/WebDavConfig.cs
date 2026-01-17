namespace WebDav.Models;

public class WebDavConfig
{
    public string Address { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 6065;
    public bool Tls { get; set; } = false;
    public string? Cert { get; set; }
    public string? Key { get; set; }
    public string Prefix { get; set; } = "/";
    public bool Debug { get; set; } = false;
    public bool NoSniff { get; set; } = false;
    public bool NoPassword { get; set; } = false;
    public bool BehindProxy { get; set; } = false;
    public string Directory { get; set; } = ".";
    public string Permissions { get; set; } = "R";
    public string RulesBehavior { get; set; } = "overwrite";
    public LogConfig Log { get; set; } = new();
    public CorsConfig Cors { get; set; } = new();
    public List<UserConfig> Users { get; set; } = new();
    public List<RuleConfig> Rules { get; set; } = new();
}

public class LogConfig
{
    public string Format { get; set; } = "console";
    public bool Colors { get; set; } = true;
    public List<string> Outputs { get; set; } = new() { "stderr" };
}

public class CorsConfig
{
    public bool Enabled { get; set; } = false;
    public bool Credentials { get; set; } = false;
    public List<string> AllowedHeaders { get; set; } = new() { "*" };
    public List<string> AllowedHosts { get; set; } = new() { "*" };
    public List<string> AllowedMethods { get; set; } = new() { "*" };
    public List<string> ExposedHeaders { get; set; } = new();
}

public class UserConfig
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Directory { get; set; }
    public string? Permissions { get; set; }
    public string? RulesBehavior { get; set; }
    public List<RuleConfig>? Rules { get; set; }
}

public class RuleConfig
{
    public string? Path { get; set; }
    public string? Regex { get; set; }
    public string Permissions { get; set; } = "R";
}
