namespace PartDbProcurement.Core;

public enum LineStatus { Ok, BelowMinimum, Short, StockUnknown, NotInPartDb }

/// <summary>One project in a purchase: its BOM and the number of boards to build.</summary>
public sealed class ProjectRun
{
    public required BomResult Bom { get; init; }
    public required int Builds { get; init; }
}

/// <summary>How one project of the purchase uses a part.</summary>
public sealed class ProjectUse
{
    public required ProjectRun Project { get; init; }
    public required BomGroup Group { get; init; }
    public int PerBuild => Group.QuantityPerBuild;
    public int Need => PerBuild * Project.Builds;
}

/// <summary>One Part-DB part combined with live Part-DB data, summed over all projects of the purchase.</summary>
public sealed class ProcurementLine
{
    /// <summary>One entry per project that uses the part, in project order.</summary>
    public required IReadOnlyList<ProjectUse> Uses { get; init; }
    public PartDbPart? Part { get; init; }

    /// <summary>BOM data (ID, value, MPN, description) of the first project that uses the part.</summary>
    public BomGroup Bom => Uses[0].Group;
    public int PerBuild => Uses.Sum(u => u.PerBuild);
    public int Need => Uses.Sum(u => u.Need);
    public int PerBuildIn(ProjectRun project) => Uses.FirstOrDefault(u => u.Project == project)?.PerBuild ?? 0;

    /// <summary>References; prefixed with the project name when the purchase has several projects.</summary>
    public string ReferenceText(bool combined) => !combined ? Bom.ReferenceList :
        string.Join(" · ", Uses.Select(u => $"{u.Project.Bom.ProjectName}: {u.Group.ReferenceList}"));

    public double Stock => Part?.Stock ?? 0;
    public double MinStock => Part?.MinStock ?? 0;
    public double AfterBuild => Stock - Need;
    public double ShortForBuild => Math.Max(0, Need - Stock);
    /// <summary>Quantity that covers the builds and restores minimum stock.</summary>
    public double ToOrder => Part == null ? Need : Math.Max(0, Math.Ceiling(Need + MinStock - Stock));

    /// <summary>Preferred offer: first non-obsolete supplier with a part number, else the first one.</summary>
    public SupplierOffer? Offer => Part?.Offers.FirstOrDefault(o => !o.Obsolete && !string.IsNullOrEmpty(o.Supplier))
                                   ?? Part?.Offers.FirstOrDefault();

    /// <summary>To-order quantity raised to the supplier's smallest price break (MOQ).</summary>
    public double OrderQty => ToOrder <= 0 ? 0 : Math.Max(ToOrder, Offer?.Moq ?? 1);
    public PriceBreak? Price => Offer?.PriceFor(OrderQty);
    public decimal? LineTotal => Price == null || OrderQty <= 0 ? null : Price.UnitPrice * (decimal)OrderQty;

    public LineStatus Status =>
        Part == null ? LineStatus.NotInPartDb :
        Part.StockUnknown && Stock < Need ? LineStatus.StockUnknown :
        ShortForBuild > 0 ? LineStatus.Short :
        AfterBuild < MinStock ? LineStatus.BelowMinimum : LineStatus.Ok;

    public string StatusText => Status switch
    {
        LineStatus.Ok => "OK",
        LineStatus.BelowMinimum => "Below min. after build",
        LineStatus.Short => "Short for build",
        LineStatus.StockUnknown => "Stock unknown",
        _ => "ID not found in Part-DB",
    };

    public string OtherOffers => Part == null ? "" :
        string.Join("; ", Part.Offers.Where(o => o != Offer).Select(o => $"{o.Supplier}: {o.Spn}{(o.Obsolete ? " (obsolete)" : "")}"));
}

public sealed class ProcurementReport
{
    /// <summary>The projects of the purchase; usually one.</summary>
    public required IReadOnlyList<ProjectRun> Projects { get; init; }
    public required string PartDbUrl { get; init; }
    public required DateTime PulledAt { get; init; }
    public List<ProcurementLine> Lines { get; } = new();
    public List<string> MissingIds { get; init; } = new();

    public bool IsCombined => Projects.Count > 1;
    /// <summary>"Board ×3", or "BoardA ×3 + BoardB ×2" for a combined purchase.</summary>
    public string Title => string.Join(" + ", Projects.Select(p => $"{p.Bom.ProjectName} ×{p.Builds}"));

    public IEnumerable<ProcurementLine> OrderLines => Lines.Where(l => l.OrderQty > 0);

    public static ProcurementReport Build(IReadOnlyList<ProjectRun> projects, IReadOnlyDictionary<string, PartDbPart> parts,
        List<string> missing, string url, DateTime pulledAt)
    {
        var r = new ProcurementReport { Projects = projects, PartDbUrl = url, PulledAt = pulledAt, MissingIds = missing };
        // Same Part-DB ID in several projects: one line, need summed. Order: first project's order, then new IDs of the next.
        var uses = new Dictionary<string, List<ProjectUse>>();
        var order = new List<string>();
        foreach (var p in projects)
            foreach (var g in p.Bom.WithId)
            {
                if (!uses.TryGetValue(g.PartDbId!, out var list)) { uses[g.PartDbId!] = list = new(); order.Add(g.PartDbId!); }
                list.Add(new ProjectUse { Project = p, Group = g });
            }
        foreach (var id in order)
            r.Lines.Add(new ProcurementLine { Uses = uses[id], Part = parts.GetValueOrDefault(id) });
        return r;
    }

    /// <summary>Same Part-DB data, different numbers of builds (one per project, no new pull needed).</summary>
    public ProcurementReport WithBuilds(IReadOnlyList<int> builds)
    {
        if (builds.Count != Projects.Count) throw new ArgumentException("One number of builds per project expected.", nameof(builds));
        var runs = Projects.Select((p, i) => new ProjectRun { Bom = p.Bom, Builds = builds[i] }).ToList();
        var parts = Lines.Where(l => l.Part != null).ToDictionary(l => l.Bom.PartDbId!, l => l.Part!);
        return Build(runs, parts, MissingIds, PartDbUrl, PulledAt);
    }
}
