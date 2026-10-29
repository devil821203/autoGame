using System.Globalization;
using Cultivation.Domain;
using Cultivation.Simulation;
using Godot;

namespace Cultivation.Presentation;

public partial class CharacterAttributesWindow : Window
{
    public override void _ExitTree() => details.Clear();
    private OptionButton characters = null!;
    private RichTextLabel details = null!;
    private WorldState? snapshot;
    private object? session;
    private string? selectedId;
    private string[] ids = [], names = [];
    public string DisplayText => details.Text;
    public string? SelectedActorId => selectedId;
    private static readonly (string Title, string Fields)[] Groups = [
        ("身分與位置", "Id:角色ID|Name:姓名|Identity:身分|TestProfession:測試專職|Aspiration:志向|RegionId:區域|DemoLocationId:目前據點|HexPosition:六角座標|OrganizationId:組織|Position:職位|MentorId:師父|MonsterKind:怪物類型|Hostile:敵對"),
        ("生命與狀態", "SeverelyInjured:重傷|Health:生命|Mindset:心境|Stamina:體力狀態"),
        ("養成、天賦與裝備", "MortalCultivation:凡人修為|CraftedWeaponId:鍛造武器|EquippedArmorId:防具|EquipmentAttackBonus:裝備攻擊加成|EquipmentDefenseBonus:裝備防禦加成|LifeSkills:生活技藝資料|Realm:境界|Cultivation:修為|MethodId:功法|MethodExperience:功法熟練|AlchemyTalent:煉丹天賦倍率|AlchemyChance:基礎煉丹成功率|AlchemyExperience:煉丹熟練|GatheringExperience:採集熟練|CombatExperience:戰鬥熟練|EquippedWeaponId:裝備武器|BaseAttack:基礎攻擊|BaseDefense:基礎防禦|Reputation:聲望"),
        ("性格、需求與決策參數", "WealthWeight:財富權重|GrowthWeight:成長權重|Caution:謹慎|SocialWeight:社交權重|MarketAffinity:市場偏好|SectAffinity:宗門偏好|CombatDrive:戰鬥動機|DesiredStones:期望錢財|DesiredCultivation:期望修為|DesiredReputation:期望聲望|SocialCooldownUntil:社交冷卻截止Tick")
    ];
    public override void _Ready()
    {
        Title = "角色完整屬性"; Size = new(620,650); MinSize = new(380,300); CloseRequested += Hide;
        var margin = new MarginContainer(); AddChild(margin); margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column = new VBoxContainer(); margin.AddChild(column);
        column.AddChild(new Label { Text = "唯讀觀察：預設主角色；選取NPC不會改變玩家操控角色。" });
        characters = new OptionButton(); column.AddChild(characters);
        characters.ItemSelected += n => SelectActor(ids[(int)n]);
        details = new RichTextLabel { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; column.AddChild(details);
    }
    public void SelectActor(string id)
    {
        if (snapshot is null || !ids.Contains(id)) return;
        selectedId = id; characters.Select(Array.IndexOf(ids,id)); Render();
    }
    public void Refresh(WorldState world, object identity)
    {
        if (!ReferenceEquals(session,identity)) { session = identity; selectedId = world.ControlledCharacterId; }
        snapshot = world;
        var nextIds = world.Characters.Select(c=>c.Id).ToArray(); var nextNames = world.Characters.Select(c=>c.Name).ToArray();
        if (!ids.SequenceEqual(nextIds) || !names.SequenceEqual(nextNames))
        {
            ids = nextIds; names = nextNames; characters.Clear();
            foreach(var c in world.Characters) characters.AddItem($"{c.Name} ({c.Id})");
        }
        if (!ids.Contains(selectedId)) selectedId = world.ControlledCharacterId;
        characters.Select(Array.IndexOf(ids,selectedId)); Render();
    }
    private void Render()
    {
        if (snapshot is not { } w || selectedId is null) return;
        var a = w.Characters.Single(c=>c.Id==selectedId); var lines = new List<string> { $"{a.Name}｜目前Tick {w.Clock.Tick}" };
        Title = "角色完整屬性 — " + a.Name;
        string Value(object? value) => value switch {
            MortalCultivationState => "見下方凡人修為明細",
            LifeSkills life => $"醫術熟練 {life.MedicineExperience}／製藥熟練 {life.PharmacyExperience}／鍛造熟練 {life.SmithingExperience}",
            null => "無／未啟用", bool flag => flag ? "是" : "否",
            double number => number.ToString("0.####",CultureInfo.InvariantCulture),
            StaminaState s => $"{s.Current}／{w.StaminaRules?.Maximum.ToString() ?? "未啟用"}；休息：{(s.Resting?"是":"否")}；昏厥：{(s.Unconscious?"是":"否")}；住處：{s.HomeLocationId??"無"}；有床：{(s.HasBed?"是":"否")}",
            TestProfession profession => profession switch { TestProfession.Physician => "醫師", TestProfession.Pharmacist => "藥師", TestProfession.Smith => "鍛造師", TestProfession.SpiritTraining => "性功修練者", TestProfession.BodyTraining => "命功修練者", TestProfession.QiTraining => "炁功修練者", _ => "無" },
            Aspiration aspiration => w.Aspirations.SingleOrDefault(d=>d.Id==aspiration)?.Name ?? aspiration.ToString(),
            CharacterIdentity identity => identity switch { CharacterIdentity.Player=>"玩家",CharacterIdentity.Alchemist=>"煉丹師",CharacterIdentity.Herbalist=>"採藥師",CharacterIdentity.Fighter=>"戰鬥者",_=>"散修" },
            OrganizationPosition position => position switch {OrganizationPosition.Leader=>"宗主／首領",OrganizationPosition.Elder=>"長老",OrganizationPosition.Member=>"成員",_=>"無"},
            CharacterCondition condition => condition switch {CharacterCondition.Healthy=>"健康",CharacterCondition.Injured=>"受傷",_=>"失能"},
            _ => value.ToString() ?? "無"
        };
        var properties = typeof(CharacterState).GetProperties(); var displayed = new HashSet<string>();
        foreach(var group in Groups)
        {
            lines.Add("\n【"+group.Title+"】");
            foreach(var field in group.Fields.Split('|'))
            {
                var parts=field.Split(':'); var p=properties.Single(p=>p.Name==parts[0]); displayed.Add(p.Name);
                lines.Add($"{parts[1]}：{Value(p.GetValue(a))}");
            }
        }
        // New persisted character fields remain visible even before their translated labels are added.
        foreach(var p in properties.Where(p=>!displayed.Contains(p.Name))) lines.Add($"{p.Name}：{Value(p.GetValue(a))}");
        var stats=CharacterProgression.Stats(a,w.Advancement); var growth=CharacterGrowthCalculator.Modifiers(w,a);
        lines.Add("\n【目前生效數值（共用核心計算）】");
        lines.Add($"攻擊：{stats.Attack}｜防禦：{stats.Defense}｜狀態：{(a.SeverelyInjured?"重傷":Value(stats.Condition))}");
        lines.Add($"功法等級：{stats.MethodLevel}｜煉丹等級：{stats.AlchemyLevel}｜採集等級：{stats.GatheringLevel}");
        lines.Add($"實際煉丹成功率：{CharacterDevelopment.SuccessChance(a,w.Rules):P1}");
        lines.Add($"心境倍率：{growth.MindsetMultiplier:0.###}｜疲勞倍率：{growth.FatigueMultiplier:0.###}｜合計養成倍率：{growth.CombinedMultiplier:0.###}");
        if(StaminaCalculator.Status(w,a) is {} stamina)
            lines.Add($"疲勞：{Value(stamina.Fatigued)}｜休息環境：{stamina.Environment switch {RestEnvironment.HomeBed=>"住處有床",RestEnvironment.Ordinary=>"一般",_=>"露宿"}}｜體力回復／Tick：{stamina.RecoveryPerTick}");
        lines.Add("\n【時間、行動與計畫】");
        if(w.Time is {} time)
        {
            var ap=time.Budgets.SingleOrDefault(b=>b.ActorId==a.Id);
            lines.Add($"AP：{ap?.Remaining.ToString()??time.Rules.DailyActionPoints.ToString()}／{time.Rules.DailyActionPoints}｜忙碌剩餘Tick：{Math.Max(0,(ap?.BusyThroughTick??w.Clock.Tick)-w.Clock.Tick)}");
        }
        else lines.Add("AP：此情境未啟用");
        foreach(var task in w.Scheduled.Where(s=>s.Intent.ActorId==a.Id)) lines.Add($"排程：{task.Intent.Kind} → {task.Intent.TargetId}｜完成Tick：{task.DueTick}");
        foreach(var task in w.Time?.Deferred.Where(d=>d.Intent.ActorId==a.Id)??[]) lines.Add($"進行：{task.Intent.Kind}｜完成Tick：{task.DueTick}");
        foreach(var pending in w.Pending.Where(p=>p.Intent.ActorId==a.Id)) lines.Add("待提交："+pending.Intent.Kind);
        var plan=w.Plans.SingleOrDefault(p=>p.ActorId==a.Id); lines.Add("計畫："+(plan is null?"無":plan.Goal+" → "+string.Join(" → ",plan.Steps)));
        if(w.BoardBattle is {} battle && battle.Fighters.SingleOrDefault(f=>f.ActorId==a.Id) is {} fighter)
            lines.Add($"方格戰鬥：{battle.Phase}｜第{battle.Round}輪｜站位 {fighter.Side} Row {fighter.Cell.Row}, Depth {fighter.Cell.Depth}｜先攻 {fighter.Speed}｜射程 {fighter.Range}｜遠程 {Value(fighter.Ranged)}");
        if (w.LifeSkillRules is {} lifeRules && a.LifeSkills is {} life)
        {
            lines.Add("\n【生活技藝】");
            foreach (var kind in Enum.GetValues<LifeSkillKind>())
            {
                string label=kind switch {LifeSkillKind.Medicine=>"醫術",LifeSkillKind.Pharmacy=>"製藥",_=>"鍛造"};
                lines.Add($"{label}：Lv.{LifeSkillCalculator.Level(life,kind,lifeRules)}｜熟練 {LifeSkillCalculator.Experience(life,kind)}｜每級 {lifeRules.ExperiencePerLevel}");
            }
            lines.Add("普通醫療基礎恢復："+lifeRules.TreatmentHealing+"");
        }        lines.Add("\n【目前庫存】");
        var resources=w.Items.Select(i=>i.Id).Concat(w.Ledger.Where(l=>l.To==a.Id||l.From==a.Id).Select(l=>l.ResourceId)).Distinct().OrderBy(id=>id,StringComparer.Ordinal);
        foreach(var id in resources) lines.Add($"{w.Items.SingleOrDefault(i=>i.Id==id)?.Name??id}：{Inventory.Balance(w,a.Id,id)}");
        lines.Add(a.MortalCultivation is null ? "\n精神力、靈力、性命炁基礎屬性與其他技藝尚未在此情境啟用。" : "\n精神力、靈力與未定派生公式尚未接入；凡人屬性基值與修練成長列於下方。");
        if (a.MortalCultivation is not null) lines.Add("\n【凡人修為】\n" + MortalCultivationPanel.Describe(w, a));
        var equipped=new[]{a.CraftedWeaponId,a.EquippedWeaponId,a.EquippedArmorId}.Where(id=>id is not null).Select(id=>id!).ToArray();
        if(equipped.Length>0)lines.Add("\n【裝備耐久】\n"+string.Join("\n",equipped.Select(id=>$"{w.Items.SingleOrDefault(i=>i.Id==id)?.Name??id}：{EquipmentCondition.Description(w,id)}")));
        string output=string.Join("\n",lines); if(details.Text!=output) details.Text=output;
    }
}
