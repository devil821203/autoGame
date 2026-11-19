using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class SettlementPanel
{
    private readonly OptionButton siteKinds=new(),shops=new();
    private readonly List<OrganizationSiteKind> siteKindIds=[];
    private readonly List<string> shopIds=[];
    private readonly Label organizationInfo=QueryLabel();
    private void OrganizationSiteUi(VBoxContainer tab)
    {
        var row=new HBoxContainer();tab.AddChild(row);row.AddChild(siteKinds);
        AddAction(row,"建立分舵／總舵",ActionKind.CreateOrganizationSite);AddAction(row,"升級組織據點",ActionKind.UpgradeOrganizationSite);
        tab.AddChild(organizationInfo);siteKinds.ItemSelected+=_=>UpdateFromControls();shops.ItemSelected+=_=>UpdateFromControls();
    }
    private string SelectedOrganizationSite(WorldState w,SettlementState s)=>w.OrganizationSites.SingleOrDefault(site=>
        site.SettlementId==s.Id&&site.OrganizationId==donorIds.ElementAtOrDefault(donors.Selected))?.Id??"";
    public void ChooseOrganizationSite(OrganizationSiteKind kind)
    {
        int n=siteKindIds.IndexOf(kind);if(n<0)throw new ArgumentException("Site type unavailable");siteKinds.Select(n);UpdateFromControls();
    }
    private void RefreshOrganizationSites(WorldState w,SettlementState s)
    {
        var kinds=w.OrganizationSiteRules?.Sites.Select(d=>d.Kind).ToList()??[];
        if(!siteKindIds.SequenceEqual(kinds)){
            siteKinds.Clear();siteKindIds.Clear();foreach(var kind in kinds){siteKindIds.Add(kind);siteKinds.AddItem(w.OrganizationSiteRules!.Sites.Single(d=>d.Kind==kind).Name);}}
        organizationInfo.Text=w.OrganizationSiteRules is not { } r?"此存檔保留舊建造模式。":
            $"據點建立：{r.ConstructionStones}錢財＋{r.ConstructionWood}木材；升級：{r.UpgradeStones}錢財＋{r.UpgradeWood}木材。\n"+
            string.Join("\n",w.OrganizationSites.Where(site=>site.SettlementId==s.Id).Select(site=>
                $"{AccountName(w,site.OrganizationId)}｜{r.Sites.Single(d=>d.Kind==site.Kind).Name} Lv.{site.Level}｜可建築 {OrganizationSiteCapacity.Used(w,site)}/{OrganizationSiteCapacity.Limit(r,site)}｜錢財 {Inventory.Balance(w,site.OrganizationId,"spirit-stone")}｜木材 {Inventory.Balance(w,site.OrganizationId,"wood")}｜成員 {w.Characters.Count(c=>c.OrganizationId==site.OrganizationId)}"));
        var next=s.Buildings.Where(b=>b.Kind==SettlementBuildingKind.Shop).ToList();
        if(!shopIds.SequenceEqual(next.Select(b=>b.Id))){shops.Clear();shopIds.Clear();foreach(var b in next){shopIds.Add(b.Id);shops.AddItem(AccountName(w,Cultivation.Simulation.SettlementOperations.Owner(w,s,b))+"｜商鋪");}}
    }
}
