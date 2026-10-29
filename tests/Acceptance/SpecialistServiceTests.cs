using Cultivation.Domain;
using Cultivation.Simulation;

internal static class SpecialistServiceTests
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static WorldState World()
    {
        var w = MortalCultivationScenario.Create(); w.PlayerAutoSchedule = false;
        var doctor = w.Characters.Single(c => c.TestProfession == TestProfession.Physician);
        w.Characters = w.Characters.Select(c => c.Id == w.ControlledCharacterId ? c with { DemoLocationId = doctor.DemoLocationId, HexPosition = doctor.HexPosition, RegionId = doctor.RegionId, Health = 50, EquippedWeaponId = null } : c).ToList();
        foreach (var (item, amount) in new[] { ("spirit-stone", 200L), ("herb", 30L), ("iron-material", 10L), ("beast-hide", 10L) })
            w.Ledger.Add(new(w.NextSequence++, 0, "service-test:" + item, "$genesis", w.ControlledCharacterId, item, amount, "Genesis"));
        WorldIntegrity.Validate(w); return w;
    }
    static ActionIntent Request(WorldState w, string service, ServiceMaterialSource source, string id = "request")
    {
        var role = service is "medical" or "severe-injury" ? TestProfession.Physician : service is "ointment" or "bleeding-powder" ? TestProfession.Pharmacist : TestProfession.Smith;
        return new(id, w.ControlledCharacterId, w.Characters.Single(c => c.TestProfession == role).Id, service, 1, 0, DecisionSource.Player,
            ActionKind.RequestLifeService, LimitPrice: PaidLifeServices.Quote(w, service, source), ServiceMaterials: source);
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Six specialists replace active alchemists without changing population or legacy saves", () =>
        {
            var old = TimeScenario.Create(); int count = old.Characters.Count; var w = MedicineOnlyProfile.ApplyFresh(old);
            foreach (var role in Enum.GetValues<TestProfession>().Where(r => r != TestProfession.None)) Check(w.Characters.Count(c => c.TestProfession == role) == 1, "Role count wrong " + role);
            Check(w.Characters.Count == count && w.Characters.All(c => c.Aspiration != Aspiration.Alchemy && c.Identity != CharacterIdentity.Alchemist) && !w.Aspirations.Any(a => a.Id == Aspiration.Alchemy), "Active alchemist retained/population changed");
            var restored = WorldCodec.Clone(w); Check(restored.SpecialistRules is not null && restored.PaidServiceRules is not null, "Service snapshot lost");
            Check(TimeScenario.Create().Characters.Any(c => c.Aspiration == Aspiration.Alchemy), "Legacy fixture was overwritten");
        }),
        ("Specialist quotes support client materials and provider materials", () =>
        {
            var w = World();
            foreach (var (service, labor, all) in new[] { ("medical",5L,9L),("severe-injury",15L,39L),("ointment",3L,11L),("bleeding-powder",5L,17L),("iron-sword",10L,20L),("leather-armor",10L,23L) })
            { Check(PaidLifeServices.Quote(w, service, ServiceMaterialSource.Client) == labor, "Labor quote wrong"); Check(PaidLifeServices.Quote(w, service, ServiceMaterialSource.Provider) == all, "Material quote wrong"); }
        }),
        ("Paid medical work reserves then pays once and preserves save progress", () =>
        {
            foreach (var source in Enum.GetValues<ServiceMaterialSource>())
            {
                var w = World(); var i = Request(w, "medical", source); long doctorMoney = Inventory.Balance(w, i.TargetId, "spirit-stone");
                long clientMoney = Inventory.Balance(w, i.ActorId, "spirit-stone"); long fee = i.LimitPrice!.Value;
                var e = new SimulationEngine(w); e.Submit(i); Check(e.Step().Single().Accepted, "Medical commission rejected");
                var reserved = e.Snapshot(); Check(reserved.LifeSystem!.Work.Single().Service is not null && Inventory.Balance(reserved, i.TargetId, "spirit-stone") == doctorMoney, "Provider paid early");
                e = new(WorldCodec.Clone(reserved)); e.Step(); var final = e.Snapshot();
                Check(final.PaidServiceReceipts.Single().Success && Inventory.Balance(final, i.TargetId, "spirit-stone") == doctorMoney + fee && Inventory.Balance(final, i.ActorId, "spirit-stone") == clientMoney - fee, "Fee duplicated or missing");
                Check(final.Characters.Single(c => c.Id == i.ActorId).Health == 70, "Medical result wrong");
                e.Submit(i); Check(!e.Step().Single().Accepted && e.Snapshot().PaidServiceReceipts.Count == 1, "Duplicate commission paid");
            }
        }),
        ("Paid pharmacy and smithing transfer actual unique products to customer", () =>
        {
            foreach (string service in new[] { "ointment", "iron-sword", "leather-armor" })
            {
                var w = World();
                if (service == "ointment") w.Ledger.RemoveAll(l => l.To == w.ControlledCharacterId && l.ResourceId == "ointment");
                var i = Request(w, service, ServiceMaterialSource.Provider); long providerStock = Inventory.Balance(w, i.TargetId, service); var e = new SimulationEngine(w); e.Submit(i); Check(e.Step().Single().Accepted, "Production commission rejected " + service);
                for (int n = 0; n < 4; n++) e.Step(); var final = e.Snapshot(); var receipt = final.PaidServiceReceipts.Single();
                Check(receipt.Success && receipt.Output is not null && Inventory.Balance(final, i.ActorId, receipt.Output) > 0 && Inventory.Balance(final, i.TargetId, receipt.Output) == (service == "ointment" ? providerStock : 0), "Output not delivered: " + service + " " + System.Text.Json.JsonSerializer.Serialize(receipt));
                if (service != "ointment") Check(final.LifeSystem!.Equipment.Any(g => g.Id == receipt.Output && g.DefinitionId == service), "Unique quality equipment lost");
            }
        }),
        ("Paid service abort refunds both supplied materials and all reserved fees", () =>
        {
            foreach (var source in Enum.GetValues<ServiceMaterialSource>())
            {
                var w = World(); var i = Request(w, "medical", source); var before = new[] { "herb", "spirit-stone" }.ToDictionary(x => x, x => Inventory.Balance(w, i.ActorId, x));
                long providerHerbs = Inventory.Balance(w, i.TargetId, "herb"); var e = new SimulationEngine(w); e.Submit(i); e.Step();
                w = e.Snapshot(); w.Characters = w.Characters.Select(c => c.Id == i.TargetId ? c with { SeverelyInjured = true } : c).ToList();
                e = new(w); e.Step(); var final = e.Snapshot();
                Check(!final.PaidServiceReceipts.Single().Success && final.LifeSystem!.Work.Count == 0, "Failed service not cancelled");
                Check(before.All(p => Inventory.Balance(final, i.ActorId, p.Key) == p.Value) && Inventory.Balance(final, i.TargetId, "herb") == providerHerbs, "Refund incomplete");
                Check(final.Characters.Single(c => c.Id == i.ActorId).Health == 50, "Failed treatment gave HP");
            }
        }),
        ("Paid service attack interrupts provider and refunds without making workers invulnerable", () =>
        {
            var w = World(); var i = Request(w, "medical", ServiceMaterialSource.Client);
            long stones = Inventory.Balance(w, i.ActorId, "spirit-stone"), herbs = Inventory.Balance(w, i.ActorId, "herb");
            var e = new SimulationEngine(w); e.Submit(i); e.Step(); w = e.Snapshot();
            var doctor = w.Characters.Single(c => c.Id == i.TargetId);
            string attacker = w.Characters.First(c => c.Id != i.ActorId && c.Id != i.TargetId && c.MonsterKind == MonsterKind.None).Id;
            w.Characters = w.Characters.Select(c => c.Id == doctor.Id ? c with { Hostile = true } : c.Id == attacker ? c with { Health = 100, BaseAttack = 100, RegionId = doctor.RegionId, DemoLocationId = doctor.DemoLocationId, HexPosition = doctor.HexPosition } : c).ToList();
            e = new(w); e.Submit(new("service-assault", attacker, doctor.Id, "combat", 1, 0, DecisionSource.Npc, ActionKind.Engage));
            Check(e.Step().Single().Accepted, "Busy provider became immune to attack"); var final = e.Snapshot();
            Check(final.PaidServiceReceipts.Single().Success == false && Inventory.Balance(final, i.ActorId, "spirit-stone") == stones && Inventory.Balance(final, i.ActorId, "herb") == herbs, "Attack refund incomplete");
        }),
        ("Paid service validation blocks bad quotes absent demand and double booking", () =>
        {
            var w = World(); var i = Request(w, "medical", ServiceMaterialSource.Provider);
            Check(ActionValidator.Validate(w, i with { LimitPrice = 1 }) is not null, "Bad quote accepted");
            var e = new SimulationEngine(w); e.Submit(i); e.Step(); w = e.Snapshot();
            var another = w.Characters.First(c => c.Id != i.ActorId && c.Id != i.TargetId && c.MonsterKind == MonsterKind.None);
            w.Characters = w.Characters.Select(c => c.Id == another.Id ? c with { Health = 50, DemoLocationId = w.Characters.Single(p => p.Id == i.TargetId).DemoLocationId, HexPosition = w.Characters.Single(p => p.Id == i.TargetId).HexPosition } : c).ToList();
            Check(ActionValidator.Validate(w, i with { Id = "second", ActorId = another.Id, Source = DecisionSource.Npc }) is not null, "Provider double booked");
        }),
        ("Specialists independently perform paid work and only train their chosen aspect", () =>
        {
            var w = World(); w.PlayerAutoSchedule = true; var e = new SimulationEngine(w);
            for (int n = 0; n < 100; n++) e.Step(true);
            var final = e.Snapshot();
            foreach (var role in new[] { TestProfession.Physician, TestProfession.Pharmacist, TestProfession.Smith })
            { var id = final.Characters.Single(c => c.TestProfession == role).Id; Check(final.PaidServiceReceipts.Any(r => r.Success && r.Contract.ProviderId == id), "No paid work " + role); }
            foreach (var role in new[] { TestProfession.SpiritTraining, TestProfession.BodyTraining, TestProfession.QiTraining })
            {
                var a = final.Characters.Single(c => c.TestProfession == role); var s = a.MortalCultivation!;
                Check(role == TestProfession.QiTraining ? s.QiInitiationExperience > 0 && s.Experience.Count == 0 : s.Experience.Count > 0 && s.Experience.Keys.All(k => MortalCultivationCalculator.Aspect(k) == (role == TestProfession.SpiritTraining ? CultivationAspect.Spirit : CultivationAspect.Body)) && s.QiInitiationExperience == 0, "Wrong specialist training " + role);
            }
            Check(final.Ledger.Where(l => l.From == "$genesis").All(l => l.Tick == 0), "Infinite test material issuance");
        })
    };
}
