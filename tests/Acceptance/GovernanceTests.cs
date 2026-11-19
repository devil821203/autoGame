using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.AI;

internal static class GovernanceTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static ActionIntent Act(string id, string actor, string target, ActionKind kind, long quantity = 1) => new(id, actor, target, kind == ActionKind.RewardMember ? "spirit-stone" : "organization", quantity, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, kind, OrganizationId: "sect");
    private static bool Run(SimulationEngine e, ActionIntent i) { e.Submit(i); return e.Step().Single().Accepted; }
    private static SimulationEngine Create(int capacity = 30)
    {
        var w = SocialScenario.Create();
        w.OrganizationPolicies.Add(new("sect", true, capacity));
        return new(w);
    }
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Player demo creates named controlled character without changing scenario rules", () =>
        {
            var w = PlayerDemoScenario.Create("  青玄  ");
            var e = new SimulationEngine(w);
            Check(w.DemoMapEnabled && w.Characters.Single(c => c.Id == w.ControlledCharacterId).Name == "青玄", "Name or authority incorrect");
            Check(w.Characters.Count == SocialScenario.Create().Characters.Count, "Creation duplicated characters");
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w))).Snapshot()), "Named world did not roundtrip");
            foreach (string invalid in new[] { " ", new string('a', 25), "a\nb" })
            {
                bool rejected = false; try { _ = PlayerDemoScenario.Create(invalid); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid name accepted");
            }
        }),
        ("World notifications reach only present observers and retain their event source", () =>
        {
            var w = AutonomousWorld.Create(); w.DemoMapEnabled = true; w.HerbRegeneration = new(2, 1, 10);
            w.Ledger.RemoveAll(e => e.To == "grove");
            w.Characters = w.Characters.Select(c => c.Id is "npc:00" or "npc:01" ? c with { DemoLocationId = "grove", Health = 100 } : c).ToList();
            var e = new SimulationEngine(w);
            e.Submit(new("depart", "npc:01", "town", "travel", 1, 0, DecisionSource.Npc, ActionKind.Travel)); e.Step();
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            e.Step(); resumed.Step();
            var after = e.Snapshot();
            Check(after.Notifications.Count == 1 && after.Notifications[0].RecipientId == "npc:00", "Notification leaked to town or traveler");
            Check(after.Events.Single(x => x.Sequence == after.Notifications[0].EventSequence).Kind == "ResourceRegenerated", "Source missing");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Notification continuation diverged");
            after.Notifications.Add(after.Notifications[0]);
            bool rejected = false; try { _ = new SimulationEngine(after); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Duplicate notification loaded");
        }),
        ("Herb regeneration is capped auditable and resumes at the same tick", () =>
        {
            var w = AutonomousWorld.Create();
            w.HerbRegeneration = new(IntervalTicks: 2, Amount: 3, Capacity: 5);
            w.Ledger.RemoveAll(x => x.To == "grove");
            var e = new SimulationEngine(w); e.Step();
            Check(Inventory.Balance(e.Snapshot(), "grove", "herb") == 0, "Early regeneration");
            var restored = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 5; n++) { e.Step(); restored.Step(); }
            var after = e.Snapshot();
            Check(Inventory.Balance(after, "grove", "herb") == 5, "Regeneration cap incorrect");
            Check(after.Ledger.Where(x => x.From == "$regeneration").Select(x => x.Quantity).SequenceEqual(new long[] { 3, 2 }), "Regeneration source incorrect");
            Check(after.Events.Count(x => x.Kind == "ResourceRegenerated") == 2, "Full capacity generated events");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(restored.Snapshot()), "Regeneration continuation diverged");
            after.HerbRegeneration = new(IntervalTicks: 0);
            bool rejected = false; try { _ = new SimulationEngine(after); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid regeneration loaded");
        }),
        ("Demo location rules apply equally and autonomous gathering travels to work and market", () =>
        {
            var w = AutonomousWorld.Create(); w.DemoMapEnabled = true;
            w.Characters = w.Characters.Select(c => c.Id == "npc:00" ? c with { Health = 100, GrowthWeight = 0, WealthWeight = 10 } : c).ToList();
            var e = new SimulationEngine(w);
            foreach (var actor in new[] { "player", "npc:00" })
            {
                var gather = new ActionIntent("wrong-place:" + actor, actor, "grove", "herb", 1, 0, actor == "player" ? DecisionSource.Player : DecisionSource.Npc, ActionKind.Gather);
                e.Submit(gather); Check(!e.Step().Single().Accepted, "Gather allowed outside grove");
            }
            e.Step(true);
            Check(e.Snapshot().Scheduled.Any(t => t.Intent.ActorId == "npc:00" && t.Intent.Kind == ActionKind.Travel && t.Intent.TargetId == "grove"), "NPC did not travel for gathering");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 40; n++) { e.Step(true); resumed.Step(true); }
            var after = e.Snapshot();
            Check(after.Memories.Any(m => m.ActorId == "npc:00" && m.Kind == ActionKind.Sell), "NPC never returned to town to sell");
            Check(after.Events.Any(x => x.Kind == "TravelCompleted" && x.Detail == "npc:00:grove") && after.Events.Any(x => x.Kind == "TravelCompleted" && x.Detail == "npc:00:town"), "Work/market travel missing");
            Check(WorldCodec.Digest(after) == WorldCodec.Digest(resumed.Snapshot()), "Spatial autonomy continuation diverged");
        }),
        ("Demo travel uses fixed ticks shared authority busy checks and save continuation", () =>
        {
            var w = SocialScenario.Create(); w.DemoMapEnabled = true;
            var e = new SimulationEngine(w);
            var travel = new ActionIntent("travel", "player", "grove", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel);
            Check(Run(e, travel), "Travel rejected");
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").DemoLocationId == "town", "Arrived early");
            Check(!Run(e, new("busy", "player", "market", "pill", 1, 0, DecisionSource.Player, ActionKind.Buy)), "Travel allowed business action");
            var restored = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 3; n++) { e.Step(); restored.Step(); }
            Check(e.Snapshot().Characters.Single(c => c.Id == "player").DemoLocationId == "grove", "Travel never arrived");
            Check(WorldCodec.Digest(e.Snapshot()) == WorldCodec.Digest(restored.Snapshot()), "Travel continuation diverged");
            Check(!Run(e, travel with { Id = "same" }), "Same-location travel allowed");
            Check(!Run(e, travel with { Id = "spoof", ActorId = "npc:00" }), "Player controlled NPC travel");
            Check(Run(e, travel with { Id = "npc-trip", ActorId = "npc:00", Source = DecisionSource.Npc }), "NPC cannot use same travel rules");
        }),
        ("Alchemy membership opportunity weighs materials success risk funds and deadline", () =>
        {
            var actor = new CharacterState("alchemist", "Alchemist") { AlchemyChance = 1, SocialWeight = 0, Caution = 1 };
            var notice = new OrganizationObservation(new("sect", true), "valley", 5, 0, 0, "admission:sect:valley", 0, MembershipPillReward: 120, MembershipDeadlineTick: 100);
            var o = new LocalObservation(actor, 0, 0, 2, 0, 0, 0, 0, 0, new(), "market:valley", 0, SocialRules: new(), Organizations: new[] { notice });
            var planner = new UtilityPlanner();
            bool Joins(LocalObservation x) => planner.Evaluate(x).Any(c => c.Intent.Kind == ActionKind.ApplyToOrganization);
            Check(Joins(o), "Owned recipe inputs ignored");
            Check(Joins(o with { Herbs = 0, Stones = 8, ShopHerbs = 2 }), "Affordable ingredients ignored");
            Check(!Joins(o with { Herbs = 0, Stones = 7, ShopHerbs = 2 }), "Insufficient ingredient funds ignored");
            Check(!Joins(o with { Herbs = 0, Stones = 8, ShopHerbs = 1 }), "Insufficient ingredient stock ignored");
            Check(!Joins(o with { Actor = actor with { AlchemyChance = 0 } }), "Unskilled actor assumed alchemy");
            Check(!Joins(o with { Actor = actor with { AlchemyChance = 0.01 } }), "Failure cost ignored");
            Check(!Joins(o with { Organizations = new[] { notice with { MembershipDeadlineTick = 6 } } }), "Impossible production deadline ignored");
        }),
        ("Member task opportunity changes admission utility without bypassing eligibility", () =>
        {
            var actor = new CharacterState("candidate", "Candidate") { SocialWeight = 0, WealthWeight = 1, Caution = 1 };
            var notice = new OrganizationObservation(new("sect", true), "valley", 5, 0, 0, "admission:sect:valley", 0, MembershipPillReward: 120);
            var o = new LocalObservation(actor, 0, 100, 0, 0, 0, 100, 0, 1, new(PillPrice: 20), "market:valley", 0,
                SocialRules: new(), Organizations: new[] { notice });
            var planner = new UtilityPlanner();
            bool Joins(LocalObservation x) => planner.Evaluate(x).Any(c => c.Intent.Kind == ActionKind.ApplyToOrganization);
            Check(Joins(o), "Profitable local membership opportunity ignored");
            Check(!Joins(o with { Organizations = new[] { notice with { MembershipPillReward = 10 } } }), "Loss-making opportunity attracted applicant");
            Check(!Joins(o with { Stones = 0 }), "Unfunded acquisition attracted applicant");
            Check(!Joins(o with { ShopPills = 0 }), "Missing supply ignored");
            Check(!Joins(o with { Organizations = new[] { notice with { Policy = new("sect", false) } } }), "Task bypassed closed applications");
            Check(!Joins(o with { Organizations = new[] { notice with { Policy = new("sect", true, MinimumReputation: 1) } } }), "Task bypassed reputation");
            Check(!planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.AcceptMission), "Outsider auto accepted task");
        }),
        ("Only active local member task terms enter admission observations", () =>
        {
            var e = Create();
            Run(e, Act("private-policy", "player", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", true, MissionsMembersOnly: true) });
            Check(Run(e, new("task", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("opportunity", 1, 120, 100, 120))), "Publication failed");
            var recorder = new ObservationRecorder();
            new SimulationEngine(e.Snapshot(), recorder).Step(true);
            var outsider = recorder.Seen.Single(o => o.Actor.Id == "npc:04");
            Check(outsider.Organizations!.Single(o => o.Policy.OrganizationId == "sect").MembershipPillReward == 120, "Public member opportunity missing");
            Check(outsider.Missions!.Count == 0, "Outsider received actionable private mission");
            Check(Run(e, new("cancel", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.CancelMission, MissionId: "opportunity")), "Cancel failed");
            recorder = new(); new SimulationEngine(e.Snapshot(), recorder).Step(true);
            Check(recorder.Seen.Single(o => o.Actor.Id == "npc:04").Organizations!.Single(o => o.Policy.OrganizationId == "sect").MembershipPillReward == 0, "Cancelled task still attracted admission");
        }),
        ("Actual mission delivery builds reputation and reciprocal trust without duplicate rewards", () =>
        {
            var e = Create();
            var w = e.Snapshot();
            w.SocialRules = w.SocialRules! with { MissionReputationPerItem = 2 };
            w.OrganizationPolicies[0] = new("sect", true, 30, 4);
            w.Characters = w.Characters.Select(c => c.Id == "npc:04" ? c with { Reputation = 0 } : c).ToList();
            e = new(w);
            Check(!Run(e, Act("too-early", "npc:04", "sect", ActionKind.ApplyToOrganization)), "Unproven applicant admitted");
            Check(Run(e, new("publish", "player", "sect", "pill", 2, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("service", 2, 1, 100, 2))), "Publication failed");
            Check(Run(e, new("accept", "npc:04", "sect", "pill", 1, 0, DecisionSource.Npc, ActionKind.AcceptMission, MissionId: "service")), "Acceptance failed");
            var delivery = new ActionIntent("delivery", "npc:04", "sect", "pill", 100, 0, DecisionSource.Npc, ActionKind.DeliverMission, MissionId: "service");
            Check(Run(e, delivery), "Delivery failed");
            var after = e.Snapshot();
            Check(after.Characters.Single(c => c.Id == "npc:04").Reputation == 4, "Reward used requested rather than accepted quantity");
            Check(after.Relationships.Any(r => r.FromId == "npc:04" && r.ToId == "player" && r.Trust == 1) && after.Relationships.Any(r => r.FromId == "player" && r.ToId == "npc:04" && r.Trust == 1), "Fulfillment trust missing");
            e.Submit(delivery); e.Step();
            Check(e.Snapshot().Characters.Single(c => c.Id == "npc:04").Reputation == 4 && e.Snapshot().Events.Count(x => x.Kind == "MissionReputation") == 1, "Duplicate reputation awarded");
            Check(Run(e, Act("qualified", "npc:04", "sect", ActionKind.ApplyToOrganization)), "Earned reputation did not unlock admission");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            e.Step(true); resumed.Step(true);
            Check(WorldCodec.Serialize(e.Snapshot()) == WorldCodec.Serialize(resumed.Snapshot()), "Reputation continuation diverged");
        }),
        ("Invalid mission reputation configuration is rejected", () =>
        {
            var w = SocialScenario.Create(); w.SocialRules = w.SocialRules! with { MissionReputationPerItem = -1 };
            bool rejected = false;
            try { _ = new SimulationEngine(w); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Negative reputation reward loaded");
        }),
        ("Autonomous successor nomination uses health trust and private local sources", () =>
        {
            var actor = new CharacterState("leader", "Leader") { OrganizationId = "sect", Position = OrganizationPosition.Leader, Health = 20, SocialWeight = 0, Caution = 1 };
            var notice = new OrganizationObservation(new("sect"), "valley", 5, 0, 0, "admission:sect:valley", 0, true);
            var contact = new RecruitmentContact("member", 0, 100, 0, "successor-contact:leader:valley", 0);
            var o = new LocalObservation(actor, 0, 0, 0, 0, 0, 0, 0, 0, new(), "market:valley", 0,
                SocialRules: new(), Organizations: new[] { notice }, SuccessorContacts: new[] { contact });
            var planner = new UtilityPlanner();
            bool Nominates(LocalObservation x) => planner.Evaluate(x).Any(c => c.Intent.Kind == ActionKind.InviteSuccessor);
            Check(Nominates(o), "Injured leader did not consider successor");
            Check(!Nominates(o with { Actor = actor with { Health = 100 } }), "Healthy leader ceded authority");
            Check(!Nominates(o with { Actor = actor with { SocialCooldownUntil = 1 } }), "Cooldown ignored");
            Check(!Nominates(o with { Organizations = new[] { notice with { IsLeader = false } } }), "Nonleader nominated");
            foreach (var bad in new[] { contact with { Source = "global" }, contact with { ObservedTick = -1 }, contact with { Trust = 0 }, contact with { Grudge = 100 } })
                Check(!Nominates(o with { SuccessorContacts = new[] { bad } }), "Invalid successor considered");
        }),
        ("NPC leader autonomously nominates and successor consents after save continuation", () =>
        {
            var w = SocialScenario.Create();
            w.OrganizationPolicies.Add(new("sect"));
            w.Organizations = w.Organizations.Select(o => o.Id == "sect" ? o with { LeaderId = "npc:00" } : o).ToList();
            w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Position = OrganizationPosition.Member } :
                c.Id == "npc:00" ? c with { Position = OrganizationPosition.Leader, Health = 20, SocialWeight = 0, WealthWeight = 0, GrowthWeight = 0, Caution = 1 } :
                c.Id == "npc:01" ? c with { SocialWeight = 1000, SectAffinity = 1 } : c).ToList();
            w.Ledger.RemoveAll(x => x.To == "npc:00" && x.ResourceId is "pill" or "spirit-stone");
            w.Relationships.Add(new("npc:00", "npc:01", Trust: 100));
            var e = new SimulationEngine(w); e.Step(true);
            var pending = e.Snapshot();
            Check(pending.SocialOffers.Any(o => o.FromId == "npc:00" && o.ToId == "npc:01" && o.Kind == SocialOfferKind.Succession), "No autonomous nomination");
            Check(pending.Organizations.Single(o => o.Id == "sect").LeaderId == "npc:00", "Nomination forced succession");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(pending)));
            for (int n = 0; n < 8; n++) { e.Step(true); resumed.Step(true); }
            Check(e.Snapshot().Organizations.Single(o => o.Id == "sect").LeaderId == "npc:01", "Autonomous succession never completed");
            Check(WorldCodec.Serialize(e.Snapshot()) == WorldCodec.Serialize(resumed.Snapshot()), "Autonomous nomination continuation diverged");
        }),
        ("Succession requires consent and transfers authority without moving assets", () =>
        {
            var e = Create();
            var before = e.Snapshot().Ledger.ToArray();
            var invite = new ActionIntent("successor", "player", "npc:00", "social", 1, 0, DecisionSource.Player, ActionKind.InviteSuccessor);
            Check(Run(e, invite), "Nomination failed");
            Check(e.Snapshot().Organizations.Single(o => o.Id == "sect").LeaderId == "player", "Nomination forced transfer");
            var accept = new ActionIntent("consent", "npc:00", "player", "social", 1, 0, DecisionSource.Npc, ActionKind.AcceptSocialOffer, SocialOfferId: "successor");
            Check(Run(e, accept), "Consent failed");
            var w = e.Snapshot();
            Check(w.Organizations.Single(o => o.Id == "sect").LeaderId == "npc:00" && w.Characters.Single(c => c.Id == "npc:00").Position == OrganizationPosition.Leader && w.Characters.Single(c => c.Id == "player").Position == OrganizationPosition.Member, "Leadership inconsistent");
            Check(before.SequenceEqual(w.Ledger), "Succession moved assets");
            Check(!Run(e, Act("old-policy", "player", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", true) }), "Former leader retained authority");
            Check(Run(e, Act("new-policy", "npc:00", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", false) }), "Successor lacked authority");
            Check(Run(e, Act("former-leaves", "player", "sect", ActionKind.LeaveOrganization)), "Former leader cannot leave");
            Check(!Run(e, Act("new-leaves", "npc:00", "sect", ActionKind.LeaveOrganization)), "Successor orphaned organization");
        }),
        ("Stale succession revalidates membership and rejects player control spoofing", () =>
        {
            var e = Create();
            var invite = new ActionIntent("successor", "player", "npc:00", "social", 1, 0, DecisionSource.Player, ActionKind.InviteSuccessor);
            Check(Run(e, invite), "Nomination failed");
            var accept = new ActionIntent("consent", "npc:00", "player", "social", 1, 0, DecisionSource.Player, ActionKind.AcceptSocialOffer, SocialOfferId: "successor");
            Check(!Run(e, accept), "Player forced NPC consent");
            Check(Run(e, Act("expel", "player", "npc:00", ActionKind.ExpelMember)), "Expulsion failed");
            Check(!Run(e, accept with { Id = "stale-consent", Source = DecisionSource.Npc }), "Former member inherited leadership");
            Check(e.Snapshot().Organizations.Single(o => o.Id == "sect").LeaderId == "player", "Failed consent changed leader");
        }),
        ("Pending succession resumes autonomous consent deterministically", () =>
        {
            var e = Create();
            Check(Run(e, new("successor", "player", "npc:00", "social", 1, 0, DecisionSource.Player, ActionKind.InviteSuccessor)), "Nomination failed");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(e.Snapshot())));
            for (int n = 0; n < 6; n++) { e.Step(true); resumed.Step(true); }
            Check(e.Snapshot().Organizations.Single(o => o.Id == "sect").LeaderId == "npc:00", "NPC never consented");
            Check(WorldCodec.Serialize(e.Snapshot()) == WorldCodec.Serialize(resumed.Snapshot()), "Succession continuation diverged");
        }),
        ("NPC recruitment uses known local contacts authority capacity and preferences", () =>
        {
            var actor = new CharacterState("leader", "Leader") { OrganizationId = "sect", Position = OrganizationPosition.Leader, SocialWeight = 2, SectAffinity = 1 };
            var notice = new OrganizationObservation(new("sect", false, 6, 10), "valley", 5, 0, 0, "admission:sect:valley", 0, true);
            var contact = new RecruitmentContact("candidate", 10, 0, 0, "recruitment-contact:leader:valley", 0);
            var o = new LocalObservation(actor, 0, 0, 0, 0, 0, 0, 0, 0, new(), "market:valley", 0,
                SocialRules: new(), Organizations: new[] { notice }, RecruitmentContacts: new[] { contact });
            var planner = new UtilityPlanner();
            bool Invites(LocalObservation value) => planner.Evaluate(value).Any(c => c.Intent.Kind == ActionKind.InviteRecruit);
            Check(Invites(o), "Leader failed to invite qualified contact");
            foreach (var invalid in new[] { contact with { Source = "global" }, contact with { ObservedTick = -1 },
                contact with { Reputation = 9 }, contact with { Grudge = 100 } })
                Check(!Invites(o with { RecruitmentContacts = new[] { invalid } }), "Invalid contact invited");
            Check(!Invites(o with { Organizations = new[] { notice with { IsLeader = false } } }), "Member recruited");
            Check(!Invites(o with { Organizations = new[] { notice with { Members = 6 } } }), "Full organization recruited");
            Check(!Invites(o with { Actor = actor with { SocialCooldownUntil = 1 } }), "Cooldown ignored");
            Check(!Invites(o with { Actor = actor with { SocialWeight = 0 } }), "Recruitment preference ignored");
        }),
        ("NPC leader recruits by consent and pending invite resumes identically", () =>
        {
            var w = SocialScenario.Create();
            w.Organizations = w.Organizations.Select(o => o.Id == "sect" ? o with { LeaderId = "npc:00" } : o).ToList();
            w.Characters = w.Characters.Select(c => c.Id == "player" ? c with { Position = OrganizationPosition.Member } :
                c.Id == "npc:00" ? c with { Position = OrganizationPosition.Leader, SocialWeight = 1000, SectAffinity = 1, GrowthWeight = 0, WealthWeight = 0 } :
                c.Id == "npc:04" ? c with { SocialWeight = 1000, SectAffinity = 1, GrowthWeight = 0, WealthWeight = 0 } : c).ToList();
            w.OrganizationPolicies.Add(new("sect", false, 6));
            w.Relationships.Add(new("npc:00", "npc:04", Trust: 10));
            var e = new SimulationEngine(w);
            e.Step(true);
            var pending = e.Snapshot();
            Check(pending.SocialOffers.Count(o => o.FromId == "npc:00" && o.ToId == "npc:04" && o.Kind == SocialOfferKind.Recruitment && o.Status == SocialOfferStatus.Pending) == 1, "Leader did not send invite");
            Check(pending.Characters.Single(c => c.Id == "npc:04").OrganizationId is null, "Invite forced admission");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(pending)));
            for (int n = 0; n < 8; n++) { e.Step(true); resumed.Step(true); }
            var final = e.Snapshot();
            Check(final.Characters.Single(c => c.Id == "npc:04").OrganizationId == "sect", "Recipient never consented");
            Check(final.SocialOffers.Count(o => o.FromId == "npc:00" && o.ToId == "npc:04") == 1, "Duplicate recruitment");
            Check(final.Characters.Count(c => c.OrganizationId == "sect") == 6, "Recruitment exceeded capacity");
            Check(WorldCodec.Serialize(final) == WorldCodec.Serialize(resumed.Snapshot()), "Recruitment continuation diverged");
        }),
        ("Recruitment contact observations exclude unknown remote and recently declined targets", () =>
        {
            var w = SocialScenario.Create();
            w.Relationships.AddRange(new[] { new RelationshipState("npc:00", "npc:04"), new("npc:00", "npc:05"), new("npc:00", "npc:06") });
            w.Characters = w.Characters.Select(c => c.Id == "npc:05" ? c with { RegionId = "remote" } : c).ToList();
            w.SocialOffers.Add(new("declined", "npc:00", "npc:04", SocialOfferKind.Recruitment, 30, "sect", SocialOfferStatus.Declined));
            var recorder = new ObservationRecorder();
            new SimulationEngine(w, recorder).Step(true);
            var contacts = recorder.Seen.Single(o => o.Actor.Id == "npc:00").RecruitmentContacts!;
            Check(contacts.Count == 1 && contacts[0].Id == "npc:06", "Unknown remote or recently declined target observed");
        }),
        ("NPC exit weighs own relationship loyalty risk and local information", () =>
        {
            var actor = new CharacterState("member", "Member") { OrganizationId = "sect", Position = OrganizationPosition.Member,
                SocialWeight = 1, SectAffinity = 1, Caution = 1 };
            var notice = new OrganizationObservation(new("sect", false, 1, 100), "valley", 5, 0, 100, "admission:sect:valley", 0);
            var observation = new LocalObservation(actor, 0, 0, 0, 0, 0, 0, 0, 0, new(), "market:valley", 0,
                SocialRules: new(), Organizations: new[] { notice });
            var planner = new UtilityPlanner();
            bool Leaves(LocalObservation o) => planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.LeaveOrganization);
            Check(Leaves(observation), "Disgruntled member had no exit candidate");
            foreach (var invalid in new[] { notice with { Source = "global" }, notice with { ObservedTick = -1 },
                notice with { RegionId = "remote" }, notice with { IsLeader = true },
                notice with { LeaderGrudge = 0, LeaderTrust = 100 }, notice with { Policy = new("other") } })
                Check(!Leaves(observation with { Organizations = new[] { invalid } }), "Invalid exit candidate");
            Check(!Leaves(observation with { Actor = actor with { SocialWeight = 10 } }), "Loyalty ignored");
            Check(!Leaves(observation with { Actor = actor with { Caution = 100 } }), "Exit risk ignored");
            Check(!Leaves(observation with { Actor = actor with { Position = OrganizationPosition.Leader } }), "Leader offered exit without succession");
            Check(!Leaves(observation with { Actor = actor with { OrganizationId = null, Position = OrganizationPosition.None } }), "Outsider offered exit");
            Check(!Leaves(observation with { Organizations = new[] { notice with { LeaderGrudge = 0 } } }), "Closed/full admission forced member out");
        }),
        ("Autonomous exit preserves assets and resumes without immediate rejoining", () =>
        {
            var e = Create();
            Check(Run(e, Act("join", "npc:04", "sect", ActionKind.ApplyToOrganization)), "Setup admission failed");
            var w = e.Snapshot();
            w.Characters = w.Characters.Select(c => c.Id == "npc:04" ? c with { SocialWeight = 0, WealthWeight = 0, GrowthWeight = 0, Caution = 0 } : c).ToList();
            w.Relationships.Add(new("npc:04", "player", Trust: -100, Grudge: 100));
            e = new SimulationEngine(w);
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(w)));
            var before = w.Ledger.Where(x => x.From == "npc:04" || x.To == "npc:04").ToArray();
            e.Step(true); resumed.Step(true);
            var after = e.Snapshot();
            var actor = after.Characters.Single(c => c.Id == "npc:04");
            Check(actor.OrganizationId is null && actor.Position == OrganizationPosition.None, "NPC did not leave");
            Check(after.Events.Any(x => x.Kind == "Governance" && x.Detail == "LeaveOrganization:npc:04:sect"), "Exit not submitted through governance");
            Check(before.SequenceEqual(after.Ledger.Where(x => x.From == "npc:04" || x.To == "npc:04")), "Exit changed personal assets");
            for (int n = 0; n < 4; n++) { e.Step(true); resumed.Step(true); }
            Check(e.Snapshot().Characters.Single(c => c.Id == "npc:04").OrganizationId is null, "NPC oscillated back into disliked organization");
            Check(WorldCodec.Serialize(e.Snapshot()) == WorldCodec.Serialize(resumed.Snapshot()), "Exit save continuation diverged");
        }),
        ("NPC admission uses fresh local qualified notices and individual utility", () =>
        {
            var actor = new CharacterState("candidate", "Candidate") { SocialWeight = 4, SectAffinity = 1, Caution = 0.3 };
            var notice = new OrganizationObservation(new("sect", true, 6), "valley", 5, 0, 0, "admission:sect:valley", 0);
            var observation = new LocalObservation(actor, 0, 0, 0, 0, 0, 0, 0, 0, new(), "market:valley", 0,
                SocialRules: new(), Organizations: new[] { notice });
            var planner = new UtilityPlanner();
            bool Applies(LocalObservation o) => planner.Evaluate(o).Any(c => c.Intent.Kind == ActionKind.ApplyToOrganization);
            Check(Applies(observation), "Qualified applicant missing");
            foreach (var invalid in new[] { notice with { Source = "global" }, notice with { ObservedTick = -1 },
                notice with { RegionId = "remote" }, notice with { Members = 6 },
                notice with { Policy = notice.Policy with { OpenApplications = false } },
                notice with { Policy = notice.Policy with { MinimumReputation = 1 } }, notice with { LeaderGrudge = 100 } })
                Check(!Applies(observation with { Organizations = new[] { invalid } }), "Invalid or unattractive notice triggered application");
            Check(!Applies(observation with { Actor = actor with { SocialWeight = 0 } }), "Different preference ignored");
            Check(!Applies(observation with { Actor = actor with { OrganizationId = "other" } }), "Member sought second affiliation");
            Check(!Applies(observation with { Actor = actor with { Hostile = true } }), "Hostile applicant offered");
        }),
        ("NPC admission competes for capacity and resumes deterministically", () =>
        {
            var w = SocialScenario.Create();
            w.OrganizationPolicies.Add(new("sect", true, 6));
            w.Characters = w.Characters.Select(c => c.OrganizationId is null && !c.Hostile
                ? c with { SocialWeight = 1000, SectAffinity = 1, WealthWeight = 0, GrowthWeight = 0 } : c).ToList();
            var e = new SimulationEngine(w);
            e.Step(true);
            var first = e.Snapshot();
            Check(first.Characters.Count(c => c.OrganizationId == "sect") == 6, "Admission exceeded or failed to fill capacity");
            Check(first.Events.Any(x => x.Kind == "Governance" && x.Detail.StartsWith("ApplyToOrganization:")), "No autonomous admission");
            Check(first.Events.Any(x => x.Kind == "Rejected" && x.Detail == "Admission requirements not met"), "Competing admissions were not revalidated");
            var resumed = new SimulationEngine(WorldCodec.Deserialize(WorldCodec.Serialize(first)));
            for (int n = 0; n < 5; n++) { e.Step(true); resumed.Step(true); }
            Check(WorldCodec.Serialize(e.Snapshot()) == WorldCodec.Serialize(resumed.Snapshot()), "Admission continuation diverged");
        }),
        ("Governance admission preserves capacity and player control", () =>
        {
            var e = Create(6);
            Check(!Run(e, Act("spoof", "npc:04", "sect", ActionKind.ApplyToOrganization) with { Source = DecisionSource.Player }), "Player controlled NPC");
            Check(Run(e, Act("join", "npc:04", "sect", ActionKind.ApplyToOrganization)), "Admission failed");
            Check(!Run(e, Act("full", "npc:05", "sect", ActionKind.ApplyToOrganization)), "Capacity exceeded");
            Check(!Run(e, Act("policy-spoof", "npc:04", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", true, 100) }), "Member governed");
            long stones = Inventory.Balance(e.Snapshot(), "npc:04", "spirit-stone");
            Check(Run(e, Act("leave", "npc:04", "sect", ActionKind.LeaveOrganization)), "Leave failed");
            Check(Inventory.Balance(e.Snapshot(), "npc:04", "spirit-stone") == stones, "Leave seized assets");
            Check(!Run(e, Act("leader-leave", "player", "sect", ActionKind.LeaveOrganization)), "Leader orphaned organization");
        }),
        ("Autonomous mission observations exclude ineligible outsiders", () =>
        {
            var e = Create();
            Run(e, Act("restrict", "player", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", true, MissionsMembersOnly: true) });
            Check(Run(e, new("publish", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("private", 1, 1, 100, 1))), "Publish failed");
            var recorder = new ObservationRecorder();
            var observed = new SimulationEngine(e.Snapshot(), recorder);
            observed.Step(true);
            Check(recorder.Seen.Any(o => o.Actor.OrganizationId == "sect" && o.Missions!.Any(m => m.Id == "private")), "Member lost task notice");
            Check(recorder.Seen.Any(o => o.Actor.OrganizationId is null), "No outsider observed");
            Check(recorder.Seen.Where(o => o.Actor.OrganizationId != "sect").All(o => o.Missions!.Count == 0), "Outsider planned inaccessible mission");
        }),
        ("Appointments grant and revoke mission authority", () =>
        {
            var e = Create();
            Run(e, Act("join", "npc:04", "sect", ActionKind.ApplyToOrganization));
            Check(Run(e, Act("promote", "player", "npc:04", ActionKind.PromoteMember)), "Promotion failed");
            var publish = new ActionIntent("task", "npc:04", "sect", "pill", 1, 0, DecisionSource.Npc, ActionKind.PublishMission, Offer: new("elder-task", 1, 1, 100, 1));
            Check(Run(e, publish), "Elder cannot publish");
            Check(Run(e, Act("demote", "player", "npc:04", ActionKind.DemoteMember)), "Demotion failed");
            Check(!Run(e, publish with { Id = "task2", Offer = new("other-task", 1, 1, 100, 1) }), "Demoted officer retained authority");
            Check(Run(e, Act("expel", "player", "npc:04", ActionKind.ExpelMember)), "Expulsion failed");
            Check(e.Snapshot().Characters.Single(c => c.Id == "npc:04").OrganizationId is null, "Expulsion retained affiliation");
        }),
        ("Member mission policy is snapshotted and checked at delivery", () =>
        {
            var e = Create();
            Check(Run(e, Act("restrict", "player", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", true, MissionsMembersOnly: true) }), "Policy failed");
            Check(Run(e, new("publish", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("private", 1, 1, 100, 1))), "Publish failed");
            var accept = new ActionIntent("accept", "npc:04", "sect", "pill", 1, 0, DecisionSource.Npc, ActionKind.AcceptMission, MissionId: "private");
            Check(!Run(e, accept), "Outsider accepted private mission");
            Run(e, Act("join", "npc:04", "sect", ActionKind.ApplyToOrganization));
            Check(Run(e, accept with { Id = "accepted" }), "Member rejected");
            Run(e, Act("public", "player", "sect", ActionKind.SetOrganizationPolicy) with { Policy = new("sect", true) });
            Run(e, Act("leave", "npc:04", "sect", ActionKind.LeaveOrganization));
            Check(e.Snapshot().Missions.Single().MembersOnly, "Policy retroactively changed mission");
            Check(!Run(e, accept with { Id = "delivery", Kind = ActionKind.DeliverMission }), "Former member delivered");
        }),
        ("Organization reward uses unreserved funds and deduplicates", () =>
        {
            var e = Create(); Run(e, Act("join", "npc:04", "sect", ActionKind.ApplyToOrganization));
            long available = Inventory.Balance(e.Snapshot(), "sect", "spirit-stone");
            Check(Run(e, new("reserve", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.PublishMission, Offer: new("reserved", 1, available, 100, available))), "Reserve failed");
            Check(!Run(e, Act("overspend", "player", "npc:04", ActionKind.RewardMember)), "Spent reserved funds");
            Check(Run(e, new("cancel", "player", "sect", "pill", 1, 0, DecisionSource.Player, ActionKind.CancelMission, MissionId: "reserved")), "Refund failed");
            long before = Inventory.Balance(e.Snapshot(), "npc:04", "spirit-stone");
            var reward = Act("reward", "player", "npc:04", ActionKind.RewardMember, 5);
            Check(Run(e, reward), "Reward failed"); e.Submit(reward); e.Step();
            Check(Inventory.Balance(e.Snapshot(), "npc:04", "spirit-stone") == before + 5 && Inventory.Balance(e.Snapshot(), "sect", "spirit-stone") == available - 5, "Reward duplicated or fabricated");
        }),
        ("Invalid governance policies are rejected on load", () =>
        {
            var w = SocialScenario.Create(); w.OrganizationPolicies.Add(new("missing"));
            bool rejected = false;
            try { _ = new SimulationEngine(w); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Orphan policy loaded");
        })
    };

    private sealed class ObservationRecorder : IActionPlanner
    {
        internal List<LocalObservation> Seen { get; } = [];
        public IReadOnlyList<ScoredAction> Evaluate(LocalObservation observation)
        {
            Seen.Add(observation);
            return [];
        }
    }
}

