using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void SiteScenesSmokeTest()
    {
        try
        {
            CreateHexWorld(); var w = engine.Snapshot();
            if (w.HexMap?.Sites.Count != 6) throw new InvalidOperationException("Missing configured sites");
            hexMap.ChooseSite("organization:sect"); hexMap.EnterSelectedSite();
            if (siteScene.ActiveSiteId is not null) throw new InvalidOperationException("Entered remote scene");
            foreach (var site in w.HexMap.Sites)
            {
                var now = engine.Snapshot(); var player = now.Characters.Single(c => c.Id == now.ControlledCharacterId);
                if (HexNavigation.Position(now.HexMap!, player) != site.Anchor)
                {
                    hexMap.Select(site.Anchor); hexMap.Travel();
                    if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Scene travel rejected");
                    Render();
                    if (siteScene.ActiveSiteId is not null) throw new InvalidOperationException("Departed scene remained active");
                    for (int n = 0; n < 100 && engine.Snapshot().HexMap!.Journeys.Any(j => j.ActorId == player.Id); n++) engine.Step();
                }
                Render(); string digest = WorldCodec.Digest(engine.Snapshot());
                hexMap.ChooseSite(site.Id); hexMap.EnterSelectedSite();
                if (siteScene.ActiveSiteId != site.Id || siteScene.UnlockedSlots != site.Slots.Count(s => s.RequiredLevel <= site.MainLevel))
                    throw new InvalidOperationException("Wrong site identity or slot visibility");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Scene view changed world");
            }
            string before = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != before) throw new InvalidOperationException("Site save altered world");
            CreateSectEconomy();
            if (siteScene.ActiveSiteId is not null) throw new InvalidOperationException("Old world scene leaked into replacement");
            GD.Print("SITE_SCENES_UI_SMOKE_PASS: six scene resources, multiple entrances, arrival gate, slots, isolated switching, read-only draw, save/load, world replacement");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("SITE_SCENES_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
}
