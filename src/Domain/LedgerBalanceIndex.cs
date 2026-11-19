namespace Cultivation.Domain;

// A derived index used only on the engine's private transaction copy. Never serialized.
internal sealed class LedgerBalanceIndex(List<LedgerEntry> ledger)
{
    private readonly List<LedgerEntry> source = ledger;
    private readonly Dictionary<(string, string), long> balances = [];
    private int cursor;
    internal long Balance(List<LedgerEntry> rows, string owner, string item)
    {
        if (!ReferenceEquals(rows, source) || rows.Count < cursor) throw new InvalidOperationException("Transaction ledger must remain append-only");
        while (cursor < rows.Count)
        {
            var e = rows[cursor++];
            if (!e.From.StartsWith('$') || e.From.StartsWith("$escrow:", StringComparison.Ordinal) || e.From.StartsWith("$mission:", StringComparison.Ordinal))
                balances[(e.From, e.ResourceId)] = checked(balances.GetValueOrDefault((e.From, e.ResourceId)) - e.Quantity);
            balances[(e.To, e.ResourceId)] = checked(balances.GetValueOrDefault((e.To, e.ResourceId)) + e.Quantity);
        }
        return balances.GetValueOrDefault((owner, item));
    }
}
