using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PartDbProcurement;

public sealed class ExportRecord
{
    public string Project { get; set; } = "";
    public int Builds { get; set; }
    public DateTime ExportedAt { get; set; }
    public string File { get; set; } = "";
    public int OrderLines { get; set; }
}

/// <summary>Settings in %AppData%\PartDbProcurement\settings.json. The API token is encrypted with Windows DPAPI (current user).</summary>
public sealed class AppSettings
{
    public string ProjectPath { get; set; } = "";
    public int Builds { get; set; } = 1;
    public bool IncludeDnp { get; set; }
    public string PartDbUrl { get; set; } = "";
    public string? TokenProtected { get; set; }
    public List<ExportRecord> Exports { get; set; } = new();

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartDbProcurement");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    [System.Text.Json.Serialization.JsonIgnore] // never write the plain token to disk
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { /* corrupt settings: start fresh */ }
        return new AppSettings();
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
