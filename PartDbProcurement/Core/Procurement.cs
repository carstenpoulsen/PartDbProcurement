namespace PartDbProcurement.Core;

public enum LineStatus { Ok, BelowMinimum, Short, StockUnknown, NotInPartDb }

/// <summary>One BOM line combined with live Part-DB data.</summary>
public sealed class ProcurementLine
{
    public required BomGroup Bom { get; init; }
    public PartDbPart? Part { get; init; }
    public int Builds { get; init; }

    public int PerBuild => Bom.QuantityPerBuild;
    public int Need => PerBuild * Builds;
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
    public required BomResult Bom { get; init; }
    public required int Builds { get; init; }
    public required string PartDbUrl { get; init; }
    public required DateTime PulledAt { get; init; }
    public List<ProcurementLine> Lines { get; } = new();
    public List<string> MissingIds { get; init; } = new();

    public IEnumerable<ProcurementLine> OrderLines => Lines.Where(l => l.OrderQty > 0);

    public static ProcurementReport Build(BomResult bom, IReadOnlyDictionary<string, PartDbPart> parts,
        List<string> missing, int builds, string url, DateTime pulledAt)
    {
        var r = new ProcurementReport { Bom = bom, Builds = builds, PartDbUrl = url, PulledAt = pulledAt, MissingIds = missing };
        foreach (var g in bom.WithId)
            r.Lines.Add(new ProcurementLine { Bom = g, Part = parts.GetValueOrDefault(g.PartDbId!), Builds = builds });
        return r;
    }

    /// <summary>Same Part-DB data, different number of builds (no new pull needed).</summary>
    public ProcurementReport WithBuilds(int builds)
    {
        var r = new ProcurementReport { Bom = Bom, Builds = builds, PartDbUrl = PartDbUrl, PulledAt = PulledAt, MissingIds = MissingIds };
        foreach (var l in Lines) r.Lines.Add(new ProcurementLine { Bom = l.Bom, Part = l.Part, Builds = builds });
        return r;
    }
}
