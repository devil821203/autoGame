using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class SettlementPanel
{
    private readonly OptionButton settlementChoices=new();
    private readonly List<string> settlementIds=[];
    private readonly VBoxContainer sharedControls=new();
    private readonly TabContainer managementTabs=new(){CustomMinimumSize=new(0,340),SizeFlagsVertical=SizeFlags.ExpandFill};
    private readonly Label feedback=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart,Text="尚無操作紀錄。"};
    private readonly Label overview=QueryLabel(), workforce=QueryLabel(), stocks=QueryLabel(), accounts=QueryLabel(), history=QueryLabel();
    private readonly LineEdit querySearch=new(){PlaceholderText="篩選帳本與紀錄，例如：木材、工資、建造"};
    private readonly Dictionary<ActionKind,Button> actionButtons=[];
    private readonly Dictionary<string,(string Settlement,string Name)> pendingOrders=[];
    private string? selectedSettlement;
    private object? session;
    private bool rendering;
    private string? feedbackOrderId;
    public string QueryText=>string.Join("\n",overview.Text,workforce.Text,stocks.Text,accounts.Text,history.Text,organizationInfo.Text);
    public string FeedbackText=>feedback.Text;
    public string? SelectedSettlementId=>selectedSettlement;
    public bool CanExecute(ActionKind kind)=>actionButtons.TryGetValue(kind,out var b)&&!b.Disabled;
    private static Label QueryLabel()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private VBoxContainer QueryTab(string name)
    {
        var scroll=new ScrollContainer(){Name=name};managementTabs.AddChild(scroll);
        var column=new VBoxContainer(){SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(column);return column;
    }
    private void AddAction(HBoxContainer row,string name,ActionKind kind)
    {
        var b=new Button(){Text=name};row.AddChild(b);actionButtons[kind]=b;b.Pressed+=()=>Command(kind);
    }
    private void BuildManagementUi()
    {
        AddChild(summary);AddChild(sharedControls);
        var locations=new HBoxContainer();sharedControls.AddChild(locations);
        locations.AddChild(new Label(){Text="查詢聚落"});locations.AddChild(settlementChoices);
        var sponsors=new HBoxContainer();sharedControls.AddChild(sponsors);
        sponsors.AddChild(new Label(){Text="出資組織"});sponsors.AddChild(donors);
        var resources=new HBoxContainer();sharedControls.AddChild(resources);
        resources.AddChild(new Label(){Text="交易／援助物資"});resources.AddChild(items);resources.AddChild(new Label(){Text="數量"});resources.AddChild(quantity);
        AddChild(feedback);AddChild(managementTabs);
        var general=QueryTab("總覽");general.AddChild(overview);
        var upgrade=new HBoxContainer();general.AddChild(upgrade);AddAction(upgrade,"升級聚落",ActionKind.UpgradeSettlement);
        var staff=QueryTab("建築與僱員");var staffRow=new HBoxContainer();staff.AddChild(staffRow);
        staffRow.AddChild(buildings);staffRow.AddChild(new Label(){Text="調整為"});staffRow.AddChild(employees);
        AddAction(staffRow,"套用僱員數",ActionKind.SetSettlementEmployees);staff.AddChild(workforce);
        AddAction(staffRow,"升級生產建築",ActionKind.UpgradeSettlementBuilding);
        var trade=QueryTab("庫存與交易");trade.AddChild(stocks);var buySell=new HBoxContainer();trade.AddChild(buySell);
        buySell.AddChild(shops);
        AddAction(buySell,"向商鋪購買",ActionKind.SettlementBuy);AddAction(buySell,"賣給商鋪",ActionKind.SettlementSell);
        var construct=QueryTab("建造與援助");OrganizationSiteUi(construct);var buildRow=new HBoxContainer();construct.AddChild(buildRow);
        buildRow.AddChild(buildTypes);buildRow.AddChild(buildTiles);AddAction(buildRow,"組織出資建造",ActionKind.BuildSettlement);
        construct.AddChild(buildCost);var support=new HBoxContainer();construct.AddChild(support);
        AddAction(support,"組織捐贈物資",ActionKind.SupportSettlement);AddAction(support,"角色物資交給組織",ActionKind.Transfer);
        construct.AddChild(new Label(){Text="可先用角色資金購買木材，再交給出資組織；公有設施自動營運，組織設施須僱員並繳生產稅。",AutowrapMode=TextServer.AutowrapMode.WordSmart});
        var ledger=QueryTab("收支與紀錄");ledger.AddChild(accounts);ledger.AddChild(querySearch);ledger.AddChild(history);
        settlementChoices.ItemSelected+=n=>SelectSettlement(settlementIds[(int)n]);
        buildings.ItemSelected+=n=>{
            if(ReadWorld?.Invoke() is { } w && Selection(w)?.Buildings.SingleOrDefault(b=>b.Id==buildingIds[(int)n]) is { } b)employees.Value=b.EmployeeCount;
            UpdateFromControls();};
        foreach(var option in new[]{items,donors,buildTypes,buildTiles})option.ItemSelected+=_=>UpdateFromControls();
        employees.ValueChanged+=_=>UpdateFromControls();quantity.ValueChanged+=_=>UpdateFromControls();querySearch.TextChanged+=_=>UpdateFromControls();
    }
    private SettlementState? Selection(WorldState w)=>w.Settlements.SingleOrDefault(s=>s.Id==selectedSettlement)??w.Settlements.FirstOrDefault();
    public void SelectSettlement(string id)
    {
        if(!settlementIds.Contains(id))return;selectedSettlement=id;settlementChoices.Select(settlementIds.IndexOf(id));
        buildingIds.Clear();buildPositions.Clear();UpdateFromControls();
    }
    public void SelectQueryTab(string name)
    {for(int n=0;n<managementTabs.GetTabCount();n++)if(managementTabs.GetTabTitle(n)==name)managementTabs.CurrentTab=n;}
    public void FilterRecords(string search){querySearch.Text=search;UpdateFromControls();}
    private void ResetSession(object? identity)
    {
        if(identity is null||ReferenceEquals(identity,session))return;
        session=identity;selectedSettlement=null;settlementIds.Clear();buildingIds.Clear();buildPositions.Clear();
        itemIds.Clear();donorIds.Clear();buildKinds.Clear();siteKindIds.Clear();shopIds.Clear();pendingOrders.Clear();feedbackOrderId=null;feedback.Text="尚無操作紀錄。";
        employees.Value=0;quantity.Value=1;querySearch.Text="";
    }
    private void RefreshSettlements(WorldState w)
    {
        var ids=w.Settlements.Select(s=>s.Id).ToList();
        if(!settlementIds.SequenceEqual(ids)){
            settlementIds.Clear();settlementIds.AddRange(ids);settlementChoices.Clear();
            foreach(var s in w.Settlements)settlementChoices.AddItem(w.HexMap!.Sites.Single(t=>t.Id==s.SiteId).Name);
        }
        if(!settlementIds.Contains(selectedSettlement??""))selectedSettlement=settlementIds.FirstOrDefault();
        if(selectedSettlement is not null)settlementChoices.Select(settlementIds.IndexOf(selectedSettlement));
    }
    private void UpdateFromControls(){if(!rendering&&ReadWorld?.Invoke() is { } w)Refresh(w);}
    public void Refresh(WorldState w,object? identity=null)
    {
        if(rendering)return;rendering=true;
        try{RefreshCore(w,identity);}finally{rendering=false;}
    }
    private static string ActionName(ActionKind kind)=>kind switch{
        ActionKind.SetSettlementEmployees=>"調整僱員",ActionKind.SettlementBuy=>"購買商品",ActionKind.SettlementSell=>"出售商品",
        ActionKind.SupportSettlement=>"組織援助",ActionKind.Transfer=>"交付出資組織",ActionKind.UpgradeSettlement=>"升級聚落",
        ActionKind.CreateOrganizationSite=>"建立組織據點",ActionKind.UpgradeOrganizationSite=>"升級組織據點",ActionKind.UpgradeSettlementBuilding=>"升級生產建築",_=>"建造建築"};
    private static string Explain(string error)=>error switch{
        "Must arrive at settlement"=>"角色須先到達聚落；目前仍可查詢。",
        "Population requirement unmet"=>"人口未達升級門檻。",
        "Insufficient settlement upgrade assets"=>"聚落升級所需錢財或木材不足。",
        "Insufficient sponsor construction assets"=>"出資組織的錢財或木材不足。",
        "Shop needs employees and a supported item"=>"商鋪尚未僱人，或不買賣此物品。",
        "Insufficient shop or seller stock"=>"賣方物品庫存不足。",
        "Insufficient buyer funds"=>"買方錢財不足。",
        "Actor busy in timed action"=>"角色正在執行行動，請先推進Tick。",
        "Invalid building terrain or unavailable settlement land"=>"建造位置不符合地形或土地權利。",
        "Invalid organization support"=>"需選擇由主角領導的組織，且只接受建材或錢財。",
        "Insufficient donor assets"=>"捐助組織庫存不足。",
        "Insufficient resources"=>"角色持有的物資不足。",
        "Shop unable to pay wages"=>"商鋪無法負擔工資，暫停交易。",
        "Employee limit or available residents exceeded"=>"超過此建築員工上限或可用居民數。",
        "Sponsor and settlement management permission required"=>"須同時有出資組織與聚落的管理權限。",
        "Settlement construction not enabled in this save"=>"此存檔未啟用建造，請建立新聚落情境。",
        "Organization site required before settlement construction"=>"請先建立組織分舵或總舵。",
        "Organization site building capacity reached"=>"此組織建築配額已滿，請升級分舵／總舵。",
        "Organization already has a site in this settlement"=>"該組織在此聚落已有分舵或總舵。",
        "Insufficient sponsor site assets"=>"組織據點所需錢財或木材不足。",
        "Public building operates automatically without employees"=>"聚落公有建築按滿員自動營運，不需僱員。",
        "Building owner permission required"=>"需建築所屬組織管理權限。",
        "Insufficient building owner upgrade assets"=>"建築所屬帳戶的升級資源不足。",
        "Settlement management permission required"=>"沒有該聚落管理權限。",
        "Building operation not enabled"=>"此建築尚未開放僱員營運。",
        "Insufficient daily AP"=>"今日行動點不足。",_=>error};
    private static string ItemName(WorldState w,string id)=>w.Items.SingleOrDefault(i=>i.Id==id)?.Name??id;
    private static string AccountName(WorldState w,string id)=>w.Organizations.SingleOrDefault(o=>o.Id==id)?.Name??
        w.Characters.SingleOrDefault(c=>c.Id==id)?.Name??(id=="consumed"?"消耗":id=="$genesis"?"初始配置":id=="$settlement-production"?"設施產出":id);
    private static string Reason(string id)=>id switch{
        "SettlementWages"=>"居民工資","SettlementConstruction"=>"組織建造","SettlementSupport"=>"組織援助",
        "SettlementShopPayment"=>"商鋪成交款","SettlementShopGoods"=>"商鋪交貨","SettlementUpgrade"=>"聚落升級",
        "SettlementProduced"=>"設施產出","SettlementProductionTax"=>"實物生產稅","OrganizationSitePayment"=>"組織據點費用",
        "SettlementDailyConsumption"=>"居民每日消耗","SettlementConsumptionIncome"=>"居民消費收入",
        "SettlementBuildingUpgrade"=>"建築升級","Genesis"=>"初始配置",_=>id};
    private void RefreshQueries(WorldState w,SettlementState s)
    {
        var r=w.SettlementRules!;var site=w.HexMap!.Sites.Single(t=>t.Id==s.SiteId);var actor=w.Characters.Single(c=>c.Id==w.ControlledCharacterId);
        bool present=actor.RegionId=="valley"&&HexNavigation.Position(w.HexMap,actor)==site.Anchor;
        var level=r.Levels.SingleOrDefault(l=>l.Level==s.Level+1);
        overview.Text=$"{site.Name}｜目前Tick {w.Clock.Tick}\n角色位置：{actor.HexPosition}；聚落位置：{site.Anchor}｜{(present?"已抵達":"遠端查詢，操作須到場")}\n"+
            $"公共資產：{AccountName(w,s.OrganizationId)}｜閒置可工作居民 {s.WorkingPopulation-s.Buildings.Sum(b=>b.EmployeeCount)}\n"+
            (level is null?"已達目前設定最高等級。":$"下一級 Lv.{level.Level}：人口 {s.Population}/{level.RequiredPopulation}、錢財 {Inventory.Balance(w,s.OrganizationId,"spirit-stone")}/{level.Stones}、木材 {Inventory.Balance(w,s.OrganizationId,"wood")}/{level.Wood}")+"\n"+
            (w.SettlementDailyEconomy is null?"":$"升級保留：錢財 {SettlementDailyEconomy.Reserved(w,s,"spirit-stone")}、木材 {SettlementDailyEconomy.Reserved(w,s,"wood")}；每日人口消費上限："+
                string.Join("、",w.SettlementDailyEconomy.Consumption.Select(c=>$"{ItemName(w,c.ResourceId)} {s.Population*c.UnitsPerResident}～{s.Population*c.MaximumUnits}份，每份換{c.MoneyPerUnit}錢財"))+"，盡量使用保留量以外庫存至上限。\n")+
            (w.SettlementDailyEconomy?.PopulationGrowth is not { } growth?"":$"人口增長值：{s.GrowthProgress}/{growth.Threshold}｜每完整糧食＋木材組相對最低需求增減{growth.PointsPerGroup}點；滿值增加{growth.PopulationPerThreshold}人，住房滿額保留進度。\n")+
            "組織影響力："+(s.Influence.Count==0?"尚無":string.Join("、",s.Influence.Select(p=>$"{AccountName(w,p.Key)} {p.Value}")))+"\n"+
            (w.SettlementDailyEconomy?.PopulationGrowth is null?"人口成長、幸福度效果、維護費及影響力用途尚未啟用。":"人口成長已啟用；幸福度效果、維護費及影響力用途尚未啟用。");
        workforce.Text=string.Join("\n\n",s.Buildings.Select((b,n)=>{
            var next=w.SettlementOperationRules?.FixedDailySettlement==true?
                checked((w.Clock.Tick/w.Time!.Rules.TicksPerDay+1)*w.Time.Rules.TicksPerDay):b.PaidThroughTick+w.Time!.Rules.TicksPerDay;
            bool auto=SettlementOperations.Automatic(w,s,b);int effective=auto?b.EmployeeLimit:b.EmployeeCount;
            string state=b.Kind is SettlementBuildingKind.Smithy or SettlementBuildingKind.Pharmacy?"營運未開放":
                !w.SettlementBuildingRules!.Single(d=>d.Kind==b.Kind).RequiresEmployees?"一般建築，不需僱員":auto?$"公有滿員自動營運｜下次 Tick {next}":b.EmployeeCount==0?"未僱用居民":$"下次結算 Tick {next}";
            return $"{n+1}. {BuildingName(b.Kind)} Lv.{b.Level??r.InitialLevel}｜{AccountName(w,SettlementOperations.Owner(w,s,b))}｜位置 {b.Position}\n實際員工 {b.EmployeeCount}/{b.EmployeeLimit}｜{state}\n"+
                $"每期工資 {(auto?0:b.EmployeeCount*r.DailyWage)} 錢財"+(r.Outputs.TryGetValue(b.Kind,out var item)?$"｜稅前產出 {ItemName(w,item)} {effective*r.DailyOutput(b.Kind)}｜小數稅額累積 {b.TaxRemainder}":"");}));
        var shop=s.Buildings.SingleOrDefault(b=>b.Id==shopIds.ElementAtOrDefault(shops.Selected));string shopOwner=shop is null?s.OrganizationId:SettlementOperations.Owner(w,s,shop);
        stocks.Text=$"交易商鋪所屬：{AccountName(w,shopOwner)}\n物品｜聚落庫存｜商鋪庫存｜角色持有｜收購／販售\n"+string.Join("\n",r.Prices.Select(p=>
            $"{ItemName(w,p.Item)}｜{Inventory.Balance(w,s.OrganizationId,p.Item)}｜{Inventory.Balance(w,shopOwner,p.Item)}｜{Inventory.Balance(w,actor.Id,p.Item)}｜{p.Buy}／{p.Sell}"))+
            $"\n角色錢財：{Inventory.Balance(w,actor.Id,"spirit-stone")}\n商鋪使用所屬帳戶的資金及庫存，沒有無限收購資金。";
        if(w.SettlementDailyEconomy is not null)stocks.Text+=$"\n聚落可動用：錢財 {SettlementDailyEconomy.Available(w,s,s.OrganizationId,"spirit-stone")}、木材 {SettlementDailyEconomy.Available(w,s,s.OrganizationId,"wood")}；已扣除下一級升級保留量。";
        var constructionIds=w.SettlementConstruction.Where(c=>c.SettlementId==s.Id).Select(c=>c.Id).ToHashSet(StringComparer.Ordinal);
        constructionIds.UnionWith(w.SettlementDays.Where(d=>d.SettlementId==s.Id).Select(d=>d.Id));
        constructionIds.UnionWith(w.SettlementBuildingUpgrades.Where(d=>d.SettlementId==s.Id).Select(d=>d.Id));
        var localSites=w.OrganizationSites.Where(site=>site.SettlementId==s.Id).Select(site=>site.Id).ToHashSet(StringComparer.Ordinal);
        constructionIds.UnionWith(w.OrganizationSiteReceipts.Where(d=>localSites.Contains(d.SiteId)).Select(d=>d.Id));
        var ledger=w.Ledger.Where(e=>e.From==s.OrganizationId||e.To==s.OrganizationId||e.From==s.ResidentsAccountId||e.To==s.ResidentsAccountId||
            constructionIds.Contains(e.ActionId)).ToList();
        var cash=ledger.Where(e=>e.ResourceId=="spirit-stone").ToList();
        long income=cash.Where(e=>e.To==s.OrganizationId&&e.From!="$genesis").Sum(e=>e.Quantity),expense=cash.Where(e=>e.From==s.OrganizationId).Sum(e=>e.Quantity);
        accounts.Text=$"聚落累計收入（不含初始配置）：{income} 錢財\n聚落累計支出：{expense} 錢財\n"+
            $"已發居民工資：{w.SettlementDays.Where(d=>d.SettlementId==s.Id).Sum(d=>d.Wages)} 錢財\n"+
            $"組織建造出資：{w.SettlementConstruction.Where(c=>c.SettlementId==s.Id).Sum(c=>c.Stones)} 錢財（不列為聚落支出）\n"+
            $"實物生產稅率：{w.SettlementTaxRules?.ProductionRate.ToString("P0")??"未啟用"}｜已收生產稅 {w.SettlementDays.Where(d=>d.SettlementId==s.Id).Sum(d=>d.TaxQuantity)} 份\n"+
            $"居民消費累計收入：{w.SettlementConsumption.Where(c=>c.SettlementId==s.Id).Sum(c=>c.Income)} 錢財\n"+
            "以下顯示最近50筆符合篩選的帳本；商品收入不等於扣除工資後的淨利。";
        var records=ledger.OrderByDescending(e=>e.Sequence).Select(e=>$"Tick {e.Tick}｜{Reason(e.Reason)}｜{AccountName(w,e.From)} → {AccountName(w,e.To)}｜{ItemName(w,e.ResourceId)} {e.Quantity}").ToList();
        records.InsertRange(0,w.Events.Where(e=>e.Kind=="SettlementStopped"&&e.ActionId.StartsWith("settlement-day:"+s.Id+":",StringComparison.Ordinal))
            .OrderByDescending(e=>e.Sequence).Select(e=>$"Tick {e.Tick}｜停工｜工資不足：{e.Detail}"));
        records.InsertRange(0,w.SettlementPopulationGrowth.Where(p=>p.SettlementId==s.Id).OrderByDescending(p=>p.Tick).Select(p=>$"Tick {p.Tick}｜人口成長｜完整消耗{p.ConsumedGroups}組／最低{p.MinimumGroups}組｜增長值{p.Delta:+0;-0;0}→{p.ProgressAfter}｜人口{p.PopulationBefore}→{p.PopulationAfter}"));
        foreach(var key in pendingOrders.Where(p=>p.Value.Settlement==s.Id).ToArray()){
            var ev=w.Events.LastOrDefault(e=>e.ActionId==key.Key&&e.Kind is "Rejected" or "TimedCompletionRejected" or "SettlementActionCompleted" or "Completed");
            string result=ev is null?"已排隊，等待Tick":ev.Kind is "Rejected" or "TimedCompletionRejected"?"失敗："+Explain(ev.Detail):"已完成";
            records.Insert(0,$"操作｜{key.Value.Name}｜{result}");if(feedbackOrderId==key.Key)feedback.Text=key.Value.Name+"："+result;
        }
        history.Text=string.Join("\n",records.Where(line=>line.Contains(querySearch.Text,StringComparison.OrdinalIgnoreCase)).Take(50));
        if(history.Text.Length==0)history.Text="沒有符合條件的紀錄。";
        foreach(var pair in actionButtons){
            var intent=CreateIntent(w,s,pair.Key);var error=ActionValidator.Validate(w,intent);
            pair.Value.Disabled=error is not null;pair.Value.TooltipText=error is null?"可執行；送出後推進Tick結算。":Explain(error);
        }
    }
}
