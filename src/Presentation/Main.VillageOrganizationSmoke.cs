using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void VillageOrganizationSmokeTest()
    {
        try
        {
            CreateVillageOrganizationWorld();OpenSettlementManagement();
            var w=engine.Snapshot();var p=w.SettlementOrganizationDemo!;
            if(!settlementWindow.Panel.QueryText.Contains(p.Organization.Name)||!settlementWindow.Panel.QueryText.Contains("成員"))
                throw new Exception("Village organization query not shown");
            string sceneId="organization:"+p.Organization.Id;
            hexMap.ChooseSite(sceneId);hexMap.EnterSelectedSite();
            if(siteScene.ActiveSiteId!=sceneId)throw new Exception("Village organization scene missing");
            var site=w.OrganizationSites.Single();
            var command=new ActionIntent("forbidden-player-hq",w.ControlledCharacterId,p.SettlementId,site.Id,1,0,DecisionSource.Player,
                ActionKind.UpgradeOrganizationSite,OrganizationId:p.Organization.Id);
            if(ActionValidator.Validate(w,command) is null)throw new Exception("Player gained authority over NPC organization");
            StepTicks(w.Time!.Rules.TicksPerDay);w=engine.Snapshot();
            if(w.SettlementConsumption.Count!=2||w.SettlementPopulationGrowth.Count!=1)throw new Exception("Village stopped settling");
            string digest=WorldCodec.Digest(w);Save();Load();Render();
            if(engine.Snapshot().SettlementOrganizationDemo is null||WorldCodec.Digest(engine.Snapshot())!=digest)
                throw new Exception("Village organization save/load changed state");
            settlementWindow.Hide();
            GD.Print("VILLAGE_ORGANIZATION_UI_SMOKE_PASS: NPC organization, query assets/roster/quota, independent scene, player authority rejected, village growth, save/load");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
