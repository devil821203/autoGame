using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private void SectSceneSmokeTest()
    {
        try
        {
            CreateSectEconomy();
            string digest = WorldCodec.Digest(engine.Snapshot());
            Render(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Rendering changed state");
            if (sectScene.Courtyard.PlayerPresent || sectScene.Courtyard.VisibleActorCount != 0) throw new InvalidOperationException("Remote actors exposed");
            int queued = engine.Snapshot().Pending.Count;
            sectScene.Courtyard.Activate("town");
            if (engine.Snapshot().Pending.Count != queued) throw new InvalidOperationException("Remote hotspot submitted action");
            sectScene.Travel("sect");
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Sect travel rejected");
            Render();
            if (sectScene.Courtyard.PlayerPresent) throw new InvalidOperationException("Arrived before scheduled completion");
            for (int n = 0; n < 3; n++) engine.Step();
            Render();
            if (!sectScene.Courtyard.PlayerPresent || sectScene.Courtyard.VisibleActorCount < 1) throw new InvalidOperationException("Arrived player missing");
            var tabs = (TabContainer)sectScene.GetParent();
            sectScene.Courtyard.Activate("missions");
            if (tabs.CurrentTab != tabs.GetTabIdxFromControl(missionPanel)) throw new InvalidOperationException("Mission hotspot failed");
            sectScene.Courtyard.Activate("exchange");
            if (tabs.CurrentTab != tabs.GetTabIdxFromControl(sectFacilities)) throw new InvalidOperationException("Exchange hotspot failed");
            digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest || !sectScene.Courtyard.PlayerPresent) throw new InvalidOperationException("Scene reload changed state");
            sectScene.Courtyard.Activate("grove");
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Exit travel rejected");
            Render();
            if (sectScene.Courtyard.PlayerPresent || sectScene.Courtyard.VisibleActorCount != 0) throw new InvalidOperationException("Departed actors still visible");
            GD.Print("SECT_SCENE_UI_SMOKE_PASS: view isolation, arrival, hotspots, exit, save/load, read-only rendering");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("SECT_SCENE_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
}
