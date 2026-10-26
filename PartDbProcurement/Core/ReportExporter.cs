namespace PartDbProcurement.Core;

/// <summary>Turns a ProcurementReport into an Excel workbook.</summary>
public static class ReportExporter
{
    public static string DefaultFileName(ProcurementReport r) => r.IsCombined
        ? $"{string.Join("+", r.Projects.Select(p => $"{p.Bom.ProjectName}_{p.Builds}x"))}_order_{r.PulledAt:yyyy-MM-dd}.xlsx"
        : $"{r.Projects[0].Bom.ProjectName}_order_{r.Projects[0].Builds}x_{r.PulledAt:yyyy-MM-dd}.xlsx";

    /// <param name="remembered">Notepad items to add to the order list with the reference REMEMBER.</param>
    public static void Save(ProcurementReport r, string path, IReadOnlyList<RememberItem>? remembered = null)
    {
        remembered ??= Array.Empty<RememberItem>();
        XlsxWriter.Save(path, new[] { OrderSheet(r, remembered), CoverageSheet(r) }.Concat(PickSheets(r))
            .Concat(new[] { NoIdSheet(r), ExcludedSheet(r), InfoSheet(r, remembered) }).ToList());
    }

    private static XCell N(double v, XStyle s = XStyle.Integer) => new(v, s);
    private static XCell T(string? v, XStyle s = XStyle.Default) => new(v ?? "", s);
    private static XCell H(string v) => new(v, XStyle.Header);

    private static XSheet OrderSheet(ProcurementReport r, IReadOnlyList<RememberItem> remembered)
    {
        var s = new XSheet("Order list");
        s.Add(new XCell(r.IsCombined ? $"Combined order: {r.Title}" : $"{r.Projects[0].Bom.ProjectName} - order for {r.Projects[0].Builds} build(s)", XStyle.Title));
        s.Add(T($"Part-DB data pulled {r.PulledAt:yyyy-MM-dd HH:mm}. To order = need + minimum stock - stock, raised to the smallest price break."));
        s.Blank();
        s.HeaderRow = 3;
        s.Add(H("Supplier"), H("SPN"), H("MPN"), H("Manufacturer"), H("Part-DB ID"), H("Name"), H("Description"),
              H("Need"), H("Stock"), H("Min. stock"), H("To order"), H("Order qty"), H("Unit price"), H("Currency"),
              H("Line total"), H("References"));
        s.Widths = new double[] { 14, 26, 22, 16, 10, 22, 36, 8, 8, 10, 9, 10, 11, 9, 12, 40 };

        var lines = r.OrderLines.ToList();
        // Part-DB lines and notepad items in one table, sorted by supplier, then MPN / order number.
        var rows = lines.Select(l => (Supplier: l.Offer?.Supplier ?? "", Key: l.Part?.Mpn ?? l.Bom.Mpn, Cells: PartRow(r, l)))
            .Concat(remembered.Select(i => (i.Supplier, Key: i.OrderNumber.Length > 0 ? i.OrderNumber : i.Description, Cells: RememberRow(i))))
            .OrderBy(x => string.IsNullOrEmpty(x.Supplier) ? "~" : x.Supplier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var x in rows) s.Add(x.Cells);

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
        if (remembered.Count > 0) s.Add(T($"{remembered.Count} line(s) from the notepad ({RememberItem.Reference}) have no price and are not in the totals."));
        if (lines.Count == 0) s.Add(new XCell("Nothing to order from Part-DB: stock covers all builds and minimum stock levels.", XStyle.Green));
        return s;
    }

    private static XCell[] PartRow(ProcurementReport r, ProcurementLine l)
    {
        var hl = l.Status == LineStatus.Short || l.Status == LineStatus.NotInPartDb ? XStyle.Red : XStyle.Default;
        return new[] { T(l.Offer?.Supplier is { Length: > 0 } sup ? sup : "(no supplier)", hl), T(l.Offer?.Spn),
            T(l.Part?.Mpn ?? l.Bom.Mpn), T(l.Part?.Manufacturer), T(l.Bom.PartDbId), T(l.Part?.Name ?? l.Bom.Value),
            T(l.Part?.Description ?? l.Bom.Description), N(l.Need), N(l.Stock), N(l.MinStock), N(l.ToOrder),
            N(l.OrderQty), l.Price == null ? T("") : new XCell(l.Price.UnitPrice, XStyle.Price), T(l.Price?.Currency),
            l.LineTotal == null ? T("") : new XCell(l.LineTotal.Value, XStyle.Money), T(l.ReferenceText(r.IsCombined)) };
    }

    /// <summary>A notepad item: order number in the SPN column, remarks as description, reference REMEMBER.</summary>
    private static XCell[] RememberRow(RememberItem i) => new[]
    {
        T(i.Supplier.Length > 0 ? i.Supplier : "(no supplier)"), T(i.OrderNumber), T(""), T(""), T(""), T(i.Description), T(i.Remarks),
        T(""), T(""), T(""), N(i.Quantity), N(i.Quantity), T(""), T(""), T(""), T(RememberItem.Reference, XStyle.Yellow),
    };

    private static XSheet CoverageSheet(ProcurementReport r)
    {
        var s = new XSheet("Stock coverage");
        s.HeaderRow = 0;
        // One "per build" column per project.
        var perBuild = r.IsCombined ? r.Projects.Select(p => H($"{p.Bom.ProjectName} per build")) : new[] { H("Per build") };
        s.Add(new[] { H("Status"), H("Part-DB ID"), H("Name"), H("MPN"), H("Value") }.Concat(perBuild).Concat(new[] { H("Need"), H("Stock"),
              H("Min. stock"), H("After build"), H("Short for build"), H("To order"), H("Storage location"),
              H("Supplier"), H("SPN"), H("Other suppliers"), H("References"), H("Part-DB") }).ToArray());
        s.Widths = new double[] { 20, 10, 22, 22, 14 }.Concat(r.Projects.Select(_ => r.IsCombined ? 14d : 9d))
            .Concat(new double[] { 8, 8, 10, 11, 13, 9, 18, 14, 26, 30, 40, 10 }).ToArray();
        foreach (var l in r.Lines)
        {
            var st = l.Status switch
            {
                LineStatus.Ok => XStyle.Green,
                LineStatus.BelowMinimum => XStyle.Yellow,
                LineStatus.StockUnknown => XStyle.Yellow,
                _ => XStyle.Red,
            };
            s.Add(new[] { T(l.StatusText, st), T(l.Bom.PartDbId), T(l.Part?.Name ?? ""), T(l.Part?.Mpn ?? l.Bom.Mpn), T(l.Bom.Value) }
                .Concat(r.Projects.Select(p => N(l.PerBuildIn(p))))
                .Concat(new[] { N(l.Need), N(l.Stock), N(l.MinStock), N(l.AfterBuild), N(l.ShortForBuild), N(l.ToOrder),
                T(string.Join(", ", l.Part?.StorageLocations ?? new())), T(l.Offer?.Supplier), T(l.Offer?.Spn),
                T(l.OtherOffers), T(l.ReferenceText(r.IsCombined)),
                l.Part == null ? T("") : XCell.Link(l.Part.Url, "open") }).ToArray());
        }
        return s;
    }

    /// <summary>One picking and placement list per project, sorted by storage location so the parts can be picked in one round.</summary>
    private static IEnumerable<XSheet> PickSheets(ProcurementReport r)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in r.Projects)
        {
            // Sheet names are limited to 31 characters and must be unique.
            var name = $"Pick {p.Bom.ProjectName}";
            if (name.Length > 31) name = name[..31];
            for (int n = 2; !names.Add(name); n++) name = $"{(name.Length > 26 ? name[..26] : name)} ({n})";

            var s = new XSheet(name);
            s.Add(new XCell($"{p.Bom.ProjectName} - picking and placement for {p.Builds} build(s)", XStyle.Title));
            s.Add(T($"Part-DB data pulled {r.PulledAt:yyyy-MM-dd HH:mm}. Need = per build x {p.Builds} build(s), for this project only."));
            s.Blank();
            s.HeaderRow = 3;
            s.Add(H("Name"), H("MPN"), H("Value"), H("Per build"), H("Need"), H("Storage location"), H("Supplier"), H("SPN"), H("References"), H("Part-DB"));
            s.Widths = new double[] { 22, 22, 14, 9, 8, 22, 14, 26, 50, 10 };

            var rows = r.Lines.Select(l => (Line: l, Use: l.Uses.FirstOrDefault(u => u.Project == p))).Where(x => x.Use != null)
                .Select(x => (Location: string.Join(", ", x.Line.Part?.StorageLocations ?? new()), x.Line, Group: x.Use!.Group))
                .OrderBy(x => x.Location.Length == 0 ? "~" : x.Location, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Group.References.OrderBy(f => f, RefComparer.Instance).FirstOrDefault() ?? "", RefComparer.Instance);
            foreach (var (location, l, g) in rows)
                s.Add(T(l.Part?.Name ?? ""), T(l.Part?.Mpn ?? g.Mpn), T(g.Value), N(g.QuantityPerBuild), N(g.QuantityPerBuild * p.Builds),
                    T(location), T(l.Offer?.Supplier), T(l.Offer?.Spn), T(g.ReferenceList),
                    l.Part == null ? T("") : XCell.Link(l.Part.Url, "open"));
            // Parts without a Part-DB ID still have to be picked and placed.
            foreach (var g in p.Bom.WithoutId)
                s.Add(T(""), T(g.Mpn), T(g.Value, XStyle.Red), N(g.QuantityPerBuild), N(g.QuantityPerBuild * p.Builds),
                    T("(no Part-DB ID)", XStyle.Red), T(""), T(""), T(g.ReferenceList));
            yield return s;
        }
    }

    private static XSheet NoIdSheet(ProcurementReport r)
    {
        var s = new XSheet("No Part-DB ID");
        if (r.Projects.All(p => p.Bom.WithoutId.Count == 0))
        {
            s.Add(new XCell("All counted symbols have a Part-DB ID.", XStyle.Green));
            return s;
        }
        // Without an ID, lines of different projects cannot be matched, so they are listed per project.
        var head = new List<XCell> { H("Value"), H("Footprint"), H("MPN"), H("Description"), H("Per build"), H("Need"), H("References"), H("Sheet") };
        var widths = new List<double> { 16, 36, 22, 36, 9, 8, 40, 16 };
        if (r.IsCombined) { head.Insert(0, H("Project")); widths.Insert(0, 16); }
        s.HeaderRow = 0;
        s.Add(head.ToArray());
        s.Widths = widths.ToArray();
        foreach (var p in r.Projects)
            foreach (var g in p.Bom.WithoutId)
            {
                var row = new List<XCell> { T(g.Value, XStyle.Red), T(g.Footprint), T(g.Mpn), T(g.Description), N(g.QuantityPerBuild),
                    N(g.QuantityPerBuild * p.Builds), T(g.ReferenceList), T(string.Join(", ", g.Sheets)) };
                if (r.IsCombined) row.Insert(0, T(p.Bom.ProjectName));
                s.Add(row.ToArray());
            }
        return s;
    }

    private static XSheet ExcludedSheet(ProcurementReport r)
    {
        var s = new XSheet("Excluded");
        var head = new List<XCell> { H("Reference"), H("Value"), H("Reason"), H("Part-DB ID"), H("Sheet") };
        var widths = new List<double> { 12, 18, 18, 10, 16 };
        if (r.IsCombined) { head.Insert(0, H("Project")); widths.Insert(0, 16); }
        s.HeaderRow = 0;
        s.Add(head.ToArray());
        s.Widths = widths.ToArray();
        foreach (var p in r.Projects)
            foreach (var (sym, why) in p.Bom.Excluded.OrderBy(e => e.Symbol.Reference, RefComparer.Instance))
            {
                var row = new List<XCell> { T(sym.Reference), T(sym.Value), T(why), T(sym.PartDbId), T(sym.SheetName) };
                if (r.IsCombined) row.Insert(0, T(p.Bom.ProjectName));
                s.Add(row.ToArray());
            }
        return s;
    }

    private static XSheet InfoSheet(ProcurementReport r, IReadOnlyList<RememberItem> remembered)
    {
        var s = new XSheet("Info");
        s.Widths = new double[] { 26, 90 };
        s.Add(new XCell("Procurement report", XStyle.Title));
        foreach (var p in r.Projects)
        {
            if (r.IsCombined) s.Blank();
            s.Add(T("Project", XStyle.Bold), T(p.Bom.ProjectFile));
            s.Add(T("Builds", XStyle.Bold), N(p.Builds));
            s.Add(T("Schematic last saved", XStyle.Bold), T(p.Bom.NewestSchematicWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            s.Add(T("Schematic files", XStyle.Bold), T(string.Join(", ", p.Bom.SchematicFiles.Select(Path.GetFileName))));
            s.Add(T("Parts counted per build", XStyle.Bold), N(p.Bom.Counted.Count));
            s.Add(T("BOM lines with Part-DB ID", XStyle.Bold), N(p.Bom.WithId.Count));
            s.Add(T("BOM lines without ID", XStyle.Bold), N(p.Bom.WithoutId.Count));
            s.Add(T("Excluded symbols", XStyle.Bold), N(p.Bom.Excluded.Count));
            foreach (var w in p.Bom.Warnings) s.Add(T("Warning", XStyle.Bold), T(w, XStyle.Yellow));
        }
        if (r.IsCombined) s.Blank();
        s.Add(T("Part-DB", XStyle.Bold), T(r.PartDbUrl));
        s.Add(T("Part-DB data pulled", XStyle.Bold), T(r.PulledAt.ToString("yyyy-MM-dd HH:mm:ss")));
        if (r.IsCombined) s.Add(T("Part-DB IDs (all projects)", XStyle.Bold), N(r.Lines.Count));
        s.Add(T("Lines to order", XStyle.Bold), N(r.OrderLines.Count()));
        if (remembered.Count > 0) s.Add(T("Notepad items (REMEMBER)", XStyle.Bold), N(remembered.Count));
        if (r.MissingIds.Count > 0)
            s.Add(T("IDs not found in Part-DB", XStyle.Bold), T(string.Join(", ", r.MissingIds), XStyle.Red));
        return s;
    }
}
