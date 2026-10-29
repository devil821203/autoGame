using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class CurrencyTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[] {
        ("Currency catalog separates active money from pending spirit stones", () => {
            var w = DemoWorld.Create();
            Check(w.Currencies!.Single(c => c.Kind == CurrencyKind.Money) is { Enabled: true, ResourceId: "spirit-stone", Name: "錢財" }, "Money mapping lost");
            Check(w.Currencies!.Single(c => c.Kind == CurrencyKind.SpiritStone) is { Enabled: false, ResourceId: null }, "Pending currency became usable");
            Check(w.Items.Single(i => i.Id == "spirit-stone").Name == "錢財", "Wrong money label");
            Check(WorldCodec.Digest(w) == WorldCodec.Digest(WorldCodec.Deserialize(WorldCodec.Serialize(w))), "Currency snapshot changed");
        }),
        ("Pending spirit currency cannot be enabled in a world snapshot", () => {
            var w = DemoWorld.Create();
            w.Currencies = w.Currencies!.Select(c => c.Kind == CurrencyKind.SpiritStone ? c with { Enabled = true, ResourceId = "new-stone" } : c).ToList();
            try { _ = new SimulationEngine(w); } catch (InvalidDataException e) { Check(e.Message.Contains("items/currencies.json"), "Missing config path"); return; }
            throw new Exception("Unconfirmed currency enabled");
        }),
        ("Legacy money save retains funds and ledger while renaming its display", () => {
            var w = DemoWorld.Create(); w.Currencies = null;
            w.Items = w.Items.Select(i => i.Id == "spirit-stone" ? i with { Name = "靈石" } : i).ToList();
            string directory = Path.Combine(Path.GetTempPath(), "currency-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                var store = new JsonWorldStore();
                string path = Path.Combine(directory, "legacy.json");
                store.Save(path, w); var loaded = store.Load(path);
                Check(loaded.Currencies is null && loaded.Items.Single(i => i.Id == "spirit-stone").Name == "錢財", "Legacy metadata incorrect");
                Check(loaded.Ledger.SequenceEqual(w.Ledger) && loaded.Rules == w.Rules, "Legacy economy changed");
                Check(Inventory.Balance(loaded, "player", "spirit-stone") == Inventory.Balance(w, "player", "spirit-stone"), "Money changed");
            } finally { Directory.Delete(directory, true); }
        })
    };
}
