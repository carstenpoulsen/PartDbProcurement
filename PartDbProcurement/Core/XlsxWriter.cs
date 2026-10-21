using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace PartDbProcurement.Core;

/// <summary>Cell styles available in the workbook (index into cellXfs).</summary>
public enum XStyle
{
    Default = 0, Header = 1, Integer = 2, Price = 3, Money = 4,
    Red = 5, Yellow = 6, Green = 7, Bold = 8, BoldMoney = 9, Link = 10, Title = 11,
}

public readonly record struct XCell(object? Value, XStyle Style = XStyle.Default, bool IsFormula = false)
{
    public static XCell Link(string url, string text) =>
        new($"HYPERLINK(\"{url.Replace("\"", "\"\"")}\",\"{text.Replace("\"", "\"\"")}\")", XStyle.Link, true);
}

public sealed class XSheet
{
    public string Name { get; }
    public List<XCell[]> Rows { get; } = new();
    public double[] Widths { get; set; } = Array.Empty<double>();
    /// <summary>Row index (0-based) of the table header; enables freeze pane and autofilter.</summary>
    public int HeaderRow { get; set; } = -1;
    public XSheet(string name) => Name = name.Length > 31 ? name[..31] : name;
    public void Add(params XCell[] cells) => Rows.Add(cells);
    public void Blank() => Rows.Add(Array.Empty<XCell>());
}

/// <summary>Dependency-free .xlsx writer (Office Open XML, enough for formatted tables).</summary>
public static class XlsxWriter
{
    public static void Save(string path, IReadOnlyList<XSheet> sheets)
    {
        var tmp = path + ".tmp";
        try
        {
            WritePackage(tmp, sheets);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            // Never leave a stray .tmp file behind, e.g. when the target is open in Excel.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void WritePackage(string tmp, IReadOnlyList<XSheet> sheets)
    {
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            Put(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
            Put(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Put(zip, "xl/workbook.xml", Workbook(sheets));
            Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
            Put(zip, "xl/styles.xml", Styles);
            for (int i = 0; i < sheets.Count; i++) Put(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(sheets[i]));
        }
    }

    private static void Put(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string ContentTypes(int n)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 1; i <= n; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(IReadOnlyList<XSheet> sheets)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (int i = 0; i < sheets.Count; i++)
            sb.Append($"<sheet name=\"{Esc(SafeSheetName(sheets[i].Name))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets><definedNames>");
        for (int i = 0; i < sheets.Count; i++)
        {
            var s = sheets[i];
            if (s.HeaderRow < 0 || s.Rows.Count <= s.HeaderRow) continue;
            int cols = Math.Max(1, s.Rows[s.HeaderRow].Length);
            sb.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{Esc(SafeSheetName(s.Name).Replace("'", "''"))}'!$A${s.HeaderRow + 1}:${Col(cols - 1)}${LastTableRow(s) + 1}</definedName>");
        }
        return sb.Append("</definedNames></workbook>").ToString().Replace("<definedNames></definedNames>", "");
    }

    private static string WorkbookRels(int n)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 1; i <= n; i++)
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        sb.Append($"<Relationship Id=\"rId{n + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    private static int LastTableRow(XSheet s)
    {
        int last = s.HeaderRow;
        for (int r = s.HeaderRow + 1; r < s.Rows.Count && s.Rows[r].Length > 0; r++) last = r;
        return last;
    }

    private static string Sheet(XSheet s)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
        if (s.HeaderRow >= 0)
            sb.Append($"<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"{s.HeaderRow + 1}\" topLeftCell=\"A{s.HeaderRow + 2}\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (s.Widths.Length > 0)
        {
            sb.Append("<cols>");
            for (int i = 0; i < s.Widths.Length; i++)
                sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{s.Widths[i].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (int r = 0; r < s.Rows.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            var row = s.Rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                var cell = row[c];
                if (cell.Value == null && cell.Style == XStyle.Default) continue;
                var refName = Col(c) + (r + 1);
                var st = (int)cell.Style;
                switch (cell.Value)
                {
                    case null:
                        sb.Append($"<c r=\"{refName}\" s=\"{st}\"/>"); break;
                    case string f when cell.IsFormula:
                        sb.Append($"<c r=\"{refName}\" s=\"{st}\" t=\"str\"><f>{Esc(f)}</f></c>"); break;
                    case string str:
                        sb.Append($"<c r=\"{refName}\" s=\"{st}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(str)}</t></is></c>"); break;
                    case IFormattable num when cell.Value is int or long or double or decimal or float:
                        sb.Append($"<c r=\"{refName}\" s=\"{st}\"><v>{num.ToString(null, CultureInfo.InvariantCulture)}</v></c>"); break;
                    default:
                        sb.Append($"<c r=\"{refName}\" s=\"{st}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(cell.Value.ToString() ?? "")}</t></is></c>"); break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (s.HeaderRow >= 0 && s.Rows.Count > s.HeaderRow)
            sb.Append($"<autoFilter ref=\"A{s.HeaderRow + 1}:{Col(Math.Max(1, s.Rows[s.HeaderRow].Length) - 1)}{LastTableRow(s) + 1}\"/>");
        sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
        return sb.Append("</worksheet>").ToString();
    }

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.0000\"/></numFmts>" +
        "<fonts count=\"4\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><u/><sz val=\"11\"/><color rgb=\"FF0563C1\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"14\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"6\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E1F2\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8CBAD\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFE699\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFC6EFCE\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left/><right/><top style=\"thin\"><color auto=\"1\"/></top><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"12\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                    // 0 default
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +   // 1 header
        "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +            // 2 integer
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +          // 3 price
        "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +            // 4 money
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +                    // 5 red
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +                    // 6 yellow
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"5\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +                    // 7 green
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/>" +  // 8 bold
        "<xf numFmtId=\"4\" fontId=\"1\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyNumberFormat=\"1\"/>" + // 9 bold money
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                    // 10 link
        "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                    // 11 title
        "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

    private static string Col(int index)
    {
        var s = "";
        index++;
        while (index > 0) { int m = (index - 1) % 26; s = (char)('A' + m) + s; index = (index - 1) / 26; }
        return s;
    }

    private static string SafeSheetName(string n)
    {
        foreach (var ch in new[] { '\\', '/', '?', '*', '[', ']', ':' }) n = n.Replace(ch, '-');
        return n.Length > 31 ? n[..31] : n;
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20) sb.Append(ch); // drop invalid XML control chars
        return SecurityElement.Escape(sb.ToString()) ?? "";
    }
}
