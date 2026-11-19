using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main
{
    private SettlementPanel settlements = null!;
    private SettlementManagementWindow settlementWindow=null!;
    private void OpenSettlementManagement()
    {
        settlementWindow.Refresh(engine.Snapshot(),engine);settlementWindow.PopupCentered();
    }
    private void CreateSettlementWorld()
        => CreateSettlementWorld(false);
    private void CreateVillageOrganizationWorld()
        => CreateSettlementWorld(true);
    private void CreateSettlementWorld(bool residentOrganization)
    {
        try
        {
            engine = new(residentOrganization?SettlementOrganizationScenario.Create(playerName.Text):SettlementScenario.Create(playerName.Text));
            pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true); paused = true;
            developer.ResetHistory(); actors.Clear(); foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            var tabs = (TabContainer)settlements.GetParent(); tabs.CurrentTab = tabs.GetTabIdxFromControl(settlements);
            notice.Text = "已建立聚落營運測試：公有建築滿員自動產出；組織先建分舵／總舵，再建自己的設施並僱員。";
            if(residentOrganization)notice.Text=$"村內組織測試：{engine.Snapshot().SettlementOrganizationDemo!.Organization.Name}由NPC首領管理，初始總舵已扣建造費；可查詢其資金、配額與成員。";
            Render();
        }
        catch (Exception e) { notice.Text = "聚落情境建立失敗：" + e.Message; }
    }
    private async void SettlementSmokeTest()
    {
        try
        {
            CreateSettlementWorld();
            if (!settlements.DetailsText.Contains("人口 20/30")) throw new Exception("Settlement UI/scenario missing");
            SeedSettlementSmokeStock();
            var w = engine.Snapshot(); var s = w.Settlements.Single();
            for(int n=0;n<w.Time!.Rules.TicksPerDay;n++) engine.Step(); Render();
            w = engine.Snapshot();
            if(w.SettlementDays.Count!=3 || w.SettlementDays.Single(d=>d.BuildingId=="farm").GrossQuantity!=w.SettlementRules!.DailyOutput(SettlementBuildingKind.Farm)*w.SettlementRules.EmployeeLimit||
                w.SettlementConsumption.Count!=2||
                w.SettlementDays.Any(d=>d.Wages!=0||!d.Automatic))
                throw new Exception("Settlement production missing");
            settlements.ChooseItem("food"); settlements.Command(ActionKind.SettlementBuy); engine.Step(); Render();
            settlements.Command(ActionKind.SettlementSell); engine.Step(); Render();
            if(Inventory.Balance(engine.Snapshot(),w.ControlledCharacterId,"food")!=0) throw new Exception("Shop UI trade failed");
            settlements.ChooseItem("wood"); settlements.Command(ActionKind.SettlementBuy); engine.Step(); Render();
            w=engine.Snapshot(); engine.Submit(new("settlement-smoke-transfer",w.ControlledCharacterId,"sect","wood",1,0,DecisionSource.Player));
            engine.Step(); Render(); settlements.Command(ActionKind.SupportSettlement); engine.Step(); Render();
            w=engine.Snapshot();
            if(w.Settlements.Single().Influence.GetValueOrDefault("sect")!=w.SettlementRules!.MaterialInfluence)
                throw new Exception("Support UI/influence failed");
            var cost=w.SettlementConstructionRules!.Single(r=>r.Kind==SettlementBuildingKind.Housing);
            long requiredWood=cost.Wood+w.OrganizationSiteRules!.ConstructionWood;
            while(SettlementDailyEconomy.Available(engine.Snapshot(),engine.Snapshot().Settlements.Single(),s.OrganizationId,"wood")<requiredWood)engine.Step();
            Render(); settlements.SetQuantity(requiredWood);settlements.ChooseItem("wood");
            settlements.Command(ActionKind.SettlementBuy);engine.Step();Render();settlements.GiveToSponsor();engine.Step();Render();
            settlements.ChooseOrganizationSite(OrganizationSiteKind.Branch);settlements.Command(ActionKind.CreateOrganizationSite);engine.Step();Render();
            w=engine.Snapshot();int capacity=w.Settlements.Single().PopulationCapacity;
            var anchor=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Anchor;
            settlements.ChooseBuild(SettlementBuildingKind.Housing,anchor);settlements.Command(ActionKind.BuildSettlement);engine.Step();Render();
            w=engine.Snapshot();
            if(w.OrganizationSites.Count!=1||w.SettlementConstruction.Count!=1||w.Settlements.Single().Buildings.Last().OwnerOrganizationId!="sect"||w.Settlements.Single().PopulationCapacity!=capacity+w.SettlementRules!.HousingCapacity||
                w.Settlements.Single().Influence["sect"]!=w.SettlementRules.MaterialInfluence+w.SettlementRules.BuildingInfluence)
                throw new Exception("Construction UI/capacity/influence failed");
            while(SettlementDailyEconomy.Available(engine.Snapshot(),engine.Snapshot().Settlements.Single(),s.OrganizationId,"wood")<w.SettlementOperationRules!.UpgradeWood)engine.Step();
            Render();settlements.ChooseBuilding("forestry");settlements.Command(ActionKind.UpgradeSettlementBuilding);engine.Step();Render();
            w=engine.Snapshot();var forestry=w.Settlements.Single().Buildings.Single(b=>b.Id=="forestry");
            if(forestry.Level!=w.SettlementOperationRules!.InitialBuildingLevel+1||forestry.EmployeeLimit!=w.SettlementRules!.EmployeeLimit+w.SettlementOperationRules.EmployeesPerLevel||
                w.SettlementBuildingUpgrades.Single().OwnerId!=s.OrganizationId)throw new Exception("Paid building upgrade UI failed");
            string digest=WorldCodec.Digest(w); Save(); Load(); Render();
            if(WorldCodec.Digest(engine.Snapshot())!=digest) throw new Exception("Settlement save/load changed");
            GD.Print("SETTLEMENT_UI_SMOKE_PASS: automatic public buildings, public shop, organization branch, owned construction, quota, paid building upgrade, capacity, influence, save/load");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }

    // Finite test fixture only: stock for exercising trade/build buttons despite daily consumption.
    // Never called by normal scenario creation or --settlement-demo.
    private void SeedSettlementSmokeStock()
    {
        var w=engine.Snapshot();var s=w.Settlements.Single();var r=w.SettlementRules!;
        foreach(var(item,amount)in new[]{("food",(long)s.Population*2),("wood",r.Levels.Sum(l=>l.Wood)+s.Population)})
            w.Ledger.Add(new(w.NextSequence++,0,"settlement-ui-fixture:"+item,"$genesis",s.OrganizationId,item,amount,"Genesis"));
        engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);Render();
    }
}
