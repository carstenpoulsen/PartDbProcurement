using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PartDbProcurement.Core;

public sealed class PriceBreak
{
    public double MinQuantity { get; init; } = 1;
    /// <summary>Price per single unit (Part-DB stores price per "price related quantity").</summary>
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "";
}

public sealed class SupplierOffer
{
    public string Supplier { get; init; } = "";
    public string Spn { get; init; } = "";
    public bool Obsolete { get; init; }
    public List<PriceBreak> Prices { get; } = new();
    public double Moq => Prices.Count > 0 ? Prices.Min(p => p.MinQuantity) : 1;

    public PriceBreak? PriceFor(double qty) =>
        Prices.Where(p => p.MinQuantity <= qty).OrderByDescending(p => p.MinQuantity).FirstOrDefault()
        ?? Prices.OrderBy(p => p.MinQuantity).FirstOrDefault();
}

public sealed class PartDbPart
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Mpn { get; init; } = "";
    public string Ipn { get; init; } = "";
    public double Stock { get; init; }
    public double MinStock { get; init; }
    public bool StockUnknown { get; init; }
    public List<string> StorageLocations { get; } = new();
    public List<SupplierOffer> Offers { get; } = new();
    public string Url { get; init; } = "";
}

public sealed class PartDbException : Exception
{
    public PartDbException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Read-only client for the Part-DB REST API (GET /api/parts/{id}).</summary>
public sealed class PartDbClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly ConcurrentDictionary<string, Task<string>> _iriNames = new();

    public PartDbClient(string baseUrl, string token)
    {
        _base = baseUrl.Trim().TrimEnd('/');
        // Accept the KiCad API URL or a "/en/..." URL by mistake: keep scheme://host:port only.
        if (Uri.TryCreate(_base, UriKind.Absolute, out var u)) _base = u.GetLeftPart(UriPartial.Authority);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string BaseUrl => _base;

    public void Dispose() => _http.Dispose();

    /// <summary>Checks URL and token by reading the current user's tokens info endpoint.</summary>
    public async Task<string> TestAsync(CancellationToken ct)
    {
        using var resp = await Send(_base + "/api/tokens/current", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var name = Str(doc.RootElement, "name");
        return string.IsNullOrEmpty(name) ? "Connected." : $"Connected (token \"{name}\").";
    }

    public async Task<(Dictionary<string, PartDbPart> Parts, List<string> Missing)> GetPartsAsync(IEnumerable<string> ids, IProgress<int>? progress, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var result = new ConcurrentDictionary<string, PartDbPart>();
        var errors = new ConcurrentBag<string>();
        using var gate = new SemaphoreSlim(6);
        int done = 0;
        var tasks = list.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try { result[id] = await GetPartAsync(id, ct); }
            catch (PartDbException ex) when (ex.Message.StartsWith("404")) { errors.Add(id); }
            finally { gate.Release(); progress?.Report(Interlocked.Increment(ref done)); }
        });
        await Task.WhenAll(tasks);
        return (new Dictionary<string, PartDbPart>(result), errors.OrderBy(x => x).ToList());
    }

    public async Task<PartDbPart> GetPartAsync(string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new PartDbException($"404 Invalid Part-DB ID '{id}'");
        using var resp = await Send($"{_base}/api/parts/{id}", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;

        var part = new PartDbPart
        {
            Id = id,
            Name = Str(r, "name"),
            Description = StripHtml(Str(r, "description")),
            Mpn = Str(r, "manufacturer_product_number"),
            Ipn = Str(r, "ipn"),
            Manufacturer = await NameOf(r, "manufacturer", ct),
            MinStock = Num(r, "minamount"),
            Stock = Num(r, "total_instock"),
            Url = $"{_base}/en/part/{id}",
            StockUnknown = r.TryGetProperty("partLots", out var lotsProbe) && lotsProbe.ValueKind == JsonValueKind.Array &&
                           lotsProbe.EnumerateArray().Any(l => Bool(l, "instock_unknown")),
        };

        if (r.TryGetProperty("partLots", out var lots) && lots.ValueKind == JsonValueKind.Array)
        {
            foreach (var lot in lots.EnumerateArray())
            {
                var loc = await NameOf(lot, "storage_location", ct);
                if (!string.IsNullOrEmpty(loc) && !part.StorageLocations.Contains(loc)) part.StorageLocations.Add(loc);
            }
        }

        if (r.TryGetProperty("orderdetails", out var ods) && ods.ValueKind == JsonValueKind.Array)
        {
            foreach (var od in ods.EnumerateArray())
            {
                var offer = new SupplierOffer
                {
                    Supplier = await NameOf(od, "supplier", ct),
                    Spn = Str(od, "supplierpartnr"),
                    Obsolete = Bool(od, "obsolete"),
                };
                if (od.TryGetProperty("pricedetails", out var pds) && pds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pd in pds.EnumerateArray())
                    {
                        var price = (decimal)NumD(pd, "price");
                        var per = Num(pd, "price_related_quantity");
                        if (per <= 0) per = 1;
                        offer.Prices.Add(new PriceBreak
                        {
                            MinQuantity = Math.Max(1, Num(pd, "min_discount_quantity")),
                            UnitPrice = price / (decimal)per,
                            Currency = await NameOf(pd, "currency", ct, "iso_code"),
                        });
                    }
                }
                part.Offers.Add(offer);
            }
        }
        return part;
    }

    private async Task<HttpResponseMessage> Send(string url, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await _http.GetAsync(url, ct); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new PartDbException($"Timeout contacting {_base}"); }
        catch (HttpRequestException ex) { throw new PartDbException($"Cannot reach {_base}: {ex.Message}", ex); }

        if (resp.IsSuccessStatusCode) return resp;
        var code = (int)resp.StatusCode;
        resp.Dispose();
        throw resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new PartDbException("401 The API token was rejected. Check the token in the settings."),
            HttpStatusCode.Forbidden => new PartDbException("403 The token has no permission to read parts."),
            HttpStatusCode.NotFound => new PartDbException($"404 Not found: {url}"),
            _ => new PartDbException($"{code} Part-DB returned an error for {url}"),
        };
    }

    /// <summary>Related entities come either embedded ({ "name": ... }) or as an IRI string ("/api/suppliers/3").</summary>
    private async Task<string> NameOf(JsonElement e, string prop, CancellationToken ct, string field = "name")
    {
        if (!e.TryGetProperty(prop, out var v)) return "";
        switch (v.ValueKind)
        {
            case JsonValueKind.Object:
                var n = Str(v, field);
                if (!string.IsNullOrEmpty(n)) return n;
                var iri = Str(v, "@id");
                return string.IsNullOrEmpty(iri) ? "" : await ResolveIri(iri, field, ct);
            case JsonValueKind.String:
                return await ResolveIri(v.GetString()!, field, ct);
            default:
                return "";
        }
    }

    private Task<string> ResolveIri(string iri, string field, CancellationToken ct) =>
        _iriNames.GetOrAdd(iri + "#" + field, async _ =>
        {
            try
            {
                using var resp = await Send(iri.StartsWith("http") ? iri : _base + iri, ct);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                return Str(doc.RootElement, field);
            }
            catch (PartDbException) { return iri; }
        });

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" :
        e.TryGetProperty(prop, out v) && v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "";

    private static bool Bool(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement e, string prop) => (double)NumD(e, prop);

    /// <summary>Numbers may arrive as JSON numbers or as strings (BigDecimal prices).</summary>
    private static decimal NumD(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        return 0;
    }

    private static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace("&amp;", "&").Trim();
}
