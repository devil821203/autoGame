using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
	private void CharacterFollowSmokeTest()
	{
		try
		{
			CreateTimeWorld(); var w = engine.Snapshot(); w.PlayerAutoSchedule = false;
			w.Characters = w.Characters.Select(c => c.Id == w.ControlledCharacterId ? c with { Health = 70 } : c).ToList();
			engine = new(w); Render(); characterJournal.PopupCentered();
			SubmitTracked(new("journal-buy", "player", "market", "herb", 1, 0, DecisionSource.Player, ActionKind.Buy));
			if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Buy rejected"); Render();
			if (!characterJournal.DisplayText.Contains("物品｜") || !characterJournal.DisplayText.Contains("Trade") || !characterJournal.DisplayText.Contains("行動｜")) throw new InvalidOperationException("Action/inventory journal missing");
			SubmitTracked(new("journal-rest", "player", "player", "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest));
			if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Rest rejected"); Render();
			if (!characterJournal.DisplayText.Contains("生命 70 → 80") || !characterJournal.DisplayText.Contains("AP 29 → 28")) throw new InvalidOperationException("State journal missing");
			string before = characterJournal.DisplayText; Render(); Render();
			if (characterJournal.DisplayText != before) throw new InvalidOperationException("Rendering duplicated records");
			characterJournal.Hide(); engine.Step(); Render();
			if (!characterJournal.DisplayText.Contains("生命 80 → 90")) throw new InvalidOperationException("Hidden window stopped recording");
			actors.Select(2); Render();
			if (!characterJournal.Title.EndsWith(engine.Snapshot().Characters.Single(c => c.Id == "player").Name)) throw new InvalidOperationException("Observation selector changed journal actor");
			engine.Step(); Render();
			SubmitTracked(new("journal-travel", "player", "grove", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
			if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Travel rejected"); Render();
			var canvas = hexMap.Canvas; canvas.Size = new(850,430); hexMap.Refresh(engine.Snapshot());
			if (canvas.PlayerScreenPosition.DistanceTo(canvas.Size/2) > .1f) throw new InvalidOperationException("Camera does not center player");
			var destination = engine.Snapshot().HexMap!.Tiles.Single(t => t.PlaceId == "dungeon").Coord;
			hexMap.Select(destination);
			if (hexMap.Selection != destination || canvas.PlayerScreenPosition.DistanceTo(canvas.Size/2) > .1f) throw new InvalidOperationException("Selection changed follow target");
			for (int n=0; n<15 && engine.Snapshot().Scheduled.Count>0; n++) { engine.Step(); Render(); }
			if (engine.Snapshot().Characters.Single(c=>c.Id=="player").DemoLocationId!="grove" || canvas.PlayerScreenPosition.DistanceTo(canvas.Size/2)>.1f) throw new InvalidOperationException("Camera did not follow travel");
			string digest=WorldCodec.Digest(engine.Snapshot()); Render();
			if (WorldCodec.Digest(engine.Snapshot())!=digest) throw new InvalidOperationException("Journal or camera changed simulation");
			CreateTimeWorld();
			if (characterJournal.DisplayText.Contains("journal-buy") || characterJournal.DisplayText.Contains("生命 70 → 80")) throw new InvalidOperationException("New world retained old journal");
			GD.Print("CHARACTER_FOLLOW_UI_SMOKE_PASS: follow travel, selection independence, action/item/state journal, no duplicates, hidden recording, actor authority, reset, read-only");
			GetTree().Quit();
		}
		catch (Exception e) { GD.PrintErr("CHARACTER_FOLLOW_UI_SMOKE_FAIL: " + e); GetTree().Quit(1); }
	}
}
