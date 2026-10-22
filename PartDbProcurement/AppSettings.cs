using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartDbProcurement;

public sealed class ExportRecord
{
    public string Project { get; set; } = "";
    public int Builds { get; set; }
    public DateTime ExportedAt { get; set; }
    public string File { get; set; } = "";
    public int OrderLines { get; set; }
    /// <summary>Other projects in the same combined order list; empty for a single project.</summary>
    public string CombinedWith { get; set; } = "";
}

/// <summary>A KiCad project and its number of builds.</summary>
public sealed class ProjectEntry
{
    public string Path { get; set; } = "";
    public int Builds { get; set; } = 1;
}

/// <summary>Settings in %AppData%\PartDbProcurement\settings.json. The API token is encrypted with Windows DPAPI (current user).</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 5;

    /// <summary>The projects of the current purchase.</summary>
    public List<ProjectEntry> Projects { get; set; } = new();
    /// <summary>Most recently used projects, newest first, each with its last number of builds.</summary>
    public List<ProjectEntry> Recent { get; set; } = new();
    // v1.0 settings had a single project; read once and migrated to Projects in Load().
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProjectPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Builds { get; set; }
    public bool IncludeDnp { get; set; }
    public string PartDbUrl { get; set; } = "";
    public string? TokenProtected { get; set; }
    public List<ExportRecord> Exports { get; set; } = new();

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartDbProcurement");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    [JsonIgnore] // never write the plain token to disk
    public string Token
    {
        get
        {
            if (string.IsNullOrEmpty(TokenProtected)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(TokenProtected), null, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { return ""; }
            catch (FormatException) { return ""; }
        }
        set => TokenProtected = string.IsNullOrEmpty(value) ? null :
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Migrate(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings());
        }
        catch (Exception) { /* corrupt settings: start fresh */ }
        return new AppSettings();
    }

    private static AppSettings Migrate(AppSettings s)
    {
        if (s.Projects.Count == 0 && !string.IsNullOrWhiteSpace(s.ProjectPath))
        {
            s.Projects.Add(new ProjectEntry { Path = s.ProjectPath, Builds = Math.Max(1, s.Builds ?? 1) });
            if (s.Recent.Count == 0) s.Recent.Add(new ProjectEntry { Path = s.ProjectPath, Builds = Math.Max(1, s.Builds ?? 1) });
        }
        s.ProjectPath = null;
        s.Builds = null;
        return s;
    }

    /// <summary>Moves the project to the top of the recent list with this number of builds.</summary>
    public void Remember(string projectPath, int builds)
    {
        Recent.RemoveAll(e => string.Equals(e.Path, projectPath, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, new ProjectEntry { Path = projectPath, Builds = builds });
        if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    /// <summary>Updates the remembered number of builds without changing the order of the recent list.</summary>
    public void UpdateRecentBuilds(string projectPath, int builds)
    {
        foreach (var e in Recent.Where(e => string.Equals(e.Path, projectPath, StringComparison.OrdinalIgnoreCase)))
            e.Builds = builds;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        // Keep the export history short.
        Exports = Exports.OrderByDescending(e => e.ExportedAt).Take(200).ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public ExportRecord? LastExportFor(string projectPath) =>
        Exports.Where(e => string.Equals(e.Project, projectPath, StringComparison.OrdinalIgnoreCase))
               .OrderByDescending(e => e.ExportedAt).FirstOrDefault();
}
