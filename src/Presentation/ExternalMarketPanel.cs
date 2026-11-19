using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class ExternalMarketPanel : VBoxContainer
{
    public Func<WorldState>? ReadWorld { get; set; }
    public event Action<ActionIntent>? Requested;
    private readonly Label summary=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly OptionButton items=new();
    private readonly OptionButton accounts=new();
    private readonly SpinBox quantity=new(){MinValue=1,MaxValue=int.MaxValue,Value=1,Step=1};
    private readonly Button buy=new(){Text="向市場購買"};
    private readonly Button sell=new(){Text="賣給市場"};
    private readonly Label quote=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Label feedback=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly List<string> itemIds=[];
    private readonly List<string?> ownerIds=[];
    private object? session;
    public string QuoteText=>quote.Text;
    public void ChooseItem(string item,long count)
    {
        int index=itemIds.IndexOf(item);if(index<0)throw new ArgumentException("Unknown external market item");
        items.Select(index);quantity.Value=count;UpdateQuote();
    }
    public void Purchase()=>Command(true);
    public void Sell()=>Command(false);
    public override void _Ready()
    {
        AddChild(summary);AddChild(accounts);AddChild(items);AddChild(quantity);AddChild(quote);
        var row=new HBoxContainer();AddChild(row);row.AddChild(buy);row.AddChild(sell);AddChild(feedback);
        buy.Pressed+=()=>Command(true);sell.Pressed+=()=>Command(false);
        items.ItemSelected+=_=>UpdateQuote();accounts.ItemSelected+=_=>UpdateQuote();quantity.ValueChanged+=_=>UpdateQuote();
    }
    public void Refresh(WorldState w,object identity)
    {
        if(!ReferenceEquals(identity,session)){session=identity;feedback.Text="";}
        string? item=itemIds.ElementAtOrDefault(items.Selected),owner=ownerIds.ElementAtOrDefault(accounts.Selected);
        itemIds.Clear();items.Clear();ownerIds.Clear();accounts.Clear();
        ownerIds.Add(null);accounts.AddItem("角色本人");
        foreach(var o in w.Organizations.Where(o=>o.LeaderId==w.ControlledCharacterId&&o.Id!="consumed"))
        {ownerIds.Add(o.Id);accounts.AddItem(o.Name+"庫存");}
        accounts.Select(Math.Max(0,ownerIds.IndexOf(owner)));
        if(w.ExternalMarket is not { } m)
        {summary.Text="尚未啟用。請新建「村內組織測試」；舊存檔不自動轉換。";buy.Disabled=sell.Disabled=true;quote.Text="";return;}
        foreach(var key in m.Rules.BasePrices.Keys.OrderBy(k=>k,StringComparer.Ordinal)
            .Concat(w.LifeSystem?.Equipment.Where(g=>Inventory.Balance(w,owner??w.ControlledCharacterId,g.Id)>0).Select(g=>g.Id)??[]))
        {itemIds.Add(key);items.AddItem(w.Items.Single(t=>t.Id==key).Name);}
        if(itemIds.Count>0)items.Select(Math.Max(0,item is null?-1:itemIds.IndexOf(item)));
        summary.Text="常駐測試市場：村莊內交易，資金／供貨無上限。收購低於基準價；販售基準價2倍。總價向下取整，不足1錢財不成交。裝備購買為單件普通品質，販售前須卸下。";
        UpdateQuote();
    }
    private ActionIntent Intent(WorldState w,bool buying)=>new("external-market-ui:"+w.NextSequence,w.ControlledCharacterId,w.ExternalMarket!.Rules.SettlementId,
        itemIds.ElementAtOrDefault(items.Selected)??"",(long)quantity.Value,0,DecisionSource.Player,buying?ActionKind.ExternalMarketBuy:ActionKind.ExternalMarketSell,
        OrganizationId:ownerIds.ElementAtOrDefault(accounts.Selected));
    private void UpdateQuote()
    {
        if(ReadWorld?.Invoke() is not { ExternalMarket:not null } w||items.Selected<0)return;
        var purchase=Intent(w,true);var sale=Intent(w,false);
        long b,s;
        try{b=ExternalMarketPricing.Total(w,purchase.ResourceId,purchase.Quantity,true);s=ExternalMarketPricing.Total(w,sale.ResourceId,sale.Quantity,false);}
        catch(OverflowException){quote.Text="數量或總價超出可交易範圍。";buy.Disabled=sell.Disabled=true;return;}
        var be=ActionValidator.Validate(w,purchase);var se=ActionValidator.Validate(w,sale);
        quote.Text=$"基準價 {w.ExternalMarket.Rules.BasePrices[ExternalMarketPricing.Definition(w,purchase.ResourceId)]}；持有 {Inventory.Balance(w,ExternalMarketActions.Owner(purchase),purchase.ResourceId)}；購買總價 {b}，出售總價 {s} 錢財\n購買：{be??"可交易"}；出售：{se??"可交易"}";
        buy.Disabled=be is not null;sell.Disabled=se is not null;
    }
    private void Command(bool buying)
    {
        if(ReadWorld?.Invoke() is not { ExternalMarket:not null } w)return;
        var i=Intent(w,buying);var error=ActionValidator.Validate(w,i);
        if(error is not null){feedback.Text=error;return;}
        Requested?.Invoke(i);feedback.Text="已排隊，推進Tick後在角色記錄或事件帳本查看交易結果。";
    }
}
