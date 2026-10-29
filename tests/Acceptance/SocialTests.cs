using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

internal static class SocialTests
{
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static ActionIntent Act(string id, string actor, string target, ActionKind kind, string? offer = null) => new(id, actor, target, "social", 1, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind, SocialOfferId: offer);
    private static void Steps(SimulationEngine e, int n) { for (int i = 0; i < n; i++) e.Step(); }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Trusted lesson quote matches validation payment and save continuation", () =>
        {
            var w = SocialScenario.Create();
            w.Characters = w.Characters.Select(c => c.Id == "npc:00" ? c with { MentorId = "player" } : c).ToList();
            w.Relationships.AddRange(new[] { new RelationshipState("player", "npc:00", Trust: 50), new("npc:00", "player") });
            w.Ledger.RemoveAll(x => x.To == "npc:00" && x.ResourceId == "spirit-stone");
            w.Ledger.Add(new(w.NextSequence++, 0, "lesson-funds", "$genesis", "npc:00", "spirit-stone", 8, "Genesis"));
            var e = new SimulationEngine(w);
            long teacherBefore = Inventory.Balance(w, "player", "spirit-stone");
            var lesson = Act("discount-lesson", "npc:00", "player", ActionKind.StudyWithMentor);
            e.Submit(lesson); Check(e.Step().Single().Accepted, "Discounted funds rejected");
            var after = e.Snapshot();
            Check(Inventory.Balance(after, "npc:00", "spirit-stone") == 0 && Inventory.Balance(after, "player", "spirit-stone") == teacherBefore + 8, "Discount payment disagreed");
            Check(after.Characters.Single(c => c.Id == "npc:00").MethodExperience == 2, "Discount changed teaching result");
            e.Submit(lesson); e.Step();
            Check(Inventory.Balance(e.Snapshot(), "player", "spirit-stone") == teacherBefore + 8, "Duplicate lesson paid twice");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            e.Step(true); resumed.Step(true);
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(resumed.Snapshot()), "Discount continuation diverged");
        }),
        ("Lesson utility uses published quote and pricing remains bounded", () =>
        {
            var contact = new KnownContact("teacher", false, true, false, true, 0, 0, "contact:student", 0, 8);
            var o = new LocalObservation(new("student", "Student"), 0, 8, 0, 0, 0, 0, 0, 0, new(), "market:valley", 0, SocialRules: new(), Contacts: new[] { contact });
            var planner = new UtilityPlanner();
            Check(planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.StudyWithMentor), "Discount quote ignored");
            Check(!planner.Evaluate(o with { Contacts = new[] { contact with { LessonQuote = 10 } } }).Any(c => c.Intent.Kind == ActionKind.StudyWithMentor), "Unaffordable quote accepted");
            Check(!planner.Evaluate(o with { Contacts = new[] { contact with { Source = "global" } } }).Any(c => c.Intent.Kind == ActionKind.StudyWithMentor), "Global quote used");
            Check(SocialPricing.Lesson(new(), 49) == 10 && SocialPricing.Lesson(new(), 50) == 8, "Trust threshold incorrect");
            Check(SocialPricing.Lesson(new(LessonPrice: long.MaxValue, LessonTrustDiscountPercent: 100), 100) == 1, "Extreme discount overflowed");
            var w = SocialScenario.Create(); w.SocialRules = w.SocialRules! with { LessonTrustDiscountPercent = 101 };
            bool rejected = false; try { _ = new SimulationEngine(w); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid discount configuration loaded");
        }),
        ("Known relationship changes mission utility without changing reward", () =>
        {
            var actor = new CharacterState("a", "Alchemist") { AlchemyChance = 0.95, SocialWeight = 3 };
            var mission = new MissionObservation("task", "sect", "pill", 20, 120, 100, false, "board:sect:valley", 0);
            var o = new LocalObservation(actor, 0, 1000, 0, 0, 100, 1000, 50, 0, new(HerbPrice: 65, PillPrice: 110, HerbsPerPill: 1), "market:valley", 0, Missions: new[] { mission });
            var planner = new UtilityPlanner();
            double Score(double relation) => planner.Evaluate(o with { Missions = new[] { mission with { RelationshipWeight = relation } } }).First(c => c.Intent.Kind == ActionKind.AcceptMission).Score;
            Check(Score(0.5) > Score(-0.5), "Relationship did not affect mission valuation");
        }),
        ("Cooperation requires consent and aid moves real inventory", () =>
        {
            var w = SocialScenario.Create();
            w.Ledger.Add(new(w.NextSequence++, 0, "aid-seed", "$genesis", "player", "herb", 1, "Genesis"));
            var e = new SimulationEngine(w);
            e.Submit(Act("invite", "player", "npc:00", ActionKind.InviteCooperation)); e.Step();
            Check(e.Snapshot().Relationships.Count == 0, "Invitation forced cooperation");
            e.Submit(Act("spoof", "npc:00", "player", ActionKind.AcceptSocialOffer, "invite") with { Source = DecisionSource.Player });
            Check(!e.Step().Single().Accepted, "Player accepted for NPC");
            e.Submit(Act("accept", "npc:00", "player", ActionKind.AcceptSocialOffer, "invite")); e.Step();
            Check(e.Snapshot().Relationships.Count == 2 && e.Snapshot().Relationships.All(r => r.Cooperating), "Sparse mutual cooperation missing");
            Steps(e, 3); e.Submit(Act("aid", "player", "npc:00", ActionKind.AssistPartner));
            Check(e.Step().Single().Accepted, "Aid failed");
            var after = e.Snapshot();
            Check(Inventory.Balance(after, "player", "herb") == 0 && Inventory.Balance(after, "npc:00", "herb") == 1 && after.Relationships.Single(r => r.FromId == "npc:00").Trust == 10, "Aid or trust was fabricated");
        }),
        ("Recruitment requires leader authority and recipient acceptance", () =>
        {
            var e = new SimulationEngine(SocialScenario.Create());
            e.Submit(Act("forbidden", "npc:00", "npc:04", ActionKind.InviteRecruit));
            Check(!e.Step().Single().Accepted, "Nonleader recruited");
            e.Submit(Act("recruit", "player", "npc:04", ActionKind.InviteRecruit)); e.Step();
            Check(e.Snapshot().Characters.Single(c => c.Id == "npc:04").OrganizationId is null, "Recruitment forced membership");
            e.Submit(Act("join", "npc:04", "player", ActionKind.AcceptSocialOffer, "recruit"));
            Check(e.Step().Single().Accepted && e.Snapshot().Characters.Single(c => c.Id == "npc:04").Position == OrganizationPosition.Member, "Voluntary membership failed");
            e.Submit(Act("join-again", "npc:04", "player", ActionKind.AcceptSocialOffer, "recruit"));
            Check(!e.Step().Single().Accepted, "Offer accepted twice");
        }),
        ("Mentorship is consensual and lessons pay the teacher", () =>
        {
            var w = SocialScenario.Create(); var e = new SimulationEngine(w);
            e.Submit(Act("mentor-offer", "player", "npc:00", ActionKind.InviteApprentice)); e.Step();
            e.Submit(Act("become-student", "npc:00", "player", ActionKind.AcceptSocialOffer, "mentor-offer")); e.Step();
            Check(e.Snapshot().Characters.Single(c => c.Id == "npc:00").MentorId == "player", "Mentor not recorded");
            Steps(e, 5);
            long before = Inventory.Balance(e.Snapshot(), "player", "spirit-stone");
            e.Submit(Act("lesson", "npc:00", "player", ActionKind.StudyWithMentor));
            Check(e.Step().Single().Accepted, "Lesson rejected");
            var after = e.Snapshot(); var student = after.Characters.Single(c => c.Id == "npc:00");
            Check(student.MethodId == w.Advancement!.MethodId && student.MethodExperience == 2 && Inventory.Balance(after, "player", "spirit-stone") == before + w.SocialRules!.LessonPrice, "Knowledge or payment incorrect");
        }),
        ("Social offers expire and declines do not force relationships", () =>
        {
            var w = SocialScenario.Create(); w.SocialRules = w.SocialRules! with { OfferLifetimeTicks = 2 };
            var e = new SimulationEngine(w); e.Submit(Act("offer", "player", "npc:00", ActionKind.InviteCooperation)); e.Step(); Steps(e, 2);
            e.Submit(Act("late", "npc:00", "player", ActionKind.AcceptSocialOffer, "offer"));
            Check(!e.Step().Single().Accepted && e.Snapshot().SocialOffers.Single().Status == SocialOfferStatus.Expired, "Expired offer accepted");
            e = new(SocialScenario.Create()); e.Submit(Act("offer", "player", "npc:00", ActionKind.InviteCooperation)); e.Step();
            e.Submit(Act("decline", "npc:00", "player", ActionKind.DeclineSocialOffer, "offer")); e.Step();
            Check(e.Snapshot().SocialOffers.Single().Status == SocialOfferStatus.Declined && e.Snapshot().Relationships.Count == 0, "Decline forced a relationship");
        }),
        ("Social utility respects preference grudge and private offer source", () =>
        {
            var a = new CharacterState("a", "A") { SocialWeight = 3 };
            var offer = new ReceivedSocialOffer("offer", "b", SocialOfferKind.Cooperation, 30, 0, 0, "invitation:a", 0);
            var o = new LocalObservation(a, 0, 100, 0, 0, 0, 100, 0, 0, new(), "market:valley", 0, SocialRules: new(), SocialOffers: new[] { offer });
            var planner = new UtilityPlanner();
            Check(planner.Evaluate(o).First().Intent.Kind == ActionKind.AcceptSocialOffer, "Friendly actor rejected without reason");
            Check(planner.Evaluate(o with { SocialOffers = new[] { offer with { Grudge = 100 } } }).First().Intent.Kind == ActionKind.DeclineSocialOffer, "Grudge ignored");
            Check(!planner.Evaluate(o with { SocialOffers = new[] { offer with { Source = "global" } } }).Any(c => c.Intent.Kind == ActionKind.AcceptSocialOffer), "Used another actor's offer");
        }),
        ("Pending recruitment file save preserves autonomous consent", () =>
        {
            var e = new SimulationEngine(SocialScenario.Create()); e.Submit(Act("recruit", "player", "npc:04", ActionKind.InviteRecruit)); e.Step();
            string dir = Path.Combine(Path.GetTempPath(), "cultivation-social-" + Guid.NewGuid().ToString("N")); SimulationEngine restored;
            try { var path = Path.Combine(dir, "world.json"); var store = new JsonWorldStore(); store.Save(path, e.Snapshot()); restored = new(store.Load(path)); }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            for (int n = 0; n < 20; n++) { e.Step(true); restored.Step(true); }
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(restored.Snapshot()) && e.Snapshot().SocialOffers.Single().Status == SocialOfferStatus.Accepted, "Autonomous consent missing or divergent");
        }),
        ("Mentor cycles and duplicate sparse edges are rejected", () =>
        {
            foreach (bool cycle in new[] { true, false })
            {
                var w = SocialScenario.Create();
                if (cycle) { w.Characters[0] = w.Characters[0] with { MentorId = "npc:00" }; w.Characters[1] = w.Characters[1] with { MentorId = "player" }; }
                else w.Relationships.AddRange([new("player", "npc:00"), new("player", "npc:00")]);
                bool rejected = false; try { _ = new SimulationEngine(w); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Invalid social save accepted");
            }
        })
    };
}
