using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class Main
{
    private async void SettlementManagementSmokeTest()
    {
        try
        {
            CreateSettlementWorld();OpenSettlementManagement();var p=settlementWindow.Panel;
            var w=engine.Snapshot();var s=w.Settlements.Single();
            if(!settlementWindow.Visible||!p.QueryText.Contains("下一級")||p.CanExecute(ActionKind.UpgradeSettlement)||p.CanExecute(ActionKind.SettlementBuy))
                throw new Exception("Management window, queries or eligibility preview failed");
            SeedSettlementSmokeStock();
            p.ChooseItem("spirit-stone");p.Command(ActionKind.SupportSettlement);if(!p.FeedbackText.Contains("排隊"))throw new Exception("Missing queued feedback");
            StepTicks(1);StepTicks(w.Time!.Rules.TicksPerDay);
            if(!p.QueryText.Contains("居民工資")||!p.QueryText.Contains("設施產出"))throw new Exception("Missing workforce/account queries");
            p.ChooseItem("food");p.SetQuantity(1);p.Refresh(engine.Snapshot(),engine);
            if(!p.CanExecute(ActionKind.SettlementBuy))throw new Exception("Valid trade disabled");
            p.Command(ActionKind.SettlementBuy);StepTicks(1);
            if(!p.FeedbackText.Contains("已完成"))throw new Exception("Missing completion feedback");
            p.SetQuantity(int.MaxValue);p.Command(ActionKind.SettlementBuy);Render();
            if(!p.FeedbackText.Contains("無法執行"))throw new Exception("Refresh replaced latest failure with old success");
            p.SetQuantity(1);
            p.SelectQueryTab("收支與紀錄");p.FilterRecords("商鋪成交款");
            if(!p.QueryText.Contains("商鋪成交款"))throw new Exception("Ledger search failed");
            string digest=WorldCodec.Digest(engine.Snapshot());
            foreach(string name in new[]{"總覽","建築與僱員","庫存與交易","建造與援助","收支與紀錄"})p.SelectQueryTab(name);
            p.FilterRecords("");p.SelectSettlement(s.Id);
            if(WorldCodec.Digest(engine.Snapshot())!=digest)throw new Exception("Read-only queries changed simulation");
            w=engine.Snapshot();var otherSite=w.HexMap!.Sites.Single(t=>t.Id=="settlement:qingyun");
            w.Organizations.Add(new("query-other","另一聚落資產"));w.Organizations.Add(new("query-residents","另一居民帳"));
            w.Settlements.Add(new(){Id="query-other",SiteId=otherSite.Id,OrganizationId="query-other",ResidentsAccountId="query-residents",
                Level=w.SettlementRules!.InitialLevel,Happiness=w.SettlementRules.InitialHappiness,Population=5,WorkingPopulation=5,
                PopulationCapacity=w.SettlementRules.HallCapacity,Territory=[otherSite.Anchor],
                Buildings=[new("query-hall",SettlementBuildingKind.TownHall,otherSite.Anchor,0,0){OwnerOrganizationId="query-other",Level=w.SettlementOperationRules!.InitialBuildingLevel}]});
            engine=new(w);pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);Render();p.SelectSettlement("query-other");
            digest=WorldCodec.Digest(engine.Snapshot());p.Refresh(engine.Snapshot(),engine);
            if(p.SelectedSettlementId!="query-other"||!p.QueryText.Contains("遠端查詢")||p.CanExecute(ActionKind.SetSettlementEmployees)||p.CanExecute(ActionKind.UpgradeSettlement))
                throw new Exception("Settlement switching or remote restrictions failed");
            p.Command(ActionKind.UpgradeSettlement);
            if(!p.FeedbackText.Contains("無法執行")||WorldCodec.Digest(engine.Snapshot())!=digest||engine.Snapshot().ControlledCharacterId!=w.ControlledCharacterId)
                throw new Exception("Read-only remote query bypassed permissions");
            Save();Load();Render();
            if(p.SelectedSettlementId!=s.Id||WorldCodec.Digest(engine.Snapshot())!=digest)throw new Exception("Reload failed to reset view or preserve world");
            settlementWindow.Hide();
            GD.Print("SETTLEMENT_MANAGEMENT_UI_SMOKE_PASS: window, public/private ownership query, trade, feedback, ledger filter, multiple settlements, read-only/remote restrictions, save/load reset");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
