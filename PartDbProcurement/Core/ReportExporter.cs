namespace PartDbProcurement.Core;

/// <summary>Turns a ProcurementReport into an Excel workbook.</summary>
public static class ReportExporter
{
    public static string DefaultFileName(ProcurementReport r) =>
        $"{r.Bom.ProjectName}_order_{r.Builds}x_{r.PulledAt:yyyy-MM-dd}.xlsx";

    public static void Save(ProcurementReport r, string path)
    {
        XlsxWriter.Save(path, new[] { OrderSheet(r), CoverageSheet(r), NoIdSheet(r), ExcludedSheet(r), InfoSheet(r) });
    }

    private static XCell N(double v, XStyle s = XStyle.Integer) => new(v, s);
    private static XCell T(string? v, XStyle s = XStyle.Default) => new(v ?? "", s);
    private static XCell H(string v) => new(v, XStyle.Header);

    private static XSheet OrderSheet(ProcurementReport r)
    {
        var s = new XSheet("Order list");
        s.Add(new XCell($"{r.Bom.ProjectName} - order for {r.Builds} build(s)", XStyle.Title));
        s.Add(T($"Part-DB data pulled {r.PulledAt:yyyy-MM-dd HH:mm}. To order = need + minimum stock - stock, raised to the smallest price break."));
        s.Blank();
        s.HeaderRow = 3;
        s.Add(H("Supplier"), H("SPN"), H("MPN"), H("Manufacturer"), H("Part-DB ID"), H("Name"), H("Description"),
              H("Need"), H("Stock"), H("Min. stock"), H("To order"), H("Order qty"), H("Unit price"), H("Currency"),
              H("Line total"), H("References"));
        s.Widths = new double[] { 14, 26, 22, 16, 10, 22, 36, 8, 8, 10, 9, 10, 11, 9, 12, 40 };

        var lines = r.OrderLines
            .OrderBy(l => string.IsNullOrEmpty(l.Offer?.Supplier) ? "~" : l.Offer!.Supplier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Part?.Mpn ?? l.Bom.Mpn, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var l in lines)
        {
            var hl = l.Status == LineStatus.Short || l.Status == LineStatus.NotInPartDb ? XStyle.Red : XStyle.Default;
            s.Add(T(l.Offer?.Supplier is { Length: > 0 } sup ? sup : "(no supplier)", hl), T(l.Offer?.Spn),
                T(l.Part?.Mpn ?? l.Bom.Mpn), T(l.Part?.Manufacturer), T(l.Bom.PartDbId), T(l.Part?.Name ?? l.Bom.Value),
                T(l.Part?.Description ?? l.Bom.Description), N(l.Need), N(l.Stock), N(l.MinStock), N(l.ToOrder),
                N(l.OrderQty), l.Price == null ? T("") : new XCell(l.Price.UnitPrice, XStyle.Price), T(l.Price?.Currency),
                l.LineTotal == null ? T("") : new XCell(l.LineTotal.Value, XStyle.Money), T(l.Bom.ReferenceList));
        }

        // Totals per supplier and currency, below the table.
        s.Blank();
        s.Add(new XCell("Totals per supplier", XStyle.Bold), new XCell(null, XStyle.Bold), new XCell("Lines", XStyle.Bold),
            new XCell("Currency", XStyle.Bold), new XCell("Total", XStyle.Bold));
        foreach (var g in lines.Where(l => l.LineTotal != null)
                     .GroupBy(l => (Supplier: l.Offer?.Supplier ?? "", Currency: l.Price?.Currency ?? ""))
                     .OrderBy(g => g.Key.Supplier))
        {
            s.Add(T(g.Key.Supplier), T(""), N(g.Count()), T(g.Key.Currency), new XCell(g.Sum(l => l.LineTotal!.Value), XStyle.Money));
        }
        int unpriced = lines.Count(l => l.LineTotal == null);
        if (unpriced > 0) s.Add(T($"{unpriced} line(s) have no price in Part-DB and are not in the totals."));
        if (lines.Count == 0) s.Add(new XCell("Nothing to order: stock covers all builds and minimum stock levels.", XStyle.Green));
        return s;
    }

    private static XSheet CoverageSheet(ProcurementReport r)
    {
        var s = new XSheet("Stock coverage");
        s.HeaderRow = 0;
        s.Add(H("Status"), H("Part-DB ID"), H("Name"), H("MPN"), H("Value"), H("Per build"), H("Need"), H("Stock"),
              H("Min. stock"), H("After build"), H("Short for build"), H("To order"), H("Storage location"),
              H("Supplier"), H("SPN"), H("Other suppliers"), H("References"), H("Part-DB"));
        s.Widths = new double[] { 20, 10, 22, 22, 14, 9, 8, 8, 10, 11, 13, 9, 18, 14, 26, 30, 40, 10 };
        foreach (var l in r.Lines)
        {
            var st = l.Status switch
            {
                LineStatus.Ok => XStyle.Green,
                LineStatus.BelowMinimum => XStyle.Yellow,
                LineStatus.StockUnknown => XStyle.Yellow,
                _ => XStyle.Red,
            };
            s.Add(T(l.StatusText, st), T(l.Bom.PartDbId), T(l.Part?.Name ?? ""), T(l.Part?.Mpn ?? l.Bom.Mpn), T(l.Bom.Value),
                N(l.PerBuild), N(l.Need), N(l.Stock), N(l.MinStock), N(l.AfterBuild), N(l.ShortForBuild), N(l.ToOrder),
                T(string.Join(", ", l.Part?.StorageLocations ?? new())), T(l.Offer?.Supplier), T(l.Offer?.Spn),
                T(l.OtherOffers), T(l.Bom.ReferenceList),
                l.Part == null ? T("") : XCell.Link(l.Part.Url, "open"));
        }
        return s;
    }

    private static XSheet NoIdSheet(ProcurementReport r)
    {
        var s = new XSheet("No Part-DB ID");
        if (r.Bom.WithoutId.Count == 0)
        {
            s.Add(new XCell("All counted symbols have a Part-DB ID.", XStyle.Green));
            return s;
        }
        s.HeaderRow = 0;
        s.Add(H("Value"), H("Footprint"), H("MPN"), H("Description"), H("Per build"), H("Need"), H("References"), H("Sheet"));
        s.Widths = new double[] { 16, 36, 22, 36, 9, 8, 40, 16 };
        foreach (var g in r.Bom.WithoutId)
            s.Add(T(g.Value, XStyle.Red), T(g.Footprint), T(g.Mpn), T(g.Description), N(g.QuantityPerBuild),
                N(g.QuantityPerBuild * r.Builds), T(g.ReferenceList), T(string.Join(", ", g.Sheets)));
        return s;
    }

    private static XSheet ExcludedSheet(ProcurementReport r)
    {
        var s = new XSheet("Excluded");
        s.HeaderRow = 0;
        s.Add(H("Reference"), H("Value"), H("Reason"), H("Part-DB ID"), H("Sheet"));
        s.Widths = new double[] { 12, 18, 18, 10, 16 };
        foreach (var (sym, why) in r.Bom.Excluded.OrderBy(e => e.Symbol.Reference, RefComparer.Instance))
            s.Add(T(sym.Reference), T(sym.Value), T(why), T(sym.PartDbId), T(sym.SheetName));
        return s;
    }

    private static XSheet InfoSheet(ProcurementReport r)
    {
        var s = new XSheet("Info");
        s.Widths = new double[] { 26, 90 };
        s.Add(new XCell("Procurement report", XStyle.Title));
        s.Add(T("Project", XStyle.Bold), T(r.Bom.ProjectFile));
        s.Add(T("Builds", XStyle.Bold), N(r.Builds));
        s.Add(T("Part-DB", XStyle.Bold), T(r.PartDbUrl));
        s.Add(T("Part-DB data pulled", XStyle.Bold), T(r.PulledAt.ToString("yyyy-MM-dd HH:mm:ss")));
        s.Add(T("Schematic last saved", XStyle.Bold), T(r.Bom.NewestSchematicWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        s.Add(T("Schematic files", XStyle.Bold), T(string.Join(", ", r.Bom.SchematicFiles.Select(Path.GetFileName))));
        s.Add(T("Parts counted per build", XStyle.Bold), N(r.Bom.Counted.Count));
        s.Add(T("BOM lines with Part-DB ID", XStyle.Bold), N(r.Bom.WithId.Count));
        s.Add(T("BOM lines without ID", XStyle.Bold), N(r.Bom.WithoutId.Count));
        s.Add(T("Excluded symbols", XStyle.Bold), N(r.Bom.Excluded.Count));
        s.Add(T("Lines to order", XStyle.Bold), N(r.OrderLines.Count()));
        if (r.MissingIds.Count > 0)
            s.Add(T("IDs not found in Part-DB", XStyle.Bold), T(string.Join(", ", r.MissingIds), XStyle.Red));
        foreach (var w in r.Bom.Warnings) s.Add(T("Warning", XStyle.Bold), T(w, XStyle.Yellow));
        return s;
    }
}
