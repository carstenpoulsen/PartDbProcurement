namespace PartDbProcurement.Core;

/// <summary>An ad hoc item to remember when ordering, e.g. solder tip tinner. Kept in the notepad until the user removes it.</summary>
public sealed class RememberItem
{
    public const string Reference = "REMEMBER";

    public string Description { get; set; } = "";
    /// <summary>Optional; groups the item with that supplier's lines on the order list.</summary>
    public string Supplier { get; set; } = "";
    /// <summary>Supplier or manufacturer order number, if known.</summary>
    public string OrderNumber { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Remarks { get; set; } = "";
    /// <summary>Offered for the order list. Untick to keep an item on the notepad without ordering it this time.</summary>
    public bool OnOrderList { get; set; } = true;
    public DateTime Added { get; set; } = DateTime.Now;
    /// <summary>Last time the item was put on an exported order list.</summary>
    public DateTime? LastOrdered { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(OrderNumber);

    /// <summary>"SOLDER TIP TIN (ACTIVATOR) 26.6G ×1 (Digi-Key 1234-ND)" for messages.</summary>
    public string Summary
    {
        get
        {
            var named = !string.IsNullOrWhiteSpace(Description);
            var where = string.Join(" ", new[] { Supplier, named ? OrderNumber : "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return $"{(named ? Description : OrderNumber)} ×{Quantity}{(where.Length > 0 ? $" ({where})" : "")}";
        }
    }
}
