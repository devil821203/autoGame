using Godot;
using Cultivation.AI;
using Cultivation.Domain;
using Cultivation.Persistence;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class Main : Control
{
    private SimulationEngine engine = new(TestScenarioCatalog.CreateDefault());
    private TickPump pump = null!;
    private CharacterJournalWindow characterJournal = null!;
    private readonly List<Button> pillActions = [];
    private CharacterAttributesWindow characterAttributes = null!;
    private PaidServicePanel paidServices = null!;
    private LifeSkillPanel lifeSkills = null!;
    private ExternalMarketPanel externalMarket = null!;
    private OrganizationLaborPanel organizationLabor = null!;
    private Label status = null!;
    private Label notice = null!;
    private Button waitOneButton = null!, waitTenButton = null!;
    private RichTextLabel log = null!;
    private bool paused = true;
    private string? lastSave;
    private FileDialog savePicker = null!;
    private OptionButton actors = null!;
    private Label actorDetails = null!;
    private DeveloperPanel developer = null!;
    private double refreshElapsed;
    private double speed = 1;
    private OptionButton rate = null!;
    private MissionPanel missionPanel = null!;
    private BoardBattlePanel boardBattlePanel = null!;
    private CombatPanel combatPanel = null!;
    private SocialPanel socialPanel = null!;
    private GovernancePanel governancePanel = null!;
    private DemoMapPanel demoMap = null!;
    private EventJournalPanel journal = null!;
    private LineEdit playerName = null!;
    private GameplayPanel gameplay = null!;
    private SectEconomyPanel sectEconomy = null!;
    private SectFacilityPanel sectFacilities = null!;
    private Button newSave = null!;
    private ExpeditionPanel expeditions = null!;
    private PerformancePanel performance = null!;
    private SectScenePanel sectScene = null!;
    private HexMapPanel hexMap = null!;
    private SiteScenePanel siteScene = null!;

    public override void _Ready()
    {
        pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
        var scroll = new ScrollContainer(); AddChild(scroll); scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(panel);
        panel.AddChild(new Label { Text = "自主修仙世界｜模擬與開發者觀察" });
        characterJournal = new CharacterJournalWindow(); AddChild(characterJournal);
        characterAttributes = new CharacterAttributesWindow(); AddChild(characterAttributes);
        settlementWindow=new SettlementManagementWindow(){ReadWorld=()=>engine.Snapshot(),Visible=false};AddChild(settlementWindow);
        settlementWindow.Requested+=intent=>{SubmitTracked(intent);if (!engine.ActionDrivenTimeEnabled) notice.Text="聚落指令已排隊，推進Tick查看結果。";};
        settlementWindow.TickRequested+=()=>StepTicks(1);
        var buttons = new HBoxContainer();
        panel.AddChild(buttons);
        AddButton(buttons, "角色完整屬性", () => { characterAttributes.Refresh(engine.Snapshot(), engine); characterAttributes.PopupCentered(); });
        AddButton(buttons, "角色紀錄視窗", () => { ObserveCharacter(); characterJournal.PopupCentered(); });
        AddButton(buttons, "暫停／繼續", TogglePause);
        waitOneButton = AddButton(buttons, "等待／前進一步", () => StepTicks(1));
        AddButton(buttons, "另存新檔", Save);
        newSave = new Button { Text = "開新存檔（重設初始值）", TooltipText = "依config重建目前情境並儲存新檔；目前未儲存進度會重設，既有存檔保留。" };
        buttons.AddChild(newSave); newSave.Pressed += () => StartNewSave();
        AddButton(buttons, "讀取本次存檔", Load);
        AddButton(buttons, "選取存檔…", () => { savePicker.CurrentDir = ProjectSettings.GlobalizePath("user://saves"); savePicker.PopupCentered(new Vector2I(800, 500)); });
        savePicker = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.json ; 世界存檔" }, Title = "選取世界存檔" };
        AddChild(savePicker);
        savePicker.FileSelected += path => { LoadFromPath(path); Render(); };
        waitTenButton = AddButton(buttons, "等待／前進十步", () => StepTicks(10));
        rate = new OptionButton();
        foreach (string text in new[] { "1 倍速", "2 倍速", "4 倍速" }) rate.AddItem(text);
        rate.ItemSelected += index => { speed = index switch { 1 => 2, 2 => 4, _ => 1 }; Render(); };
        buttons.AddChild(rate);
        status = new Label(); panel.AddChild(status);
        notice = new Label { Text = "初始暫停｜凡人專職測試：醫師、藥師、鍛造師與性／命／炁修練者各1位；到青雲宗可委託有償服務。" }; panel.AddChild(notice);
        actors = new OptionButton(); panel.AddChild(actors);
        foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
        actors.ItemSelected += _ => Render();
        actorDetails = new Label(); panel.AddChild(actorDetails);
        var actions = AddButtonGroup(panel, "玩家行動");
        AddButton(actions, "轉移 10 錢財", SubmitPlayer);
        AddButton(actions, "玩家採藥", () => SubmitAction(ActionKind.Gather, "grove", "herb"));
        AddButton(actions, "購買回復藥物", () => SubmitAction(ActionKind.Buy, "market", engine.Snapshot().RecoveryResourceId));
        pillActions.Add(AddButton(actions, "玩家修煉", () => SubmitAction(ActionKind.Cultivate, "player", "pill")));
        AddButton(actions, "玩家學功法", () => SubmitAction(ActionKind.LearnMethod, "player", "spirit-stone"));
        pillActions.Add(AddButton(actions, "玩家突破", () => SubmitAction(ActionKind.Breakthrough, "player", "pill")));
        AddButton(actions, "玩家裝備武器", () => SubmitAction(ActionKind.EquipWeapon, "player", engine.Snapshot().Advancement?.WeaponId ?? "iron-sword"));
        AddButton(actions, "玩家卸下武器", () => SubmitAction(ActionKind.UnequipWeapon, "player", engine.Snapshot().Advancement?.WeaponId ?? "iron-sword"));
        AddButton(actions, "探索打怪副本", () => SubmitAction(ActionKind.ExploreDungeon, "dungeon", "combat"));
        CreateCurrentScenarioControls(panel);
        var showHistory = new CheckButton { Text = "顯示歷史／專項情境" }; panel.AddChild(showHistory);
        var scenarios = AddButtonGroup(panel, "歷史／專項情境（重建目前世界，請先存檔）");
        var historyFrame = scenarios.GetParent().GetParent() as Control;
        historyFrame!.Visible = false; showHistory.Toggled += visible => historyFrame.Visible = visible;
        AddButton(scenarios, "階段 2 任務", CreateMissionWorld);
        AddButton(scenarios, "階段 3 養成", CreateAdvancementWorld);
        AddButton(scenarios, "階段 3 戰鬥", CreateCombatWorld);
        AddButton(scenarios, "階段 3 社交", CreateSocialWorld);
        AddButton(scenarios, "建立角色／階段4 Demo", CreatePlayerDemo);
        AddButton(scenarios, "宗門經濟／副本測試", CreateSectEconomy);
        AddButton(scenarios, "六角世界／領地測試", CreateHexWorld);
        AddButton(scenarios, "聚落營運測試", CreateSettlementWorld);
        AddButton(scenarios, "村內組織測試", CreateVillageOrganizationWorld);
        AddButton(scenarios, "聚落管理／查詢", OpenSettlementManagement);
        AddButton(scenarios, "階段6 養成測試", CreateGrowthWorld);
        AddButton(scenarios, "玩家自動排程 Demo", CreatePlayerAutoScheduleWorld);
        AddButton(scenarios, "時間與AP測試", CreateTimeWorld);
        AddButton(scenarios, "生活技能測試", CreateLifeSkillWorld);
        AddButton(scenarios, "階段8 凡人修為測試", CreateMortalCultivationWorld);
        AddButton(scenarios, "功法修練測試", CreateMethodWorld);
        AddButton(scenarios, "階段7 雙陣地測試", CreateBoardBattleWorld);
        AddButton(scenarios, "休息（玩家）", () => { var w = engine.Snapshot(); SubmitTracked(new ActionIntent("player-rest:" + w.NextSequence, w.ControlledCharacterId, w.ControlledCharacterId, "stamina", 1, 0, DecisionSource.Player, ActionKind.Rest)); if (!engine.ActionDrivenTimeEnabled) notice.Text = "休息已排隊，請推進Tick。"; });
        AddButton(scenarios, "階段5 效能情境（100 NPC）", CreatePerformanceWorld);
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 440), SizeFlagsVertical = SizeFlags.ExpandFill }; panel.AddChild(tabs);
        hexMap = new HexMapPanel { Name = "六角世界", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(hexMap);
        hexMap.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "六角地圖指令已排隊，請推進 Tick 查看結果。"; };
        hexMap.EnterSect += () => tabs.CurrentTab = tabs.GetTabIdxFromControl(sectScene);
        siteScene = new SiteScenePanel { Name = "獨立場景", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(siteScene);
        hexMap.EnterSite += id => { if (siteScene.Enter(id)) tabs.CurrentTab = tabs.GetTabIdxFromControl(siteScene); };
        siteScene.BackToMap += () => tabs.CurrentTab = tabs.GetTabIdxFromControl(hexMap);
        siteScene.OpenService += id => tabs.CurrentTab = tabs.GetTabIdxFromControl(id switch
        { "missions" => missionPanel, "exchange" => sectFacilities, _ => gameplay });
        sectScene = new SectScenePanel { Name = "青雲宗場景", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(sectScene);
        sectScene.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "場景旅行已排隊，推進 Tick 後查看結果。"; };
        sectScene.OpenPanel += id => tabs.CurrentTab = tabs.GetTabIdxFromControl(id switch
        {
            "missions" => missionPanel,
            "exchange" => sectFacilities,
            _ => governancePanel
        });
        performance = new PerformancePanel { Name = "效能量測", CustomMinimumSize = new(0, 350) }; tabs.AddChild(performance);
        gameplay = new GameplayPanel { Name = "角色與交易", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(gameplay);
        gameplay.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "玩家交易已排隊，推進Tick查看成交或失敗結果。"; };
        sectEconomy = new SectEconomyPanel { Name = "宗門運作", CustomMinimumSize = new(0, 420) }; tabs.AddChild(sectEconomy);
        sectFacilities = new SectFacilityPanel { Name = "宗門設施與兌換", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(sectFacilities);
        sectFacilities.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "宗門工作／兌換指令已排隊，前進Tick查看；須到對應據點且符合資格。"; };
        expeditions = new ExpeditionPanel { Name = "副本組隊與清剿", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(expeditions);
        expeditions.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "組隊／清剿指令已排隊，推進Tick查看結果。"; };
        developer = new DeveloperPanel { Name = "開發者" }; tabs.AddChild(developer);
        demoMap = new DemoMapPanel { Name = "地圖Demo", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(demoMap);
        demoMap.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "旅行已排隊，推進Tick查看結果。"; };
        developer.ActorSelected += id =>
        {
            var world = engine.Snapshot();
            int index = world.Characters.FindIndex(c => c.Id == id);
            if (index >= 0) { actors.Select(index); Render(); }
        };
        log = new RichTextLabel { Name = "事件與帳本", CustomMinimumSize = new Vector2(0, 300) }; tabs.AddChild(log);
        mortalCultivation = new MortalCultivationPanel { Name = "凡人修為", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(mortalCultivation);
        mortalCultivation.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "修練指令已排隊，推進Tick結算。"; };
        paidServices = new PaidServicePanel { Name = "有償服務", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(paidServices);
        paidServices.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "服務委託已排隊，請推進Tick結算；成功才付款交付。"; };
        lifeSkills = new LifeSkillPanel {Name="生活技能",ReadWorld=()=>engine.Snapshot()}; tabs.AddChild(lifeSkills);
        lifeSkills.Requested += intent=>{SubmitTracked(intent);if (!engine.ActionDrivenTimeEnabled) notice.Text="生活指令已排隊，推進Tick後查看材料、進度及結果。";};
        settlements = new SettlementPanel { Name = "聚落營運", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(settlements);
        externalMarket=new ExternalMarketPanel {Name="額外市場",ReadWorld=()=>engine.Snapshot()};tabs.AddChild(externalMarket);
        organizationLabor=new OrganizationLaborPanel{Name="人口與組織人力",ReadWorld=()=>engine.Snapshot()};tabs.AddChild(organizationLabor);
        organizationLabor.Requested+=intent=>{SubmitTracked(intent);if (!engine.ActionDrivenTimeEnabled) notice.Text="人口／組織人力指令已排隊。";};
        externalMarket.Requested+=intent=>{SubmitTracked(intent);if (!engine.ActionDrivenTimeEnabled) notice.Text="額外市場交易已排隊，推進Tick結算。";};
        settlements.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "聚落指令已排隊，推進Tick查看結果。"; };
        journal = new EventJournalPanel { Name = "事件查詢", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(journal);
        missionPanel = new MissionPanel { Name = "宗門任務", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(missionPanel);
        missionPanel.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "已排隊：" + intent.Kind + "；推進 Tick 後查看任務或事件結果。"; };
        combatPanel = new CombatPanel { Name = "戰鬥", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(combatPanel);
        combatPanel.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "已排隊：" + intent.Kind + "；推進 Tick 後查看戰鬥結果。"; };
        boardBattlePanel = new BoardBattlePanel { Name = "雙陣地戰鬥", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(boardBattlePanel);
        boardBattlePanel.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "戰場指令已排隊，推進Tick結算本輪。"; };
        socialPanel = new SocialPanel { Name = "社交與招募", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(socialPanel);
        governancePanel = new GovernancePanel { Name = "組織治理", ReadWorld = () => engine.Snapshot() }; tabs.AddChild(governancePanel);
        governancePanel.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "治理指令已排隊，推進 Tick 查看結果。"; };
        socialPanel.Requested += intent => { SubmitTracked(intent); if (!engine.ActionDrivenTimeEnabled) notice.Text = "已排隊：" + intent.Kind + "；推進 Tick 後查看社交結果，NPC 不會被強制接受。"; };
        Render();
        if (OS.GetCmdlineUserArgs().Contains("--settlement-demo")) { CreateSettlementWorld(); OpenSettlementManagement(); }
        if (OS.GetCmdlineUserArgs().Contains("--village-organization-demo")) { CreateVillageOrganizationWorld(); OpenSettlementManagement(); }
        if (OS.GetCmdlineUserArgs().Contains("--player-auto-schedule-smoke")) PlayerAutoScheduleSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--settlement-smoke")) SettlementSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--scenario-catalog-smoke")) ScenarioCatalogSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--action-driven-smoke")) ActionDrivenSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--village-organization-smoke")) VillageOrganizationSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--external-market-smoke")) ExternalMarketSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--organization-labor-smoke")) OrganizationLaborSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--settlement-management-smoke")) SettlementManagementSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--time-ap-smoke")) TimeApSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--character-follow-smoke")) CharacterFollowSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--character-attributes-smoke")) CharacterAttributesSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--specialists-smoke")) SpecialistsSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--mortal-smoke") || OS.GetCmdlineUserArgs().Contains("--mortal-long-smoke") || OS.GetCmdlineUserArgs().Contains("--method-smoke")) MortalCultivationSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--life-skills-smoke")) LifeSkillsSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--medicine-only-smoke")) MedicineOnlySmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--board-battle-smoke")) BoardBattleSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--stamina-smoke")) StaminaSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--sect-scene-smoke")) SectSceneSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--hex-world-smoke")) HexWorldSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--site-scenes-smoke")) SiteScenesSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--phase1-smoke")) SmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--phase2-smoke")) MissionSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--growth-smoke")) AdvancementSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--combat-smoke")) CombatSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--social-smoke")) SocialSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--governance-smoke")) GovernanceSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--save-picker-smoke")) SavePickerSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--map-smoke")) MapSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--world-events-smoke")) WorldEventsSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--player-demo-smoke")) PlayerDemoSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--gameplay-smoke")) GameplaySmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--time-smoke")) TimeSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--sect-economy-smoke")) SectEconomySmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--sect-facility-smoke")) SectFacilitySmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--new-save-smoke")) NewSaveSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--expedition-smoke")) ExpeditionSmokeTest();
        if (OS.GetCmdlineUserArgs().Contains("--performance-smoke")) PerformanceSmokeTest();
        var args = OS.GetCmdlineUserArgs();
        int create = Array.IndexOf(args, "--e2e-create");
        int resume = Array.IndexOf(args, "--e2e-resume");
        if (create >= 0 || resume >= 0)
        {
            int flag = create >= 0 ? create : resume;
            if (flag + 1 >= args.Length) { GD.PrintErr("E2E manifest path required"); GetTree().Quit(1); }
            else EndToEndSmoke(args[flag + 1], create >= 0);
        }
    }

    private void GovernanceSmokeTest()
    {
        try
        {
            CreateSocialWorld(); governancePanel.Command(ActionKind.SetOrganizationPolicy);
            if (!engine.Step().Single().Accepted || engine.Snapshot().OrganizationPolicies.Count != 1) throw new InvalidOperationException("Policy UI failed");
            governancePanel.Command(ActionKind.PromoteMember);
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Appointment UI failed");
            governancePanel.Command(ActionKind.RewardMember);
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Reward UI failed");
            governancePanel.Command(ActionKind.InviteSuccessor);
            if (!engine.Step().Single().Accepted || !engine.Snapshot().SocialOffers.Any(o => o.Kind == SocialOfferKind.Succession && o.Status == SocialOfferStatus.Pending)) throw new InvalidOperationException("Succession UI failed");
            Render(); string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Governance persistence failed");
            GD.Print("GOVERNANCE_UI_SMOKE_PASS: policy, appointment, reward, succession nomination, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("GOVERNANCE_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void MapSmokeTest()
    {
        try
        {
            CreateSocialWorld(); var w = engine.Snapshot(); w.DemoMapEnabled = true; w.HerbRegeneration = new(); engine = new(w);
            demoMap.Travel("grove");
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Map travel rejected");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Travel save failed");
            for (int n = 0; n < 3; n++) engine.Step();
            if (engine.Snapshot().Characters.Single(c => c.Id == "player").DemoLocationId != "grove") throw new InvalidOperationException("Map travel did not arrive");
            Render(); GD.Print("MAP_UI_SMOKE_PASS: map command, scheduled travel, save/load, arrival"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("MAP_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void CreatePlayerDemo()
    {
        try
        {
            var replacement = NewTestWorld(PlayerDemoScenario.Create(playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "已建立玩家角色與Demo世界（未覆寫存檔）。觀察其他角色不會轉移控制權。"; Render();
        }
        catch (Exception error) { notice.Text = "建立角色失敗：" + error.Message; }
    }

    private void BoardBattleSmokeTest()
    {
        try
        {
            CreateBoardBattleWorld();
            if (engine.Snapshot().BoardBattle is null) throw new InvalidOperationException("Board scenario missing");
                        boardBattlePanel.Refresh(engine.Snapshot());
            if (boardBattlePanel.AttackEnabled) throw new InvalidOperationException("Attack enabled without target");
            boardBattlePanel.ChooseAttackTarget("npc");
            if (!boardBattlePanel.AttackEnabled) throw new InvalidOperationException("Attack button unavailable after selecting enemy");
            boardBattlePanel.PressAttackButton();
            StepTicks(1); Render(); var w = engine.Snapshot();
            if (w.BoardBattle!.Round != 1 || !w.Events.Any(e => e.Kind == "BoardAttacked" && e.Detail.StartsWith("player->", StringComparison.Ordinal)) ||
                !boardBattlePanel.StatusText.Contains("Round 1")) throw new InvalidOperationException("Board input or display failed");
            string digest = WorldCodec.Digest(w); Save(); Load(); Render();
            if (digest != WorldCodec.Digest(engine.Snapshot())) throw new InvalidOperationException("Board save changed state");
            boardBattlePanel.ToggleAutoCombat(true); Render();
            if (!boardBattlePanel.AutoCombatEnabled || engine.Snapshot().BoardBattle!.PlayerAutoCombat) throw new InvalidOperationException("Queued auto toggle display wrong");
            StepTicks(1); Render();
            if (!engine.Snapshot().BoardBattle!.PlayerAutoCombat || !boardBattlePanel.AutoCombatEnabled) throw new InvalidOperationException("Player auto combat toggle failed");
            string autoDigest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (autoDigest != WorldCodec.Digest(engine.Snapshot()) || !boardBattlePanel.AutoCombatEnabled) throw new InvalidOperationException("Auto combat save failed");
            int playerHits = engine.Snapshot().Events.Count(e => e.Kind == "BoardAttacked" && e.Detail.StartsWith("player->", StringComparison.Ordinal));
            boardBattlePanel.ToggleAutoCombat(false); StepTicks(1); Render();
            if (engine.Snapshot().Events.Count(e => e.Kind == "BoardAttacked" && e.Detail.StartsWith("player->", StringComparison.Ordinal)) != playerHits) throw new InvalidOperationException("Auto attacked after toggle off");
            boardBattlePanel.ToggleAutoCombat(true); StepTicks(100); Render();
            if (engine.Snapshot().BoardBattle!.Phase != BoardBattlePhase.Finished) throw new InvalidOperationException("Board battle never ended");
            GD.Print("BOARD_BATTLE_UI_SMOKE_PASS: dual grids, player command, initiative round, NPC attacks, save/load, terminal state"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("BOARD_BATTLE_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
    private void CreateBoardBattleWorld()
    {
        try
        {
            var replacement = NewTestWorld(BoardBattleScenario.Create(playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "已建立雙陣地2對2測試，選普攻目標並按普攻，再推進Tick。";
            if (boardBattlePanel.GetParent() is TabContainer battleTabs) battleTabs.CurrentTab = battleTabs.GetTabIdxFromControl(boardBattlePanel);
            Render();
        }
        catch (Exception error) { notice.Text = "建立戰場失敗：" + error.Message; }
    }

    private void PlayerAutoScheduleSmokeTest()
    {
        try
        {
            CreatePlayerAutoScheduleWorld(); StepTicks(20); Render(); var w = engine.Snapshot();
            gameplay.Refresh(w, w.ControlledCharacterId);
            if (!w.PlayerAutoSchedule || !w.ProcessedIntentIds.Any(id => id.StartsWith("player-auto:", StringComparison.Ordinal)) ||
                !gameplay.DetailsText.Contains("自動排程 啟用")) throw new InvalidOperationException("Player auto demo not active");
            string digest = WorldCodec.Digest(w); Save(); Load(); Render();
            if (digest != WorldCodec.Digest(engine.Snapshot()) || !engine.Snapshot().PlayerAutoSchedule) throw new InvalidOperationException("Auto demo save changed");
            GD.Print("PLAYER_AUTO_SCHEDULE_UI_SMOKE_PASS: scenario, player decisions, source validation, status, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("PLAYER_AUTO_SCHEDULE_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
    private static SimulationEngine NewTestWorld(WorldState world) => new(MedicineOnlyProfile.ApplyFresh(world));
    private void CreateLifeSkillWorld()
    {
        try
        {
            engine=NewTestWorld(LifeSkillScenario.Create(playerName.Text));pump=new(engine,TimeSpan.FromMilliseconds(250),autonomous:true);
            paused=true;developer.ResetHistory();actors.Clear();foreach(var a in engine.Snapshot().Characters)actors.AddItem(a.Name);
            notice.Text="生活技能測試：有限測試鐵料與草／獸皮；可治療、製藥、鍛造及穿戴。重傷需獨立療程；止血散五次緩效。";
            ((TabContainer)lifeSkills.GetParent()).CurrentTab=((TabContainer)lifeSkills.GetParent()).GetTabIdxFromControl(lifeSkills);Render();
        }
        catch(Exception error){notice.Text="建立生活測試失敗："+error.Message;}
    }    private void CreateTimeWorld()
    {
        try
        {
            engine = NewTestWorld(TimeScenario.Create(playerName.Text));
            pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "時間與AP：每日30 Tick／30AP，空閒自主行動；玩家戰鬥停世界，戰後補5 Tick。新情境週期按天，舊檔不轉換。"; Render();
        }
        catch (Exception error) { notice.Text = "建立時間測試失敗：" + error.Message; }
    }
    private void TimeApSmokeTest()
    {
        try
        {
            CreateTimeWorld();
            if (engine.Snapshot().Time is null || !status.Text.Contains("春季1日")) throw new InvalidOperationException("Time scenario or calendar display missing");
            StepTicks(3); Render();
            if (!status.Text.Contains("AP ") || engine.Snapshot().Time!.Charges.Count == 0) throw new InvalidOperationException("AP actions/display missing");
            missionPanel.Publish();
            var world = engine.Snapshot();
            var offer = world.Pending.Single(p => p.Intent.Kind == ActionKind.PublishMission).Intent.Offer!;
            if (offer.DeadlineTick - world.Clock.Tick != 210) throw new InvalidOperationException("UI seven day deadline is not 210 ticks");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (digest != WorldCodec.Digest(engine.Snapshot())) throw new InvalidOperationException("Time save/load changed state");
            GD.Print("TIME_AP_UI_SMOKE_PASS: new scenario, calendar, AP, autonomous actions, seven day mission, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("TIME_AP_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
    private void CreatePlayerAutoScheduleWorld()
    {
        try
        {
            var replacement = NewTestWorld(PlayerAutoScheduleScenario.Create(playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "玩家自動排程Demo：推進Tick即可自主行動；手動指令優先，忙碌工作不重開。優先級排序介面待確認。"; Render();
        }
        catch (Exception error) { notice.Text = "建立自動排程失敗：" + error.Message; }
    }
    private void CreateGrowthWorld()
    {
        try
        {
            var replacement = NewTestWorld(GrowthScenario.Create(playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "階段6體力測試：每次活動完成耗10，疲勞收益50%；可按休息並推進Tick。"; Render();
        }
        catch (Exception error) { notice.Text = "建立養成測試失敗：" + error.Message; }
    }

    private void StaminaSmokeTest()
    {
        try
        {
            CreateGrowthWorld();
            if (engine.Snapshot().StaminaRules is null) throw new InvalidOperationException("Scenario creation failed");
            var w = engine.Snapshot();
            SubmitTracked(new("stamina-ui-travel", w.ControlledCharacterId, "sect", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
            StepTicks(1);
            if (engine.Snapshot().Characters.Single(c => c.Id == w.ControlledCharacterId).Stamina!.Current != 100) throw new InvalidOperationException("Travel cost deducted before completion");
            StepTicks(15); Render();
            gameplay.Refresh(engine.Snapshot(), "npc:00");
            if (!gameplay.DetailsText.Contains("心境 50") || !gameplay.DetailsText.Contains("體力")) throw new InvalidOperationException("Growth details missing" );
            gameplay.Rest();
            StepTicks(1); StepTicks(1); Render();
            if (engine.Snapshot().Characters.Single(c => c.Id == w.ControlledCharacterId).Stamina!.Current != 100) throw new InvalidOperationException("Rest UI failed");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load();
            if (digest != WorldCodec.Digest(engine.Snapshot())) throw new InvalidOperationException("Save changed stamina");
            var injured = engine.Snapshot();
            injured.Characters = injured.Characters.Select(c => c.Id == injured.ControlledCharacterId ? c with { Health = 75, Stamina = c.Stamina! with { Current = 100, Resting = false } } : c).ToList();
            engine = new SimulationEngine(injured); pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true); Render();
            gameplay.Rest(); StepTicks(1);
            if (engine.Snapshot().Characters.Single(c => c.Id == injured.ControlledCharacterId).Health != 75) throw new InvalidOperationException("Rest healed on start Tick");
            StepTicks(1);
            if (engine.Snapshot().Characters.Single(c => c.Id == injured.ControlledCharacterId).Health != 85) throw new InvalidOperationException("Rest health rate wrong");
            StepTicks(2); Render();
            if (engine.Snapshot().Characters.Single(c => c.Id == injured.ControlledCharacterId) is not { Health: 100, Stamina.Resting: false } || !gameplay.DetailsText.Contains("生命 10／Tick")) throw new InvalidOperationException("Rest health completion or UI missing");
            GD.Print("STAMINA_UI_SMOKE_PASS: scenario, travel cost, player rest while observing NPC, mindset and modifier display, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("STAMINA_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }
    private void CreateSectEconomy()
    {
        try
        {
            var replacement = NewTestWorld(SectEconomyScenario.Create(playerName.Text));
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true); paused = true;
            developer.ResetHistory(); actors.Clear(); foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "已建立宗門經濟／副本測試世界：收購使用明確外部無限測試資金；原存檔未改寫。"; Render();
        }
        catch (Exception error) { notice.Text = "建立測試情境失敗：" + error.Message; }
    }

    private void CreatePerformanceWorld()
    {
        try
        {
            var replacement = NewTestWorld(ScaleScenario.Create());
            engine = replacement; pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; developer.ResetHistory(); actors.Clear();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            notice.Text = "已建立100 NPC效能情境；近／同區／遠方為測試LOD，正式地圖仍待確認。"; Render();
        }
        catch (Exception error) { notice.Text = "建立效能情境失敗：" + error.Message; }
    }

    private void PerformanceSmokeTest()
    {
        try
        {
            CreatePerformanceWorld(); StepTicks(10); Render();
            if (engine.Snapshot().Characters.Count != 101 || engine.LastDiagnostics is null || !performance.Text.Contains("ms") || !performance.Text.Contains("不適用")) throw new InvalidOperationException("Performance display missing measured data or headless FPS distinction");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Performance world save changed logical state");
            StepTicks(1); playerName.Text = "效能新檔"; newSave.EmitSignal(Button.SignalName.Pressed);
            if (engine.Snapshot().Clock.Tick != 0 || engine.Snapshot().ScaleNpcCount != 100) throw new InvalidOperationException("Scale new save failed");
            GD.Print("PERFORMANCE_UI_SMOKE_PASS: 100 NPC, LOD, measured Tick/AI/queue/memory, headless FPS, save/load, reset"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("PERFORMANCE_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void SectEconomySmokeTest()
    {
        try
        {
            playerName.Text = "宗門測試"; CreateSectEconomy();
            demoMap.Travel("dungeon"); StepTicks(1); StepTicks(3);
            SubmitAction(ActionKind.ExploreDungeon, "dungeon", "combat"); StepTicks(1);
            if (!engine.Snapshot().Events.Any(e => e.Kind is "DungeonGathered" or "DungeonGatherFailed" or "DungeonEncounter")) throw new InvalidOperationException("Dungeon UI failed");
            StepTicks(20); Render();
            var w = engine.Snapshot();
            if (!w.Events.Any(e => e.Kind == "PayrollPaid") || !w.Events.Any(e => e.Kind == "ProcurementPriceReviewed") || !w.Missions.Any(m => m.ResourceId == "beast-hide")) throw new InvalidOperationException("Sect automation failed");
            string digest = WorldCodec.Digest(w); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Sect state save failed");
            GD.Print("SECT_ECONOMY_UI_SMOKE_PASS: scenario, dungeon travel/explore, procurement, payroll, tasks, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("SECT_ECONOMY_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void SectFacilitySmokeTest()
    {
        try
        {
            playerName.Text = "設施驗收"; CreateSectEconomy();
            long initialHerbs = Inventory.Balance(engine.Snapshot(), "sect", "herb");
            void Commit() { if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Facility UI command rejected"); }
            void Wait(int ticks) { for (int n = 0; n < ticks; n++) engine.Step(); Render(); }
            demoMap.Travel("sect"); Commit(); Wait(3);
            sectFacilities.Command(ActionKind.SetSectStockReservation, "sect", "sect-stock"); Commit();
            if (!engine.Snapshot().Events.Any(e => e.Kind == "SectStockReservationChanged")) throw new InvalidOperationException("Reserve setting UI failed");
            sectFacilities.Command(ActionKind.AcceptSectWork, "sect", "herb"); Commit();
            demoMap.Travel("grove"); Commit(); Wait(5);
            sectFacilities.Command(ActionKind.SectGather, "grove", "herb"); Commit(); Wait((int)engine.Snapshot().Rules.GatherTicks);
            if (Inventory.Balance(engine.Snapshot(), "player", "contribution") != 4 || Inventory.Balance(engine.Snapshot(), "sect", "herb") != initialHerbs + 1) throw new InvalidOperationException("Work UI ownership or points failed");
            demoMap.Travel("sect"); Commit(); Wait(5);
            sectFacilities.Command(ActionKind.RedeemContribution, "sect", "herb"); Commit();
            if (Inventory.Balance(engine.Snapshot(), "player", "contribution") != 0 || Inventory.Balance(engine.Snapshot(), "player", "herb") != 1) throw new InvalidOperationException("Redemption UI failed");
            sectFacilities.Command(ActionKind.BuyContribution, "sect", "spirit-stone"); Commit();
            if (Inventory.Balance(engine.Snapshot(), "player", "contribution") != 30) throw new InvalidOperationException("Point purchase UI failed");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Facility persistence failed");
            GD.Print("SECT_FACILITY_UI_SMOKE_PASS: acceptance, travel, sect gathering, contribution, redemption, purchase, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("SECT_FACILITY_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void PlayerDemoSmokeTest()
    {
        try
        {
            playerName.Text = "測試修士"; CreatePlayerDemo();
            if (!paused || engine.Snapshot().Characters.Single(c => c.Id == engine.Snapshot().ControlledCharacterId).Name != "測試修士") throw new InvalidOperationException("Player creation failed");
            actors.Select(2); Render();
            if (engine.Snapshot().ControlledCharacterId != "player") throw new InvalidOperationException("Observation changed control");
            string digest = WorldCodec.Digest(engine.Snapshot()); playerName.Text = " "; CreatePlayerDemo();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Invalid name replaced world");
            Save(); Load();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Player persistence failed");
            GD.Print("PLAYER_DEMO_UI_SMOKE_PASS: name, creation, observation authority, invalid input, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("PLAYER_DEMO_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void GameplaySmokeTest()
    {
        try
        {
            CreateSocialWorld(); actors.Select(2); Render();
            long herbs = Inventory.Balance(engine.Snapshot(), "player", "herb");
            gameplay.Trade(ActionKind.Buy);
            if (!engine.Step().Single().Accepted || Inventory.Balance(engine.Snapshot(), "player", "herb") != herbs + 1) throw new InvalidOperationException("Player purchase failed");
            gameplay.Trade(ActionKind.Sell);
            if (!engine.Step().Single().Accepted || Inventory.Balance(engine.Snapshot(), "player", "herb") != herbs) throw new InvalidOperationException("Player sale failed");
            missionPanel.Publish(); if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Publish failed"); Render();
            missionPanel.Respond(ActionKind.AcceptMission); if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Player acceptance failed");
            missionPanel.Respond(ActionKind.DeliverMission); if (!engine.Step().Single().Accepted || engine.Snapshot().Missions.Single().Delivered != 1) throw new InvalidOperationException("Player delivery failed");
            Render();
            if (!gameplay.DetailsText.Contains("玩家成交") || !gameplay.DetailsText.Contains("AI理由") || engine.Snapshot().Trades.Any(t => t.ActorId != "player")) throw new InvalidOperationException("Player view or authority failed");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Gameplay save failed");
            GD.Print("GAMEPLAY_UI_SMOKE_PASS: inventory, player buy/sell, mission acceptance/delivery, observed authority, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("GAMEPLAY_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void WorldEventsSmokeTest()
    {
        try
        {
            CreateSocialWorld(); var w = engine.Snapshot(); w.HerbRegeneration = new(2, 3, 5);
            w.Ledger.RemoveAll(e => e.To == "grove"); engine = new(w);
            for (int n = 0; n < 4; n++) engine.Step();
            Render(); journal.Filter("ResourceRegenerated", "grove");
            if (!journal.DisplayText.StartsWith("符合 2 筆")) throw new InvalidOperationException("Event query failed");
            journal.OnlyPlayerNotifications(true);
            if (!journal.DisplayText.StartsWith("符合 2 筆")) throw new InvalidOperationException("Player notifications missing");
            journal.OnlyPlayerNotifications(false);
            journal.Filter("ResourceRegenerated", "not-present");
            if (!journal.DisplayText.StartsWith("符合 0 筆")) throw new InvalidOperationException("Event filter failed");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("World event persistence failed");
            GD.Print("WORLD_EVENTS_UI_SMOKE_PASS: regeneration, event type/text query, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("WORLD_EVENTS_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void CreateSocialWorld()
    {
        engine = NewTestWorld(SocialScenario.Create()); pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
        paused = true; developer.ResetHistory(); actors.Clear(); foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
        notice.Text = "已建立社交新情境（未覆寫存檔）。在「社交與招募」邀請合作、收徒或招募，再推進時間等待 NPC 回應。"; Render();
    }
    private void SocialSmokeTest()
    {
        try
        {
            CreateSocialWorld(); socialPanel.SelectTarget("npc:04"); socialPanel.Command(ActionKind.InviteCooperation); engine.Step();
            for (int n = 0; n < 10; n++) engine.Step(true);
            socialPanel.Command(ActionKind.InviteRecruit); engine.Step();
            for (int n = 0; n < 20; n++) engine.Step(true);
            Render(); var w = engine.Snapshot();
            if (w.SocialOffers.Count(o => o.Status == SocialOfferStatus.Accepted) != 2 || w.Characters.Single(c => c.Id == "npc:04").OrganizationId != "sect" || !socialPanel.DetailsText.Contains("信任")) throw new InvalidOperationException("Autonomous consent or social UI failed");
            string digest = WorldCodec.Digest(w); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Social UI save/load changed world");
            GD.Print("SOCIAL_UI_SMOKE_PASS: cooperation, voluntary recruitment, relationships, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("SOCIAL_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void CreateCombatWorld()
    {
        engine = NewTestWorld(CombatScenario.Create()); pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
        paused = true; developer.ResetHistory(); actors.Clear();
        foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
        notice.Text = "已建立戰鬥新情境（未覆寫存檔）。在「戰鬥」頁選敵人交戰；啟動時間後 NPC 也會評估戰鬥。";
        Render();
    }

    private void CombatSmokeTest()
    {
        try
        {
            CreateCombatWorld(); combatPanel.Command(ActionKind.Engage); engine.Step();
            combatPanel.Command(ActionKind.PowerStrike); engine.Step(); Render();
            if (!engine.Snapshot().Combats.Single().InitiatorBoost) throw new InvalidOperationException("Combat skill control failed");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Active combat UI save/load changed state");
            engine.Step(); combatPanel.Command(ActionKind.CombatHeal); engine.Step(); Render();
            if (Inventory.Balance(engine.Snapshot(), "player", "pill") != 1 || !combatPanel.DetailsText.Contains("近期戰鬥")) throw new InvalidOperationException("Combat healing or display failed");
            combatPanel.Command(ActionKind.Retreat); engine.Step(); Render();
            if (!engine.Snapshot().Events.Any(e => e.Kind is "Retreated" or "RetreatFailed")) throw new InvalidOperationException("Retreat control failed");
            GD.Print("COMBAT_UI_SMOKE_PASS: engagement, skill, healing, retreat, display, active save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("COMBAT_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void CreateAdvancementWorld()
    {
        engine = NewTestWorld(AdvancementScenario.Create());
        pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
        paused = true; developer.ResetHistory(); actors.Clear();
        foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
        notice.Text = "已建立階段 3 養成新情境（未覆寫存檔），查看開發者「養成與裝備」。此情境未啟用戰鬥或社交。";
        Render();
    }

    private void AdvancementSmokeTest()
    {
        try
        {
            CreateAdvancementWorld();
            SubmitAction(ActionKind.LearnMethod, "player", "spirit-stone"); engine.Step();
            for (int n = 0; n < 3; n++) engine.Step();
            SubmitAction(ActionKind.Breakthrough, "player", "pill"); engine.Step();
            for (int n = 0; n < 4; n++) engine.Step();
            SubmitAction(ActionKind.EquipWeapon, "player", "iron-sword"); engine.Step(); Render();
            var snapshot = engine.Snapshot(); var actor = snapshot.Characters.Single(c => c.Id == "player");
            if (actor.Realm != 1 || actor.MethodId is null || actor.EquippedWeaponId is null || !developer.GrowthText.Contains("境界 1")) throw new InvalidOperationException("Advancement controls or display failed");
            string digest = WorldCodec.Digest(snapshot); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Advancement UI save/load changed state");
            for (int n = 0; n < 60; n++) engine.Step(true);
            if (!engine.Snapshot().Characters.Any(c => c.Id != "player" && c.Realm > 0)) throw new InvalidOperationException("NPC advancement absent");
            GD.Print("GROWTH_UI_SMOKE_PASS: learning, breakthrough, equipment, values, save/load, NPC growth"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("GROWTH_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void CreateMissionWorld()
    {
        engine = NewTestWorld(MissionScenario.Create());
        pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
        paused = true;
        developer.ResetHistory(); actors.Clear();
        foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
        notice.Text = "已建立階段 2 新情境（未覆寫存檔）。請於宗門任務頁設定並發布任務，再推進時間。";
        Render();
    }

    private void MissionSmokeTest()
    {
        try
        {
            CreateMissionWorld(); missionPanel.Publish(); engine.Step(true);
            for (int n = 0; n < 130; n++) engine.Step(true);
            Render();
            var w = engine.Snapshot();
            if (w.Missions.Single().Delivered != 20 || w.Deliveries.Sum(d => d.Payment) != 2400 || !missionPanel.DetailsText.Contains("20/20")) throw new InvalidOperationException("Mission UI did not complete scenario");
            string digest = WorldCodec.Digest(w); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Mission UI save continuation changed world");
            missionPanel.Publish(); engine.Step(); Render(); missionPanel.Cancel(); engine.Step(); Render();
            if (engine.Snapshot().Missions[0].Status != MissionStatus.Completed || engine.Snapshot().Missions[1].Status != MissionStatus.Cancelled) throw new InvalidOperationException("Mission UI cancellation failed");
            GD.Print("PHASE2_UI_SMOKE_PASS: mission publication, autonomous delivery, budget, display, save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("PHASE2_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void SmokeTest()
    {
        try
        {
            for (int n = 0; n < 80; n++) engine.Step(autonomous: true);
            actors.Select(1);
            Render();
            var snapshot = engine.Snapshot();
            if (snapshot.Characters.Count != 41 || !snapshot.Events.Any(e => e.Kind == "Trade") || actorDetails.Text.Length == 0)
                throw new InvalidOperationException("Autonomous UI did not render simulation results");
            Save();
            if (lastSave is null) throw new InvalidOperationException("UI save failed");
            string digest = WorldCodec.Digest(snapshot);
            Load();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("UI save/load changed state");
            if (!paused) throw new InvalidOperationException("Load did not pause");
            SubmitAction(ActionKind.Gather, "grove", "herb");
            engine.Step(true);
            Render();
            if (!engine.Snapshot().Scheduled.Any(t => t.Intent.ActorId == "player")) throw new InvalidOperationException("Player input failed");
            developer.ResetHistory();
            Render();
            for (int n = 0; n < 130; n++) { engine.Step(true); Render(); }
            if (developer.SampleCount != 120 || !developer.WorldText.Contains("藥材報價")) throw new InvalidOperationException("Developer telemetry failed");
            Render();
            if (developer.SampleCount != 120) throw new InvalidOperationException("Duplicate frame sampled twice");
            Save(); Load(); Render();
            if (developer.SampleCount != 1) throw new InvalidOperationException("Loaded timeline retained stale history");
            GD.Print("DEVELOPER_UI_SMOKE_PASS: bounded history, duplicate sample, load reset, world values");
            GD.Print("PHASE1_UI_SMOKE_PASS: 40 NPC, selection, render, save/load, player input");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("PHASE1_UI_SMOKE_FAIL: " + error);
            GetTree().Quit(1);
        }
    }

    private static HFlowContainer AddButtonGroup(VBoxContainer parent, string title)
    {
        var frame = new PanelContainer(); parent.AddChild(frame);
        var content = new VBoxContainer(); frame.AddChild(content);
        content.AddChild(new Label { Text = title });
        var buttons = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(buttons);
        return buttons;
    }

    private static Button AddButton(Container parent, string text, System.Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    public override void _Process(double delta)
    {
        AdvanceSimulation(delta);
        refreshElapsed += delta;
        if (refreshElapsed >= 0.2) { refreshElapsed = 0; Render(); }
    }

    private void AdvanceSimulation(double delta)
    {
        if (engine.WaitsForPlayerAction) { paused = true; return; }
        if (!paused)
        {
            int count = pump.Advance(TimeSpan.FromSeconds(delta * speed), maxSteps: 1);
            if (count > 0) ObserveCharacter();
            for (int n = 1; count > 0 && n < 32; n++)
            {
                count = pump.Advance(TimeSpan.Zero, maxSteps: 1);
                if (count > 0) ObserveCharacter();
            }
        }
    }
    private void TogglePause() { paused = !paused; Render(); }
    private void StepTicks(int count)
    {
        if (!paused) { notice.Text = "請先暫停，再手動前進Tick。"; return; }
        for (int n = 0; n < count; n++) {
            var w = engine.Snapshot();
            if (w.Time?.ActionDriven is not null && w.BoardBattle is not { Phase: BoardBattlePhase.Active }) {
                var result = engine.ExecutePlayerActivity(new("player-wait:" + w.NextSequence, w.ControlledCharacterId, w.ControlledCharacterId,
                    "time", 1, 0, DecisionSource.Player, ActionKind.Wait));
                if (!result.Accepted) { notice.Text = result.Reason; break; }
            } else engine.Step(true);
            ObserveCharacter();
        }
        Render();
    }
    private void TimeSmokeTest()
    {
        try
        {
            playerName.Text = "時間測試"; CreatePlayerDemo();
            long tick = engine.Snapshot().Clock.Tick; AdvanceSimulation(10);
            if (engine.Snapshot().Clock.Tick != tick) throw new InvalidOperationException("Paused world advanced");
            StepTicks(1); StepTicks(10);
            if (engine.Snapshot().Clock.Tick != tick + 11) throw new InvalidOperationException("Manual stepping incorrect");
            TogglePause(); tick = engine.Snapshot().Clock.Tick; StepTicks(1);
            if (engine.Snapshot().Clock.Tick != tick) throw new InvalidOperationException("Running single step allowed");
            for (int index = 0; index < 3; index++)
            {
                rate.Select(index); rate.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                tick = engine.Snapshot().Clock.Tick; AdvanceSimulation(0.25);
                if (engine.Snapshot().Clock.Tick - tick != (1 << index)) throw new InvalidOperationException("Speed scaling incorrect");
            }
            Save(); string digest = WorldCodec.Digest(engine.Snapshot()); Load(); AdvanceSimulation(100);
            if (!paused || WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Load advanced offline time");
            GD.Print("TIME_UI_SMOKE_PASS: pause, manual steps, 1/2/4x, running step rejection, paused load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("TIME_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void EndToEndSmoke(string manifestPath, bool create)
    {
        try
        {
            if (create)
            {
                playerName.Text = "續玩驗收"; CreatePlayerDemo(); actors.Select(2); Render();
                long herbs = Inventory.Balance(engine.Snapshot(), "player", "herb");
                gameplay.Trade(ActionKind.Buy); StepTicks(1);
                if (Inventory.Balance(engine.Snapshot(), "player", "herb") != herbs + 1) throw new InvalidOperationException("E2E purchase failed");
                gameplay.Trade(ActionKind.Sell); StepTicks(1);
                if (Inventory.Balance(engine.Snapshot(), "player", "herb") != herbs) throw new InvalidOperationException("E2E sale failed");
                demoMap.Travel("grove"); StepTicks(1);
                if (!engine.Snapshot().Scheduled.Any(s => s.Intent.ActorId == "player" && s.Intent.Kind == ActionKind.Travel)) throw new InvalidOperationException("E2E travel failed");
                Save();
                if (lastSave is null) throw new InvalidOperationException("E2E save failed");
                var expected = new SimulationEngine(engine.Snapshot());
                for (int n = 0; n < 4; n++) expected.Step(true);
                var manifest = new Dictionary<string, string>
                {
                    ["save"] = lastSave, ["digest"] = WorldCodec.Digest(engine.Snapshot()),
                    ["continued"] = WorldCodec.Digest(expected.Snapshot())
                };
                using var file = new System.IO.FileStream(manifestPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write);
                System.Text.Json.JsonSerializer.Serialize(file, manifest);
                GD.Print("E2E_CREATE_PASS: create, observe, trade, travel, save, exit");
            }
            else
            {
                var manifest = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(manifestPath))!;
                savePicker.EmitSignal(FileDialog.SignalName.FileSelected, manifest["save"]);
                if (!paused || lastSave != manifest["save"] || WorldCodec.Digest(engine.Snapshot()) != manifest["digest"]) throw new InvalidOperationException("Cross-process load changed state");
                AdvanceSimulation(86400);
                if (WorldCodec.Digest(engine.Snapshot()) != manifest["digest"]) throw new InvalidOperationException("Offline time advanced");
                StepTicks(4);
                if (WorldCodec.Digest(engine.Snapshot()) != manifest["continued"] || engine.Snapshot().Characters.Single(c => c.Id == "player").DemoLocationId != "grove") throw new InvalidOperationException("Cross-process continuation diverged");
                GD.Print("E2E_RESUME_PASS: independent restart, file selection, no offline progress, identical continuation");
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("E2E_FAIL: " + error); GetTree().Quit(1); }
    }

    private void SubmitPlayer()
    {
        // Sequence is persisted, so IDs remain unique after reloading a save.
        var snapshot = engine.Snapshot();
        SubmitTracked(new("ui:" + snapshot.NextSequence, snapshot.ControlledCharacterId, "sect",
            "spirit-stone", 10, 2, DecisionSource.Player));
    }

    private void SubmitAction(ActionKind kind, string target, string resource)
    {
        var w = engine.Snapshot();
        SubmitTracked(new("ui:" + w.NextSequence, w.ControlledCharacterId, target, resource, 1, 0, DecisionSource.Player, kind));
    }

    private void Save()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath("user://saves/world-" + Guid.NewGuid().ToString("N") + ".json");
            new JsonWorldStore().Save(path, engine.Snapshot());
            lastSave = path;
            notice.Text = "已儲存：" + path;
        }
        catch (Exception error) { notice.Text = "存檔失敗：" + error.Message; }
    }

    private void StartNewSave(string? saveDirectory = null)
    {
        try
        {
            var replacement = NewTestWorld(NewGameFactory.CreateLike(engine.Snapshot(), playerName.Text));
            string directory = saveDirectory ?? ProjectSettings.GlobalizePath("user://saves");
            string path = System.IO.Path.Combine(directory, "world-" + Guid.NewGuid().ToString("N") + ".json");
            new JsonWorldStore().Save(path, replacement.Snapshot());
            engine = replacement;
            pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            paused = true; speed = 1; rate.Select(0); refreshElapsed = 0;
            lastSave = path; developer.ResetHistory(); actors.Clear();
            missionPanel.ResetInputs(); sectFacilities.ResetInputs();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            actors.Select(0);
            notice.Text = "已開新存檔並重設初始值（Tick 0、已暫停）：" + path;
            Render();
        }
        catch (Exception error) { notice.Text = "開新存檔失敗，保留目前世界：" + error.Message; }
    }

    private void NewSaveSmokeTest()
    {
        try
        {
            playerName.Text = "新檔驗收"; CreateSectEconomy();
            for (int n = 0; n < 35; n++) engine.Step(true);
            Save(); string oldPath = lastSave ?? throw new InvalidOperationException("Old save missing");
            string oldContents = System.IO.File.ReadAllText(oldPath);
            SubmitTracked(new("discard-pending", "player", "sect", "travel", 1, 0, DecisionSource.Player, ActionKind.Travel));
            paused = false; speed = 4; rate.Select(2); actors.Select(3);
            newSave.EmitSignal(Button.SignalName.Pressed);
            string path = lastSave ?? throw new InvalidOperationException("New save missing");
            var fresh = engine.Snapshot();
            if (path == oldPath || !System.IO.File.Exists(path) || !paused || speed != 1 || actors.Selected != 0 ||
                WorldCodec.Digest(fresh) != WorldCodec.Digest(SectEconomyScenario.Create("新檔驗收")))
                throw new InvalidOperationException("New save did not reset complete world or controls");
            if (System.IO.File.ReadAllText(oldPath) != oldContents) throw new InvalidOperationException("Old save modified");
            if (WorldCodec.Digest(new JsonWorldStore().Load(path)) != WorldCodec.Digest(fresh)) throw new InvalidOperationException("Initial save not persisted");
            engine.Step(true); string digest = WorldCodec.Digest(engine.Snapshot());
            playerName.Text = " "; newSave.EmitSignal(Button.SignalName.Pressed);
            if (lastSave != path || WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Invalid name changed world");
            playerName.Text = "新檔驗收"; StartNewSave(oldPath);
            if (lastSave != path || WorldCodec.Digest(engine.Snapshot()) != digest || System.IO.File.ReadAllText(oldPath) != oldContents)
                throw new InvalidOperationException("Failed write changed world or old file");
            Load();
            if (WorldCodec.Digest(engine.Snapshot()) != WorldCodec.Digest(fresh)) throw new InvalidOperationException("New save cannot reload");
            GD.Print("NEW_SAVE_UI_SMOKE_PASS: full reset, unique persisted file, old save preserved, pause, invalid input/write isolation, reload");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("NEW_SAVE_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void ExpeditionSmokeTest()
    {
        try
        {
            playerName.Text = "組隊驗收"; CreateSectEconomy(); engine.Step();
            var w = engine.Snapshot();
            w.Characters = w.Characters.Select(c => c.Id is "player" or "npc:10" ? c with { DemoLocationId = "sect" } : c).ToList();
            engine = new(w); Render();
            expeditions.SelectContact("npc:10"); expeditions.Command(ActionKind.InviteParty);
            if (!engine.Step().Single().Accepted || engine.Snapshot().Parties.Count != 0) throw new InvalidOperationException("Invitation forced membership");
            var invitation = engine.Snapshot().PartyInvitations.Single();
            SubmitTracked(new("smoke-consent", "npc:10", invitation.Id, "party", 1, 0, DecisionSource.Npc, ActionKind.AcceptParty));
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Consent failed");
            Render(); expeditions.Command(ActionKind.AcceptHunt);
            if (!engine.Step().Single().Accepted) throw new InvalidOperationException("Hunt UI failed");
            w = engine.Snapshot(); w.Dungeon = new(1, 1);
            w.Characters = w.Characters.Select(c => c.Id is "player" or "npc:10" ? c with { DemoLocationId = "dungeon", Health = 100, BaseAttack = 20, Realm = 0, MethodId = null, MethodExperience = 0 } : c).ToList();
            engine = new(w); SubmitAction(ActionKind.ExploreDungeon, "dungeon", "combat");
            if (!engine.Step().Single().Accepted || engine.Snapshot().Combats.Single().Allies?.Count != 1) throw new InvalidOperationException("Party battle not started");
            string digest = WorldCodec.Digest(engine.Snapshot()); Save(); Load(); Render();
            if (WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Expedition save lost state");
            for (int n = 0; n < 20 && engine.Snapshot().Combats.Any(b => b.Status == CombatStatus.Active); n++) engine.Step();
            Render(); w = engine.Snapshot();
            if (w.Hunts.Sum(h => h.Kills) != 1 || w.HuntReceipts.Sum(r => r.Contribution) != 10 || w.PartyLoot.Sum(l => l.Quantity) != 1) throw new InvalidOperationException("Party hunt reward or loot failed");
            GD.Print("EXPEDITION_UI_SMOKE_PASS: invitation, consent, hunt, team combat, shared rewards and loot, mid-combat save/load"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("EXPEDITION_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
    }

    private void Load()
    {
        if (lastSave is null) { notice.Text = "請先另存新檔。"; return; }
        LoadFromPath(lastSave);
    }

    private void LoadFromPath(string path)
    {
        try
        {
            var restored = new SimulationEngine(new JsonWorldStore().Load(path));
            engine = restored;
            pump = new(engine, TimeSpan.FromMilliseconds(250), autonomous: true);
            actors.Clear();
            developer.ResetHistory();
            foreach (var actor in engine.Snapshot().Characters) actors.AddItem(actor.Name);
            paused = true;
            lastSave = path;
            notice.Text = "已載入並暫停：" + path;
        }
        catch (Exception error) { notice.Text = "讀檔失敗：" + error.Message; }
    }

    private void SavePickerSmokeTest()
    {
        string? invalid = null;
        try
        {
            CreateSocialWorld();
            engine.Step(true); Save();
            string path = lastSave ?? throw new InvalidOperationException("Save failed");
            string digest = WorldCodec.Digest(engine.Snapshot());
            engine.Step(true); lastSave = null; paused = false;
            savePicker.EmitSignal(FileDialog.SignalName.FileSelected, path);
            if (!paused || lastSave != path || WorldCodec.Digest(engine.Snapshot()) != digest) throw new InvalidOperationException("Selected save did not restore and pause");
            var resumed = new SimulationEngine(engine.Snapshot());
            engine.Step(true); resumed.Step(true);
            if (WorldCodec.Digest(engine.Snapshot()) != WorldCodec.Digest(resumed.Snapshot())) throw new InvalidOperationException("Loaded continuation diverged");
            digest = WorldCodec.Digest(engine.Snapshot()); paused = false;
            invalid = path + ".invalid.json";
            System.IO.File.WriteAllText(invalid, "{invalid");
            savePicker.EmitSignal(FileDialog.SignalName.FileSelected, invalid);
            if (WorldCodec.Digest(engine.Snapshot()) != digest || paused || lastSave != path || !notice.Text.StartsWith("讀檔失敗：")) throw new InvalidOperationException("Failed load changed active session");
            var future = engine.Snapshot(); future.SchemaVersion = 99;
            System.IO.File.WriteAllText(invalid, WorldCodec.Serialize(future));
            savePicker.EmitSignal(FileDialog.SignalName.FileSelected, invalid);
            if (WorldCodec.Digest(engine.Snapshot()) != digest || paused || lastSave != path || !notice.Text.Contains("不支援存檔版本")) throw new InvalidOperationException("Unsupported version replaced active session");
            GD.Print("SAVE_PICKER_UI_SMOKE_PASS: selection, previous session path, pause, continuation, invalid save isolation");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("SAVE_PICKER_UI_SMOKE_FAIL: " + error); GetTree().Quit(1); }
        finally { if (invalid is not null && System.IO.File.Exists(invalid)) System.IO.File.Delete(invalid); }
    }

    private void ObserveCharacter()
    {
        var world = engine.Snapshot();
        characterJournal.Observe(world, engine);
        characterAttributes.Refresh(world, engine);
    }
    private void SubmitTracked(ActionIntent intent)
    {
        ObserveCharacter(); var world = engine.Snapshot(); characterJournal.Track(intent, world.ControlledCharacterId);
        if (world.Time?.ActionDriven is not null) {
            try { var result = engine.ExecutePlayerActivity(intent); paused = true; notice.Text = result.Accepted ? "指令已執行，請查看結果並下達下一個指令。" : result.Reason; ObserveCharacter(); }
            catch (Exception e) { paused = true; notice.Text = "活動未執行：" + e.Message; }
        } else engine.Submit(intent);
    }
    private void Render()
    {
        ObserveCharacter();
        demoMap.QueueRedraw();
        journal.Refresh();
        var snapshot = engine.Snapshot();
        autonomousPlayerToggle?.SetPressedNoSignal(snapshot.PlayerAutoSchedule);
        waitOneButton.Text = "等待" + GameTimeDisplay.Duration(snapshot, 1);
        waitTenButton.Text = "等待" + GameTimeDisplay.Duration(snapshot, 10);
        if (autonomousPlayerToggle is not null) autonomousPlayerToggle.Disabled = !engine.ActionDrivenTimeEnabled;
        foreach(var button in pillActions) { button.Disabled = snapshot.MedicineOnly; button.TooltipText = snapshot.MedicineOnly ? "移除丹藥期間，原耗丹行動暫停" : ""; }
        lifeSkills.Refresh(snapshot);
        paidServices.Refresh(snapshot);
        mortalCultivation.Refresh(snapshot);
        settlements.Refresh(snapshot,engine);
        externalMarket.Refresh(snapshot,engine);
        organizationLabor.Refresh(snapshot);
        if(settlementWindow.Visible)settlementWindow.Refresh(snapshot,engine);
        sectEconomy.Refresh(snapshot);
        sectScene.Refresh(snapshot);
        hexMap.Refresh(snapshot);
        siteScene.Refresh(snapshot);
        sectFacilities.Refresh(snapshot);
        expeditions.Refresh(snapshot);
        performance.Refresh(snapshot, engine.LastDiagnostics);
        if (actors.ItemCount != snapshot.Characters.Count)
        {
            int selected = actors.Selected; actors.Clear(); foreach (var character in snapshot.Characters) actors.AddItem(character.Name);
            actors.Select(Math.Clamp(selected, 0, snapshot.Characters.Count - 1));
        }
        gameplay.Refresh(snapshot, snapshot.Characters[Math.Clamp(actors.Selected, 0, snapshot.Characters.Count - 1)].Id);
        developer.Refresh(snapshot, snapshot.Characters[Math.Clamp(actors.Selected, 0, snapshot.Characters.Count - 1)].Id);
        missionPanel.Refresh(snapshot);
        combatPanel.Refresh(snapshot);
        boardBattlePanel.Refresh(snapshot);
        socialPanel.Refresh(snapshot);
        governancePanel.Refresh(snapshot);
        status.Text = $"Tick {snapshot.Clock.Tick}｜{(paused ? "暫停" : "運作中")}｜{speed}倍速（1倍每秒4 Tick）｜待提交 {snapshot.Pending.Count}｜排程 {snapshot.Scheduled.Count}\n" +
            $"NPC {snapshot.Characters.Count - 1}｜交易 {snapshot.Events.Count(e => e.Kind == "Trade")}｜煉丹成功 {snapshot.Events.Count(e => e.Kind == "BrewSucceeded")}｜失敗 {snapshot.Events.Count(e => e.Kind == "BrewFailed")}" +
            $"　青雲宗: {Inventory.Balance(snapshot, "sect", "spirit-stone")}";
        if (snapshot.MedicineOnly) status.Text = "藥物測試：煉丹及原耗丹行動已暫停，丹藥已移除。\n" + status.Text;
        var actor = snapshot.Characters[Math.Clamp(actors.Selected, 0, snapshot.Characters.Count - 1)];
        if (snapshot.Time is { } time)
        {
            var date = CalendarCalculator.Date(snapshot.Clock.Tick, time.Rules);
            var ap = time.Budgets.Single(b => b.ActorId == actor.Id);
            bool battlePaused = snapshot.BoardBattle is { Phase: BoardBattlePhase.Active } battle && battle.Fighters.Any(f => f.ActorId == snapshot.ControlledCharacterId);
            string clock = time.ActionDriven?.MinutesPerActionPoint is { } minutes ?
                $"{date.TickOfDay * minutes / 60:00}:{date.TickOfDay * minutes % 60:00}" : $"日內 {date.TickOfDay}/{time.Rules.TicksPerDay}Tick";
            status.Text = $"第{date.Year}年 {date.Season}季{date.Day}日 {clock}｜{actor.Name} AP {ap.Remaining}/{time.Rules.DailyActionPoints}｜忙碌剩餘 {GameTimeDisplay.Duration(snapshot, Math.Max(0, ap.BusyThroughTick - snapshot.Clock.Tick))}\n" +
                (battlePaused ? $"玩家戰鬥：世界時間凍結，戰後補{GameTimeDisplay.Duration(snapshot, time.Rules.BattleActionPoints)}\n" : "") + status.Text;
        }
        var needs = CharacterDevelopment.Needs(actor, Inventory.Balance(snapshot, actor.Id, "spirit-stone"), Inventory.Balance(snapshot, actor.Id, "herb"), Inventory.Balance(snapshot, actor.Id, "pill"), snapshot.Rules);
        var plan = snapshot.Plans.SingleOrDefault(p => p.ActorId == actor.Id);
        actorDetails.Text = $"{actor.Name}｜志向 {snapshot.Aspirations.SingleOrDefault(a => a.Id == actor.Aspiration)?.Name ?? actor.Aspiration.ToString()}｜體力 {actor.Stamina?.Current.ToString() ?? "未啟用"}{(actor.Stamina?.Unconscious == true ? " 昏厥" : actor.Stamina?.Resting == true ? " 休息中" : snapshot.StaminaRules is { } sr && StaminaCalculator.Fatigued(actor, sr) ? " 疲勞" : "")}｜生命 {actor.Health}｜修為 {actor.Cultivation}｜煉丹熟練 {actor.AlchemyExperience}\n" +
            $"錢財 {Inventory.Balance(snapshot, actor.Id, "spirit-stone")}｜藥材 {Inventory.Balance(snapshot, actor.Id, "herb")}｜丹藥 {Inventory.Balance(snapshot, actor.Id, "pill")}｜財富需求 {needs.Wealth:F2}｜成長需求 {needs.Growth:F2}\n" +
            $"服務：{string.Join("、", CharacterDevelopment.Services(actor))}｜治療 {needs.Healing:F2}｜安全 {needs.Safety:F2}｜聲望 {actor.Reputation}\n" +
            $"計畫：{(plan is null ? "重新評估／無計畫" : plan.Goal + " → " + string.Join(" → ", plan.Steps))}";
        var decision = snapshot.Events.LastOrDefault(e => e.Kind == "Decision" && (e.ActionId.StartsWith("ai:" + actor.Id + ":", StringComparison.Ordinal) || e.ActionId.StartsWith("player-auto:" + actor.Id + ":", StringComparison.Ordinal)));
        log.Text = "角色決策\n" + (decision?.Detail ?? "尚無決策") + "\n\n事件\n" + string.Join("\n", snapshot.Events.TakeLast(8).Select(e => $"[{e.Tick}] {e.ActionId}: {e.Kind} {e.Detail}")) +
            "\n\n帳本（含初始來源與保留帳戶）\n" + string.Join("\n", snapshot.Ledger.TakeLast(12).Select(e => $"[{e.Tick}] {e.From} → {e.To}: {e.Quantity} {e.ResourceId} ({e.Reason})"));
    }
}
















