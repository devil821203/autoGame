using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void HexWorldSmokeTest()
    {
        try
        {
            CreateHexWorld();
            if (engine.Snapshot().HexMap is null) throw new InvalidOperationException("Hex scenario failed to create");
            var tabs = (TabContainer)hexMap.GetParent(); tabs.CurrentTab = tabs.GetTabIdxFromControl(hexMap);
            hexMap.Select(new(4, 2)); hexMap.Travel();
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("UI hex travel rejected");
            Render();
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Hex travel save changed state");
            for (int n = 0; n < 20; n++) engine.Step();
            Render();
            if (!sectScene.Courtyard.PlayerPresent) throw new InvalidOperationException("Sect scene did not synchronize with hex arrival");
            hexMap.Select(new(4, 3)); hexMap.Build();
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("UI construction rejected");
            Render(); hexMap.Cancel();
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("UI construction cancellation rejected");
            Render();
            // Permit actual canvas draw callbacks before quitting; no gameplay state advances here.
            digest = WorldCodec.Digest(engine.Snapshot());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Drawing advanced simulation");
            GD.Print("HEX_WORLD_UI_SMOKE_PASS: map, tile selection, travel, internal scene, construction/refund, save/load, draw without Tick advancement");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("HEX_WORLD_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
}
