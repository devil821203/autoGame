using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void OrganizationLaborSmokeTest()
    {
        try
        {
            CreateVillageOrganizationWorld();var w=engine.Snapshot();w.LaborAutonomy=null;w.OrganizationDevelopment=null;
            w.PopulationInvestmentRules=w.PopulationInvestmentRules! with{SuccessProbability=1}; // Finite deterministic test fixture only.
            foreach(var(item,amount)in new[]{("food",100L),("wood",100L)})
                w.Ledger.Add(new(w.NextSequence++,0,"labor-ui-fixture:"+item,"$genesis","sect",item,amount,"Genesis"));
            engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);Render();
            settlements.ChooseOrganizationSite(OrganizationSiteKind.Headquarters);settlements.Command(ActionKind.CreateOrganizationSite);StepTicks(2);
            var anchor=w.HexMap!.Sites.Single(s=>s.Id==w.Settlements.Single().SiteId).Anchor;
            foreach(var kind in new[]{SettlementBuildingKind.Dormitory,SettlementBuildingKind.SeniorDormitory,SettlementBuildingKind.Farm})
            {settlements.ChooseBuild(kind,anchor);settlements.Command(ActionKind.BuildSettlement);StepTicks(2);}
            organizationLabor.ChooseOrganization("sect");organizationLabor.Command(ActionKind.RecruitOrganizationLabor);StepTicks(2);
            w=engine.Snapshot();string farm=w.Settlements.Single().Buildings.Single(b=>b.OwnerOrganizationId=="sect"&&b.Kind==SettlementBuildingKind.Farm).Id;
            organizationLabor.ChooseBuilding(farm,1);organizationLabor.Command(ActionKind.SetOrganizationLabor);StepTicks(2);
            organizationLabor.Command(ActionKind.RecruitOrganizationMember);StepTicks(2);
            int before=engine.Snapshot().Settlements.Single().Population;
            organizationLabor.Command(ActionKind.InvestSettlementPopulation);StepTicks(2);w=engine.Snapshot();
            if(OrganizationCapacity.LaborCount(w,"sect")!=1||OrganizationCapacity.Assigned(w,"sect")!=1||w.MemberRecruitments.Count!=1||
                w.Settlements.Single().Population!=before+1||!organizationLabor.DetailsText.Contains("成員14/15"))throw new Exception("Labor/member/investment UI command failed");
            string digest=WorldCodec.Digest(w);Save();Load();Render();
            if(WorldCodec.Digest(engine.Snapshot())!=digest)throw new Exception("Labor UI snapshot changed after save");
            settlementWindow.Hide();GD.Print("ORGANIZATION_LABOR_UI_SMOKE_PASS: dormitories, recruitment, assignment, member capacity, investment, save/load");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
