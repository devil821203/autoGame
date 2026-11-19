# 階段3 MVP 範圍核對與結案驗收

日期：2026-10-17。結論：階段3原定MVP範圍已完成；不代表完整遊戲、所有未定設計或發行驗收完成。

依據為 `修仙遊戲_開發需求規格.md` §3.2、3.6–3.8、§7階段3，以及 `prompt/AI_開發_Prompt.md` 的階段3。使用者要求停止擴張並完成本階段，故本次固定以下驗收範圍。沒有新增規則或刪除既有延伸程式。

## 原定工作與實作證據

| 項目 | 本階段完成條件 | 實作與本次通過的驗收 |
|---|---|---|
| 3-01 角色 | 身分、組織、職位、生命／狀態、裝備與攻防可運作 | Domain/Models、CharacterProgression、AdvancementActions；非法歸屬／裝備拒載、裝備須持有且卸下後才可轉移；養成UI驗收 |
| 3-02 養成 | 學功法、熟練成長、境界突破有成本與時間，NPC可自行成長 | AdvancementActions、CharacterProgression；學習保留成本、突破條件／丹藥消耗、技能影響成長、排程存讀與自主養成 |
| 3-03 戰鬥 | 固定Tick自動回合、傷害／受傷、技能／丹藥與有限戰利品 | CombatActions；回合、強擊、治療、撤退、勝負／平局、戰利品不重發及戰中存讀；戰鬥UI驗收 |
| 3-04 戰鬥決策 | NPC依可見敵人、傷勢、風險與資源選戰鬥／撤退／治療／準備 | AI/CombatObservation、UtilityPlanner；視野來源、風險、撤退與備丹評分，實際自主戰鬥及重現 |
| 3-05 社交 | 稀疏關係、好感／信任／仇恨／聲望、師徒與合作，關係影響行動 | SocialModels、SocialActions、SocialObservation；邀請須同意、援助移轉真實物品、付費傳授、關係改變任務效用、衝突形成仇恨、履約／成交聲望、聲望影響入門。交易與傳授的關係回饋以現有授課報價涵蓋；社交UI驗收 |
| 3-06 組織 | 通用組織有資產／成員／職位、招募、政策與獎懲 | OrganizationState、GovernanceActions、SocialActions；自主招募／同意、入退資格、任免／逐出、帳本獎勵、名額與資金保護。宗門／國家／門派以同一通用模型承載，本階段未要求三套不同官制。已有繼任擴充保留；治理UI驗收 |
| 3-07 間接控制 | 領導者設定商品需求、報酬、期限與資格，NPC自行決策 | MissionActions、GovernanceActions、AutonomousDecisions；任務發布／預算保留／分批驗收／支付／取消／到期，成員資格及入門政策，自主接單與供貨；玩家不能替NPC提交玩家指令 |

原規格「資源與關係能影響成長及決策」以付費學習／突破／丹藥、關係改變任務效用與援助／傳授、聲望入門門檻及NPC自主選擇對應。核心測試同時驗證玩家／NPC共用規則、守恆、去重、可重現與存讀續跑。

## 本次實際執行

```powershell
dotnet build CultivationGame.sln --no-restore
dotnet run --project tests/Acceptance --no-restore
dotnet build src/Presentation/CultivationGame.csproj --no-restore
$enginePath = '.tools/godot43/Godot_v4.3-stable_mono_win64/Godot_v4.3-stable_mono_win64_console.exe'
foreach ($check in @('--growth-smoke', '--combat-smoke', '--social-smoke', '--governance-smoke')) {
    & $enginePath --headless --path src/Presentation -- $check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

結果：核心與Godot建置皆0警告／0錯誤；Acceptance **92/92 passed**、退出碼0。此92項包含先前階段回歸及既有延伸，不以測試數量本身作為階段完成依據。

四組Godot皆退出碼0：

- `GROWTH_UI_SMOKE_PASS`：學習、突破、裝備、數值、存讀、NPC成長。
- `COMBAT_UI_SMOKE_PASS`：交戰、技能、治療、撤退、顯示、戰中存讀。
- `SOCIAL_UI_SMOKE_PASS`：合作、自願招募、關係、存讀。
- `GOVERNANCE_UI_SMOKE_PASS`：政策、任命、獎勵、繼任提名、存讀。

## 範圍控制與保留限制

本次不新增功能；完成工作是依原規格核對、重驗及修正進度文件。已有信任折扣、繼任、成員任務入門收益等延伸保留，但不再以持續深化它們作為階段3必要條件。

採集供貨的入門收益估算、區域聲望傳播、一般商品社交議價、更複雜組織政策／官制、外交／叛逃／勢力戰爭皆非本次結案門檻；後續需另行限定需求，不能自動加入階段3待辦。沒有以這些項目已實作作為結案前提。

本階段使用單功法／有限境界、簡易自動戰鬥與非永久死亡、通用組織／政策，以及既有有限市場。最終數值與戰鬥操作仍屬未定設計；沒有宣稱已完成最終版本。人工視覺、效能、長時間經濟與社交平衡均未驗證。

階段4的2D地圖、旅行、角色建立／操作流程及完整遊玩介面，階段5的AI LOD與規模壓測，依原進度保留。下一步轉入階段4前先限定其工作與未定事項。

先前養成、戰鬥、社交與治理文件保留當時狀態與失敗紀錄；其中「階段3仍測試中」「完整…待補」為歷史批次紀錄，目前狀態以本結案紀錄與唯一進度總表為準。
