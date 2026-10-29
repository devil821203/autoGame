using Cultivation.Domain;

namespace Cultivation.Simulation;

// All state changes occur on the engine's transactional working copy.
internal static class ProductionActions
{
    internal static long Duration(WorldState w, ActionKind kind) => kind switch
    {
        ActionKind.Gather => w.Rules.GatherTicks,
        ActionKind.Brew => w.Rules.BrewTicks,
        ActionKind.Cultivate => w.Rules.CultivateTicks,
        ActionKind.Heal => w.Rules.CultivateTicks,
        _ => 0
    };

    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (!Enum.IsDefined(i.Kind) || i.Quantity != 1 || i.DurationTicks != 0) return "Invalid production action";
        if (w.Scheduled.Any(t => t.Intent.ActorId == i.ActorId)) return "Actor busy";
        var actor = w.Characters.Single(c => c.Id == i.ActorId);
        if (actor.RegionId != "valley" && i.Kind is not (ActionKind.Cultivate or ActionKind.Heal)) return "No local facilities";
        if (Duration(w, i.Kind) > long.MaxValue - w.Clock.Tick) return "Duration overflow";
        long Balance(string owner, string item) => Inventory.Balance(w, owner, item);
        if (i.Kind == ActionKind.Gather)
            return i.TargetId == "grove" && i.ResourceId == "herb" && actor.Health >= 30 && Balance("grove", "herb") > 0 && InventoryCapacity.CanReceive(w, actor.Id, "herb", 1) ? null : "No gatherable herbs, capacity or safe health";
        if (i.Kind == ActionKind.Brew)
            return i.TargetId == i.ActorId && i.ResourceId == "herb" && actor.AlchemyChance > 0 && Balance(i.ActorId, "herb") >= w.Rules.HerbsPerPill && InventoryCapacity.CanReceive(w, actor.Id, "pill", 1) ? null : "Missing skill, capacity or ingredients";
        if (i.Kind == ActionKind.Cultivate)
            return i.TargetId == i.ActorId && i.ResourceId == "pill" && Balance(i.ActorId, "pill") > 0 && actor.Cultivation <= long.MaxValue - CharacterGrowthCalculator.Growth(w, actor, CharacterProgression.GrowthPerPill(actor, w.Rules, w.Advancement)) ? null : "Cannot cultivate";
        if (i.Kind == ActionKind.Heal)
            return i.TargetId == i.ActorId && i.ResourceId == "pill" && Balance(i.ActorId, "pill") > 0 && actor.Health < 100 ? null : "Cannot heal";
        if (i.TargetId != "market" || (i.ResourceId != "herb" && i.ResourceId != w.RecoveryResourceId) || !w.Organizations.Any(o => o.Id == "market" && o.RegionId == actor.RegionId)) return "Unknown local quote";
        var quote = MarketPricing.Quote(w, i.ResourceId);
        long price = i.Kind == ActionKind.Buy ? quote.BuyPrice : quote.SellPrice;
        if (i.LimitPrice is not null && (i.Kind == ActionKind.Buy ? price > i.LimitPrice : price < i.LimitPrice)) return "Quote moved beyond limit";
        string buyer = i.Kind == ActionKind.Buy ? i.ActorId : "market";
        string seller = i.Kind == ActionKind.Buy ? "market" : i.ActorId;
        if (Balance(buyer, "spirit-stone") < price || Balance(seller, i.ResourceId) < 1) return "Insufficient market funds or stock";
        if (!InventoryCapacity.CanReceive(w, seller, "spirit-stone", price) || !InventoryCapacity.CanReceive(w, buyer, i.ResourceId, 1)) return "Trade capacity exceeded";
        return null;
    }

    internal static void Start(WorldState w, ActionIntent i)
    {
        if (i.Kind is ActionKind.Buy or ActionKind.Sell)
        {
            var quote = MarketPricing.Quote(w, i.ResourceId);
            long price = i.Kind == ActionKind.Buy ? quote.BuyPrice : quote.SellPrice;
            string buyer = i.Kind == ActionKind.Buy ? i.ActorId : "market";
            string seller = i.Kind == ActionKind.Buy ? "market" : i.ActorId;
            ActionExecutor.Transfer(w, i, seller, buyer, "Trade");
            ActionExecutor.Transfer(w, i with { ResourceId = "spirit-stone", Quantity = price }, buyer, seller, "Payment");
            ActionExecutor.Emit(w, i.Id, "Trade", $"{i.ActorId}:{i.Kind}:{i.ResourceId}@{price}");
            w.Trades.Add(new(i.Id, w.Clock.Tick, i.ActorId, "market", i.Kind, i.ResourceId, 1, price));
            var nextQuote = MarketPricing.Quote(w, i.ResourceId);
            if (quote.BuyPrice != nextQuote.BuyPrice || quote.SellPrice != nextQuote.SellPrice)
                ActionExecutor.Emit(w, i.Id, "PriceChanged", $"market:valley:{i.ResourceId}:{quote.BuyPrice}->{nextQuote.BuyPrice}");
            w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, true, i.ResourceId,
                i.Kind == ActionKind.Buy ? -price : price, i.Kind == ActionKind.Buy ? 1 : -1, "market:valley"));
            int actorIndex = w.Characters.FindIndex(c => c.Id == i.ActorId);
            var actor = w.Characters[actorIndex];
            w.Characters[actorIndex] = actor with { Reputation = CharacterDevelopment.GainExperience(actor.Reputation, 1) };
            return;
        }
        long quantity = i.Kind == ActionKind.Brew ? w.Rules.HerbsPerPill : 1;
        ActionExecutor.Transfer(w, i with { Quantity = quantity }, i.Kind == ActionKind.Gather ? "grove" : i.ActorId, ActionExecutor.Escrow(i.Id), "Reserved");
        long duration = Duration(w, i.Kind);
        if (i.Kind == ActionKind.Gather)
            duration = Math.Max(1, duration - Math.Min(duration - 1, w.Characters.Single(c => c.Id == i.ActorId).GatheringExperience / w.Rules.GatheringExperiencePerSpeedLevel));
        w.Scheduled.Add(new(w.NextSequence++, checked(w.Clock.Tick + duration), i with { Quantity = quantity }));
        ActionExecutor.Emit(w, i.Id, "Scheduled", i.Kind.ToString());
    }

    internal static void Complete(WorldState w, ActionIntent i)
    {
        if (i.Kind == ActionKind.Gather)
        {
            ActionExecutor.Transfer(w, i, ActionExecutor.Escrow(i.Id), i.ActorId, "Gathered");
            int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
            var actor = w.Characters[index];
            w.Characters[index] = actor with { GatheringExperience = CharacterDevelopment.GainExperience(actor.GatheringExperience, StaminaCalculator.Growth(w, actor, w.Rules.ExperiencePerAction)) };
            w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, true, "herb", 0, 1, "grove:valley"));
        }
        else
        {
            ActionExecutor.Transfer(w, i, ActionExecutor.Escrow(i.Id), "consumed", "Consumed");
            int index = w.Characters.FindIndex(c => c.Id == i.ActorId);
            var actor = w.Characters[index];
            if (i.Kind == ActionKind.Brew)
            {
                var random = new SeededRandom(w.RandomState);
                bool success = random.NextUInt32() / ((double)uint.MaxValue + 1) < CharacterDevelopment.SuccessChance(actor, w.Rules);
                w.RandomState = random.State;
                w.Characters[index] = actor with { AlchemyExperience = CharacterDevelopment.GainExperience(actor.AlchemyExperience, StaminaCalculator.Growth(w, actor, w.Rules.ExperiencePerAction)) };
                if (success) ActionExecutor.Transfer(w, i with { ResourceId = "pill", Quantity = 1 }, "$alchemy", i.ActorId, "Brewed");
                ActionExecutor.Emit(w, i.Id, success ? "BrewSucceeded" : "BrewFailed", actor.Id);
                w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, success, "herb", 0, -i.Quantity, "self:production"));
            }
            else
            {
                w.Characters[index] = i.Kind == ActionKind.Heal
                    ? actor with { Health = Math.Min(100, actor.Health + w.Rules.HealingPerPill) }
                    : actor with
                    {
                        Cultivation = checked(actor.Cultivation + StaminaCalculator.Growth(w, actor, CharacterProgression.GrowthPerPill(actor, w.Rules, w.Advancement))),
                        MethodExperience = actor.MethodId is null ? actor.MethodExperience : CharacterDevelopment.GainExperience(actor.MethodExperience, StaminaCalculator.Growth(w, actor, w.Rules.ExperiencePerAction))
                    };
                w.Memories.Add(new(i.ActorId, w.Clock.Tick, i.Kind, true, "pill", 0, -1, "self:consumption"));
            }
        }
        ActionExecutor.Emit(w, i.Id, "Completed", $"{i.ActorId}:{i.Kind}");
    }
}


