using Cultivation.Domain;
using Cultivation.Simulation;
using Cultivation.Persistence;

internal static class SocialExpiryEventTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static SimulationEngine Engine(){var w=LifeEventScenario.Create();w.SocialRules=w.SocialRules! with{OfferLifetimeTicks=3,CooldownTicks=1};return new(w);}
    private static void Invite(SimulationEngine e){
        e.AdvanceTo(1020);e.Submit(new("seconds-invite","npc:00","player","social",1,0,DecisionSource.Npc,ActionKind.InviteCooperation));e.AdvanceTo(1620);
        Check(e.Snapshot().SocialOffers.Any(o=>o.Id=="seconds-invite"),"Invite failed");
    }
    private static ActionIntent Respond(ActionKind kind)=>new("seconds-response","player","npc:00","social",1,0,DecisionSource.Player,kind,SocialOfferId:"seconds-invite");
    public static IEnumerable<(string Name,Action Body)> Cases=>new (string,Action)[]{
        ("Social seconds off-grid invitation gets a full lifetime and expires exactly once",()=>{
            var e=Engine();Invite(e);Check(e.Snapshot().SocialOffers.Single(o=>o.Id=="seconds-invite").ExpiresAtSeconds==3420,"Rounded lifetime");
            e.AdvanceTo(3419);Check(e.Snapshot().SocialOffers.Single(o=>o.Id=="seconds-invite").Status==SocialOfferStatus.Pending,"Early expiry");
            e.AdvanceTo(3420);Check(e.Snapshot().SocialOffers.Single(o=>o.Id=="seconds-invite").Status==SocialOfferStatus.Expired,"Late expiry");
            e.AdvanceTo(6000);Check(e.Snapshot().Events.Count(x=>x.Kind=="SocialExpired"&&x.Detail=="seconds-invite")==1,"Repeated expiry");
        }),
        ("Social seconds accepted and declined invitations invalidate expiration",()=>{
            foreach(var kind in new[]{ActionKind.AcceptSocialOffer,ActionKind.DeclineSocialOffer}){
                var e=Engine();Invite(e);Check(e.ExecutePlayerActivity(Respond(kind)).Accepted,"Response failed");e.AdvanceTo(6000);var w=e.Snapshot();
                Check(w.SocialOffers.Single(o=>o.Id=="seconds-invite").Status==(kind==ActionKind.AcceptSocialOffer?SocialOfferStatus.Accepted:SocialOfferStatus.Declined)&&!w.Events.Any(x=>x.Kind=="SocialExpired"&&x.Detail=="seconds-invite"),"Closed offer expired");
            }
        }),
        ("Social seconds response completing at deadline is rejected",()=>{
            var e=Engine();Invite(e);e.AdvanceTo(2820);e.Submit(Respond(ActionKind.AcceptSocialOffer));e.AdvanceTo(3420);var w=e.Snapshot();
            Check(w.Events.Any(x=>x.ActionId=="seconds-response"&&x.Kind=="TimedCompletionRejected")&&!w.Relationships.Any(r=>r.FromId=="player"&&r.ToId=="npc:00"&&r.Cooperating),"Late response accepted");
        }),
        ("Social seconds boundary stop and latest stores preserve expiration",()=>{
            var e=Engine();e.ScheduleWorldSignal(3420,"before-social-expiry","stop",true);Invite(e);Check(e.AdvanceTo(6000).Reason==AdvanceStopReason.ImportantEvent,"No stop");var w=e.Snapshot();
            string dir=Path.Combine(Path.GetTempPath(),"social-expiry-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{foreach(IWorldStore store in new IWorldStore[]{new JsonWorldStore(),new SqliteWorldStore()}){
                string p=Path.Combine(dir,store.GetType().Name);store.Save(p,w);var a=new SimulationEngine(w);var b=new SimulationEngine(store.Load(p));a.AdvanceTo(6000);b.AdvanceTo(6000);
                Check(WorldCodec.Digest(a.Snapshot())==WorldCodec.Digest(b.Snapshot())&&b.Snapshot().Events.Count(x=>x.Kind=="SocialExpired")==1,"Expiry replay mismatch");
            }}finally{foreach(string p in Directory.GetFiles(dir))File.Delete(p);Directory.Delete(dir);}
        }),
        ("Social seconds reject missing or duplicate expiration events",()=>{
            foreach(bool duplicate in new[]{false,true}){var e=Engine();Invite(e);var w=e.Snapshot();if(duplicate)new EventScheduler(w.Time!.DiscreteEvents!).Schedule(3420,SimulationEventKind.SocialExpiry,"$social-expiry:seconds-invite",detail:"seconds-invite");
                else w.Time!.DiscreteEvents!.Pending.RemoveAll(x=>x.Kind==SimulationEventKind.SocialExpiry);
                bool rejected=false;try{WorldIntegrity.Validate(w);}catch(InvalidDataException){rejected=true;}Check(rejected,"Corrupt expiry accepted");}
        })
    };
}
