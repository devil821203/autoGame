using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class SocialExpiryEvents
{
    private static string Key(string id)=>"$social-expiry:"+id;
    internal static void Sync(WorldState w,EventScheduler queue)
    {
        if(w.Time?.IndependentSocialExpiryEvents!=true)return;
        var events=w.Time.DiscreteEvents!;
        foreach(var e in events.Pending.Where(e=>e.Kind==SimulationEventKind.SocialExpiry&&e.Version==events.Versions[e.CorrelationId]&&
            !w.SocialOffers.Any(o=>o.Id==e.Detail&&o.Status==SocialOfferStatus.Pending)).ToArray())queue.Cancel(e.CorrelationId);
        foreach(var offer in w.SocialOffers.Where(o=>o.Status==SocialOfferStatus.Pending)){
            string key=Key(offer.Id);
            if(!events.Pending.Any(e=>e.Kind==SimulationEventKind.SocialExpiry&&e.CorrelationId==key&&e.Version==events.Versions[e.CorrelationId]))
                queue.Schedule(offer.ExpiresAtSeconds??throw new InvalidDataException("Missing social expiry seconds"),SimulationEventKind.SocialExpiry,key,detail:offer.Id);
        }
    }
    internal static void Validate(WorldState w)
    {
        var events=w.Time?.DiscreteEvents;
        var live=events?.Pending.Where(e=>e.Kind==SimulationEventKind.SocialExpiry&&e.Version==events.Versions[e.CorrelationId]).ToArray()??[];
        if(w.Time?.IndependentSocialExpiryEvents!=true){if(live.Length!=0||w.SocialOffers.Any(o=>o.ExpiresAtSeconds is not null))throw new InvalidDataException("Social expiry requires seconds mode");return;}
        if(events is null)throw new InvalidDataException("Missing social expiry clock");
        long step=DiscreteTime.SecondsPerStep(w);
        if(w.SocialOffers.Any(o=>o.ExpiresAtSeconds is not {} due||due<=0||o.ExpiresTick!=due/step+(due%step==0?0:1)))throw new InvalidDataException("Invalid social seconds projection");
        var active=w.SocialOffers.Where(o=>o.Status==SocialOfferStatus.Pending).ToArray();
        if(live.Length!=active.Length||active.Any(o=>o.ExpiresAtSeconds<events.WorldTime||live.Count(e=>e.CorrelationId==Key(o.Id)&&e.Detail==o.Id&&e.DueAt==o.ExpiresAtSeconds)!=1))
            throw new InvalidDataException("Social expiry deadline/event mismatch");
    }
}
