using System.Text.Json;

namespace PartDbProcurement.Core;

/// <summary>One placed symbol instance in the schematic hierarchy (multi-unit symbols are merged).</summary>
public sealed class PlacedSymbol
{
    public required string Reference { get; init; }
    public required string SheetPath { get; init; }
    public required string SheetName { get; init; }
    public string? PartDbId { get; init; }
    public string Value { get; init; } = "";
    public string Footprint { get; init; } = "";
    public string Mpn { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Description { get; init; } = "";
    public bool InBom { get; init; } = true;
    public bool Dnp { get; init; }
    /// <summary>True when "Exclude from BOM" comes only from the sheet (or a parent sheet), not the symbol itself.</summary>
    public bool NoBomBySheet { get; init; }
    /// <summary>True when DNP comes only from the sheet (or a parent sheet), not the symbol itself.</summary>
    public bool DnpBySheet { get; init; }
}

/// <summary>BOM line aggregated over all instances that share a Part-DB ID (or Value/Footprint/MPN if no ID).</summary>
public sealed class BomGroup
{
    public string? PartDbId { get; init; }
    public string Value { get; init; } = "";
    public string Footprint { get; init; } = "";
    public string Mpn { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> References { get; } = new();
    public HashSet<string> Sheets { get; } = new();
    public int QuantityPerBuild => References.Count;
    public string ReferenceList => string.Join(", ", References.OrderBy(r => r, RefComparer.Instance));
}

public sealed class BomResult
{
    public required string ProjectFile { get; init; }
    public required string ProjectName { get; init; }
    public List<string> SchematicFiles { get; } = new();
    public List<PlacedSymbol> Counted { get; } = new();
    public List<(PlacedSymbol Symbol, string Reason)> Excluded { get; } = new();
    public List<BomGroup> WithId { get; } = new();
    public List<BomGroup> WithoutId { get; } = new();
    public List<string> Warnings { get; } = new();
    public DateTime NewestSchematicWriteUtc { get; set; }
}

/// <summary>Reads a KiCad project directly from its .kicad_pro / .kicad_sch files.</summary>
public static class KiCadBomReader
{
    public static BomResult Read(string projectFile, bool includeDnp)
    {
        projectFile = Path.GetFullPath(projectFile);
        if (!File.Exists(projectFile)) throw new FileNotFoundException("Project file not found.", projectFile);
        var dir = Path.GetDirectoryName(projectFile)!;
        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        var result = new BomResult { ProjectFile = projectFile, ProjectName = projectName };

        // Top-level sheets (KiCad 10+). Older versions: <project>.kicad_sch with no known root path.
        var tops = ReadTopLevelSheets(projectFile);
        if (tops.Count == 0) tops.Add((projectName + ".kicad_sch", null, projectName));

        var collected = new Dictionary<string, PlacedSymbol>(); // key: sheetPath|reference
        foreach (var (file, uuid, name) in tops)
        {
            var full = Path.Combine(dir, file);
            if (!File.Exists(full)) { result.Warnings.Add($"Schematic not found: {file}"); continue; }
            string? rootPath = uuid != null ? "/" + uuid : null;
            Visit(full, rootPath, name, projectName, result, collected, new HashSet<string>(), 0, false, false);
        }

        foreach (var s in collected.Values)
        {
            if (!s.InBom) { result.Excluded.Add((s, s.NoBomBySheet ? "Exclude from BOM (sheet)" : "Exclude from BOM")); continue; }
            if (s.Dnp && !includeDnp) { result.Excluded.Add((s, s.DnpBySheet ? "DNP (sheet)" : "DNP")); continue; }
            result.Counted.Add(s);
        }

        foreach (var g in result.Counted.Where(s => !string.IsNullOrWhiteSpace(s.PartDbId)).GroupBy(s => s.PartDbId!.Trim()))
        {
            var first = g.First();
            var bg = new BomGroup { PartDbId = g.Key, Value = first.Value, Footprint = first.Footprint, Mpn = first.Mpn, Description = first.Description };
            foreach (var s in g) { bg.References.Add(s.Reference); bg.Sheets.Add(s.SheetName); }
            result.WithId.Add(bg);
        }
        foreach (var g in result.Counted.Where(s => string.IsNullOrWhiteSpace(s.PartDbId)).GroupBy(s => (s.Value, s.Footprint, s.Mpn)))
        {
            var first = g.First();
            var bg = new BomGroup { Value = first.Value, Footprint = first.Footprint, Mpn = first.Mpn, Description = first.Description };
            foreach (var s in g) { bg.References.Add(s.Reference); bg.Sheets.Add(s.SheetName); }
            result.WithoutId.Add(bg);
        }
        result.WithId.Sort((a, b) => RefComparer.Instance.Compare(a.References.Min(RefComparer.Instance), b.References.Min(RefComparer.Instance)));
        result.WithoutId.Sort((a, b) => RefComparer.Instance.Compare(a.References.Min(RefComparer.Instance), b.References.Min(RefComparer.Instance)));
        return result;
    }

    private static List<(string File, string? Uuid, string Name)> ReadTopLevelSheets(string projectFile)
    {
        var list = new List<(string, string?, string)>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(projectFile));
            if (doc.RootElement.TryGetProperty("schematic", out var sch) &&
                sch.TryGetProperty("top_level_sheets", out var tls) && tls.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in tls.EnumerateArray())
                {
                    var f = e.TryGetProperty("filename", out var fp) ? fp.GetString() : null;
                    var u = e.TryGetProperty("uuid", out var up) ? up.GetString() : null;
                    var n = e.TryGetProperty("name", out var np) ? np.GetString() : null;
                    if (!string.IsNullOrEmpty(f)) list.Add((f!, u, n ?? Path.GetFileNameWithoutExtension(f!)));
                }
            }
        }
        catch (JsonException) { /* fall back to <project>.kicad_sch */ }
        return list;
    }

    /// <param name="sheetPath">Instance path of this sheet ("/rootuuid/sheetuuid"); null if unknown (older KiCad).</param>
    private static void Visit(string file, string? sheetPath, string sheetName, string project,
        BomResult result, Dictionary<string, PlacedSymbol> collected, HashSet<string> stack, int depth,
        bool sheetExcluded, bool sheetDnp)
    {
        if (depth > 32) { result.Warnings.Add($"Hierarchy too deep at {sheetName}"); return; }
        var key = Path.GetFullPath(file).ToLowerInvariant() + "|" + sheetPath;
        if (!stack.Add(key)) { result.Warnings.Add($"Recursive sheet reference: {Path.GetFileName(file)}"); return; }

        if (!result.SchematicFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
        {
            result.SchematicFiles.Add(file);
            var t = File.GetLastWriteTimeUtc(file);
            if (t > result.NewestSchematicWriteUtc) result.NewestSchematicWriteUtc = t;
        }

        var root = SExprParser.ParseFile(file);
        var dir = Path.GetDirectoryName(file)!;

        // Older files without a known root path: derive it from the sheet/symbol instance data.
        if (sheetPath == null) sheetPath = GuessRootPath(root, project) ?? "/";

        foreach (var sym in root.Children("symbol"))
        {
            var props = Properties(sym);
            var instances = Instances(sym, project).ToList();
            List<(string Path, string Reference)> placed;
            if (instances.Count > 0)
                placed = instances.Where(i => PathEquals(i.Path, sheetPath)).ToList();
            else
                placed = new() { (sheetPath, props.GetValueOrDefault("Reference", "?")) }; // pre-KiCad-7 files

            foreach (var (_, reference) in placed)
            {
                if (reference.StartsWith("#")) continue; // power symbols, flags
                var k = sheetPath + "|" + reference;
                if (collected.ContainsKey(k)) continue;  // further units of a multi-unit symbol
                collected[k] = new PlacedSymbol
                {
                    Reference = reference,
                    SheetPath = sheetPath,
                    SheetName = sheetName,
                    PartDbId = FindPartDbId(props),
                    Value = props.GetValueOrDefault("Value", ""),
                    Footprint = props.GetValueOrDefault("Footprint", ""),
                    Mpn = props.GetValueOrDefault("MPN", props.GetValueOrDefault("manf#", "")),
                    Manufacturer = props.GetValueOrDefault("Manufacturer", props.GetValueOrDefault("manf", "")),
                    Description = props.GetValueOrDefault("Description", ""),
                    // Sheet-level "Exclude from BOM" / DNP applies to everything on the sheet and its sub-sheets.
                    InBom = sym.Flag("in_bom", true) && !sheetExcluded,
                    Dnp = sym.Flag("dnp", false) || sheetDnp,
                    NoBomBySheet = sheetExcluded && sym.Flag("in_bom", true),
                    DnpBySheet = sheetDnp && !sym.Flag("dnp", false),
                };
            }
        }

        foreach (var sheet in root.Children("sheet"))
        {
            var props = Properties(sheet);
            var fileName = props.GetValueOrDefault("Sheetfile") ?? props.GetValueOrDefault("Sheet file");
            var uuid = sheet.ChildValue("uuid");
            if (string.IsNullOrEmpty(fileName) || uuid == null) continue;
            var childName = props.GetValueOrDefault("Sheetname") ?? props.GetValueOrDefault("Sheet name") ?? Path.GetFileNameWithoutExtension(fileName);
            var childFile = Path.Combine(dir, fileName);
            if (!File.Exists(childFile)) { result.Warnings.Add($"Sub-sheet not found: {fileName}"); continue; }
            var childPath = sheetPath.TrimEnd('/') + "/" + uuid;
            Visit(childFile, childPath, childName, project, result, collected, stack, depth + 1,
                sheetExcluded || !sheet.Flag("in_bom", true), sheetDnp || sheet.Flag("dnp", false));
        }
        stack.Remove(key);
    }

    private static string? GuessRootPath(SNode root, string project)
    {
        // A sheet placed in the root carries the root path as its instance path.
        foreach (var sheet in root.Children("sheet"))
            foreach (var i in Instances(sheet, project)) return i.Path;
        foreach (var sym in root.Children("symbol"))
            foreach (var i in Instances(sym, project)) return i.Path;
        return null;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Properties(SNode node)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in node.Children("property"))
        {
            var vals = p.Items.OfType<string>().ToList();
            if (vals.Count >= 1) d[vals[0]] = vals.Count >= 2 ? vals[1] : "";
        }
        return d;
    }

    private static IEnumerable<(string Path, string Reference)> Instances(SNode node, string project)
    {
        var inst = node.Child("instances");
        if (inst == null) yield break;
        foreach (var proj in inst.Children("project"))
        {
            if (!string.Equals(proj.Value, project, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var p in proj.Children("path"))
                yield return (p.Value ?? "", p.ChildValue("reference") ?? "");
        }
    }

    /// <summary>Accepts "Part-DB ID", "PartDB ID", "partdb_id" etc.</summary>
    private static string? FindPartDbId(Dictionary<string, string> props)
    {
        foreach (var (k, v) in props)
        {
            var norm = new string(k.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (norm == "partdbid" && !string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }
}

/// <summary>Natural sort for designators: R2 before R10.</summary>
public sealed class RefComparer : IComparer<string>
{
    public static readonly RefComparer Instance = new();
    public int Compare(string? x, string? y)
    {
        if (x == null || y == null) return string.Compare(x, y, StringComparison.Ordinal);
        var (px, nx) = Split(x); var (py, ny) = Split(y);
        int c = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : nx != ny ? nx.CompareTo(ny) : string.Compare(x, y, StringComparison.Ordinal);
    }
    private static (string, long) Split(string s)
    {
        int i = 0; while (i < s.Length && !char.IsDigit(s[i])) i++;
        int j = i; while (j < s.Length && char.IsDigit(s[j])) j++;
        return (s[..i], j > i && long.TryParse(s[i..j], out var n) ? n : -1);
    }
}
