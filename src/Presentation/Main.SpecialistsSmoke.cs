using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void SpecialistsSmokeTest()
    {
        try
        {
            CreateMortalCultivationWorld(); var w = engine.Snapshot();
            foreach (var role in Enum.GetValues<TestProfession>().Where(r => r != TestProfession.None))
                if (w.Characters.Count(a => a.TestProfession == role) != 1) throw new Exception("Missing specialist " + role);
            var doctor = w.Characters.Single(a => a.TestProfession == TestProfession.Physician);
            w.PlayerAutoSchedule = false;
            w.Characters = w.Characters.Select(a => a.Id == w.ControlledCharacterId ? a with { Health = 50, RegionId = doctor.RegionId, DemoLocationId = doctor.DemoLocationId, HexPosition = doctor.HexPosition } : a).ToList();
            engine = new(w); Render();
            paidServices.Select(doctor.Id, "medical", ServiceMaterialSource.Provider);
            if (!paidServices.DisplayText.Contains("總價 9")) throw new Exception("Provider quote missing");
            paidServices.Request(); if (!engine.Step().Single().Accepted) throw new Exception("UI commission rejected"); Render();
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new Exception("Commission save changed state");
            engine.Step(); Render();
            if (!engine.Snapshot().PaidServiceReceipts.Single().Success) throw new Exception("Commission did not settle");
            characterJournal.SelectActor(doctor.Id); ObserveCharacter();
            if (characterJournal.SelectedActorId != doctor.Id) throw new Exception("Provider journal selection unavailable");
            characterAttributes.Refresh(engine.Snapshot(), engine); characterAttributes.SelectActor(doctor.Id);
            if (!characterAttributes.DisplayText.Contains("醫師01")) throw new Exception("Specialist attributes missing");
            characterAttributes.Hide(); characterJournal.Hide();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("SPECIALISTS_PAID_SERVICE_UI_SMOKE_PASS"); GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr("SPECIALISTS_PAID_SERVICE_UI_SMOKE_FAIL: " + e); GetTree().Quit(1); }
    }
}
