# 遊戲參數設定

這裡是新建世界使用的分類JSON。修改數值並儲存後，重新建立對應情境即可生效；正在運作的世界與舊存檔沿用建立時保存的設定。程式執行中不逐Tick讀檔，也不自動重套設定。

## 分類索引

| 分類 | 檔案 | 用途 |
|---|---|---|
| 獨立場景 | [scenes/sites.json](scenes/sites.json) | 六類場景ID、世界入口、主建築名稱／展示等級及建築格解鎖等級；首版每場景兩個可用／兩個鎖定格為展示配置，不是最終容量平衡 |
| 六角地圖 | [map/layout.json](map/layout.json) | 固定測試地圖大小、自然特徵、聚落／五據點與單層組織初始領地 |
| 六角旅行 | [map/movement.json](map/movement.json) | 平原／森林／沙漠每格Tick；山脈與湖泊首版不通行 |
| 六角組織 | [organizations/hex-world.json](organizations/hex-world.json) | 宗門／幫派／山寨三類組織與測試宗主；不修改舊世界 |
| 六角角色 | [characters/hex-world.json](characters/hex-world.json) | 新六角情境玩家宗主身分，便於建造測試 |
| 建築 | [buildings/definitions.json](buildings/definitions.json) | 宗門建築錢財成本、抽象工期與接入既有服務的ID |
| 雙陣地 | [combat/board.json](combat/board.json)、[combat/formation.json](combat/formation.json)、[characters/board-battle.json](characters/board-battle.json) | 階段7獨立測試的最大輪數、編制／速度／武器射程與角色攻防；不替換舊副本 |
| 心境 | [characters/mindset.json](characters/mindset.json) | 階段6新養成情境：0～100、初始50、0.5＋心境×0.01，與疲勞相乘；修改後重建世界生效，舊檔保留原模式 |
| 體力 | [characters/stamina.json](characters/stamina.json) | 階段6養成測試：上限、疲勞門檻／倍率、活動成本、各環境與昏厥回復，以及HealthRecoveryPerTick＝10的非戰鬥休息回血；舊檔缺回血欄位保留0 |
| 角色 | [characters/aspirations.json](characters/aspirations.json) | 五種志向的名稱與效用倍率 |
| 角色 | [characters/autonomous.json](characters/autonomous.json) | 玩家及50個NPC範本：志向、煉丹天賦／成功率、性格權重、能力、需求；30～50人世界取前N人 |
| 角色 | [characters/mission.json](characters/mission.json) | 回春丹任務情境的角色覆寫 |
| 角色 | [characters/advancement.json](characters/advancement.json) | 養成情境的角色覆寫 |
| 角色 | [characters/combat.json](characters/combat.json) | 戰鬥情境的角色覆寫／山賊；npc:00使用戰鬥志向 |
| 角色 | [characters/social.json](characters/social.json) | 社交情境的角色覆寫 |
| 角色 | [characters/demo.json](characters/demo.json) | 最初兩角色的帳本測試情境，不是階段4地圖Demo |
| 道具 | [items/catalog.json](items/catalog.json) | 既有道具ID、名稱與分類；遊玩庫存／交易選單使用名稱 |
| 生產／經濟 | [economy/production.json](economy/production.json) | 基礎價格、藥草配方用量、生產／修煉時間、療傷、熟練與決策排程參數 |
| 生產／經濟 | [economy/mission-production.json](economy/mission-production.json) | 任務及其後續情境的配方／價格／採藥時間覆寫 |
| 市場 | [economy/market.json](economy/market.json) | 商店基價、目標庫存、上下限與價差 |
| 庫存 | `economy/autonomous-stock.json`、`mission-stock.json` | 自主／任務世界的初始錢財及物品 |
| 庫存 | `economy/advancement-stock.json`、`combat-stock.json` | 養成／戰鬥情境額外發放的物品 |
| 庫存 | [economy/demo-start.json](economy/demo-start.json) | 最初帳本Demo的隨機初始資金 |
| 收購 | [economy/procurement.json](economy/procurement.json) | 測試需求、調價週期、初價與漲跌幅 |
| 養成 | [cultivation/advancement.json](cultivation/advancement.json) | 功法、境界、突破、武器能力與熟練規則 |
| 戰鬥 | [combat/combat.json](combat/combat.json) | 回合、傷害浮動、強擊、撤退、戰利品與經驗規則 |
| 副本 | [combat/dungeon.json](combat/dungeon.json) | 怪物／採藥分流、狼、山匪掉落與採藥成功機率 |
| 宗門 | [organizations/facilities.json](organizations/facilities.json) | 煉丹／採藥貢獻報酬、兌換價格及丹草保留量 |
| 宗門 | [organizations/payroll.json](organizations/payroll.json) | 發薪週期與每人俸祿 |
| 宗門 | [organizations/duty.json](organizations/duty.json) | 任務發布、數量、報酬、考核週期與逐出門檻 |
| 宗門 | [organizations/test-policy.json](organizations/test-policy.json) | 測試情境的招募與任務資格政策 |
| 社交 | [organizations/social.json](organizations/social.json) | 師徒／合作／聲望／社交期限與授課參數 |
| 任務 | [organizations/representative-mission.json](organizations/representative-mission.json) | 回春丹示範任務數量、單價、天數與預算；亦提供發布介面初值 |
| 世界 | [world/demo.json](world/demo.json) | 每Tick世界秒數與Demo旅行Tick，非畫面速度或正式地圖設計 |
| 世界 | [world/regeneration.json](world/regeneration.json) | 藥園再生週期、數量與容量 |
| 世界 | [world/organizations.json](world/organizations.json) | 基礎組織資料 |

## 志向與天賦怎麼改

角色的`Aspiration`可填`Balanced`（均衡）、`Wealth`（求財）、`Alchemy`（丹道）、`Cultivation`（修煉）、`Combat`（戰鬥）。種類仍是程式列舉，直接增加陌生ID不會新增玩法。

`aspirations.json`各欄位：

| 欄位 | 效果 |
|---|---|
| `WealthMultiplier` | 原有一般經濟決策的財富效益倍率 |
| `GrowthMultiplier` | 原有修煉等決策的成長效益倍率，不是直接發放經驗 |
| `AlchemyExperienceMultiplier` | 一般煉丹決策中天賦／熟練效益的係數 |
| `CombatMultiplier` | 交戰收益與強擊評分，以及副本山匪掉落預期收益倍率；戰鬥志向初值1.5 |

戰鬥倍率不直接增加攻擊力、不改命中或掉落機率、不移除風險與資格檢查，也不提高純採藥事件收益。既有宗門工作評分沿用自身權重，未擴充套用其他志向。`CombatDrive`是角色自身戰鬥偏好，可與戰鬥志向共同調整。

角色天賦`AlchemyTalent`與基礎煉丹成功率`AlchemyChance`分開；實際成功率仍依原有熟練與規則計算。沒有新增採藥／戰鬥天賦種類。

例如在對應角色檔的物件加入或修改：

```json
{ "Id": "npc:00", "Aspiration": "Combat", "CombatDrive": 5 }
```

## 情境覆寫順序

`autonomous` → `mission` → `advancement` → `combat` → `social` → 玩家地圖Demo → 宗門經濟測試。

後續角色檔只列變動欄位，未列者繼承前一層。例如要修改宗門測試中的npc:00，須檢查`combat.json`及`social.json`是否會再次覆寫同一欄位。任務情境會用`mission-stock.json`取代基礎庫存，養成／戰鬥庫存檔則追加；每筆仍透過創世帳本建立，可稽核。

## 使用與限制

- UTF-8、標準JSON，欄位區分大小寫，不加註解或尾端逗號。錯誤欄位、未知列舉、缺檔與非法數值會拒絕建立世界，不會靜默忽略。
- 現有道具／角色／組織／功法ID是引用鍵，不要任意改名；道具展示名稱可以改。新增道具種類或配方仍須對應程式規則，本次沒有實作通用道具／多配方系統。
- 2026-10-17依使用者指示調整NPC志向配額：求財／丹道／修煉／戰鬥各3，其他NPC均衡；其他數值與志向倍率維持原值。舊存檔沒有分類資料時仍採原有相容預設；不自動替舊NPC換志向。
- 在此儲存庫啟動時讀根目錄`config/`；建置亦會將JSON複製至輸出目錄的`config/`。獨立執行時須將此目錄與程式一起帶走。Godot正式匯出封裝尚未驗證。
- 存檔保存設定快照；改JSON後需建立新世界才會採用。沒有加入熱更新、存檔改值工具或新玩法。
- C#的類別／列舉、行動演算法、未資料化的內部判斷常數仍在程式中；類別中的建構預設保留給舊存檔及直接建立模型使用。

驗收與實作紀錄：[參數設定分類與戰鬥志向驗收](../document/參數設定分類與戰鬥志向驗收.md)。

新增操作：儲存JSON後，在遊戲按「開新存檔（重設初始值）」會重新讀設定，重建目前類型的情境並自動新增初始存檔；舊存檔保留，未儲存進度會重設。

## 目前NPC志向測試配額（2026-10-17）

玩家與敵對山賊／副本怪物不列入配額。30／40／50位NPC情境的非均衡志向各3，其餘分別18／28／38位均衡；玩家維持均衡。

| 志向 | NPC ID |
|---|---|
| 戰鬥 | npc:00、npc:10、npc:11 |
| 丹道 | npc:01、npc:04、npc:07 |
| 修煉 | npc:02、npc:05、npc:08 |
| 求財 | npc:03、npc:06、npc:09 |
| 均衡 | npc:12起的其餘NPC |

統一設定於characters/autonomous.json；mission／advancement／combat／social已移除志向覆寫，沿用基礎分布。最初僅兩角色的帳本Demo不具此NPC數量，維持原設定。角色天賦、成功率、身分與其他權重不變，志向倍率也未變動。儲存後按「開新存檔（重設初始值）」採用此配置，載入舊檔不會改變既有人物志向。

驗收：128/128；覆蓋九種人數／情境、存讀及開新存檔後的分布，PHASE2／SECT_ECONOMY／NEW_SAVE UI通過，建置0警告錯誤。長期平衡與人工視覺未驗證。
NPC命名已依志向統一：戰鬥者01～03、丹道者01～03、修煉者01～03、求財者01～03；其餘均衡者01起，採各類獨立編號。人物ID與能力不變，mission不再覆寫姓名；玩家與敵對怪物名稱保持原設定。開新存檔後生效。

## 最新：清剿、組隊及經濟測試設定（2026-10-17）

| 檔案 | 用途與本輪值 |
|---|---|
| combat/party.json | 同門同地點、最多3人、邀請20Tick、出發最低50血 |
| organizations/hunts.json | 狼／山匪各5隻、每隻10點、120Tick一期；組隊共享進度均分 |
| characters/sect-economy.json | 宗門測試最後一層角色覆寫；12位NPC成員、工作／戰鬥偏好與目標修為60；不覆寫姓名／志向配額 |
| economy/sect-stock.json | 額外初始藥草40／丹藥15，走創世帳本 |
| economy/sect-production.json | 僅宗門測試套用藥價4／丹價120、採藥2Tick、成長效用倍率60、自用丹保留；成長倍率影響決策，不直接增修為 |
| economy/sect-market.json | 宗門測試商店；藥價2～8、丹價80～180 |
| organizations/facilities.json | 煉丹報酬本輪30點、採藥4點；兌換丹120／草4，保留丹草各10 |
| organizations/payroll.json | 本輪每10Tick發10錢財，替代原30 |
| organizations/duty.json | 每20Tick檢查物品任務、各5份、期限120；考核120Tick，連續3期未成功逐出 |

前文原批次數值為歷史值，以目前JSON及此節為準。舊存檔不變，按「開新存檔（重設初始值）」套用。完整實測見[清剿組隊經濟驗收](../document/宗門清剿_組隊與經濟運轉驗收.md)。134/134及三種子各400Tick通過，不代表長期封閉經濟平衡。
## 階段5效能設定

world/performance.json：附近／同區／遠方AI更新間隔1／4／16Tick、遠方單人戰鬥批次16Tick、診斷保留120Tick與候選詳列開關。只在階段5情境或明確啟用LOD的長測套用，設定與每角色更新游標可存讀。原有遊玩情境的更新頻率不強制改動；量測與UTF8複製／稽核索引改善共用。新效能情境額外NPC皆均衡，四類特殊志向仍各3。使用方式見[階段5驗收](../document/階段5_效能LOD與長期驗收.md)。





時間測試設定：time/calendar.json 與 time/action-costs.json。新建「時間與AP測試」才啟用，30Tick／日、30AP／角色；行動成本即工期，戰鬥預設5。原組織等週期設定數字於此新情境按天×30；舊情境及存檔保持原規則。詳見[時間驗收](../document/時間與AP系統_實作與驗收.md)。

生活技能測試：life/skills.json（確認的等級與療效）、options.json（醫療工期／重傷／品質／熟練）、recipes.json（配方／工期／裝備加成）、test-stock.json（有限初始測試物資）。只有新建生活情境載入並保存快照，舊檔不強制套用。製作體力沿characters/stamina.json的ActivityCost；其餘測試值及限制見[驗收](../document/生活技能首版_實作與驗收.md)。

新建停丹測試：life/procurement.json 指定藥草收購初價4；life/medicine-only.json 指定藥膏市場基準12、範圍8～24及草任務價4；life/availability.json 停用生活情境煉丹。所有實際新測試入口套MedicineOnlyProfile，原economy/procurement.json等丹藥欄位只保留舊規則樣本／存檔相容，不是新模式生效品項。詳見[驗收](../document/藥草收購與停丹測試_實作與驗收.md)。

## 凡人修為參數（2026-10-18）

`cultivation/mortal-training.json`、`cultivation/qi-awakening.json` 與 `cultivation/mortal-actions.json` 已接入「階段8 凡人修為測試」新情境與世界設定快照。修改後須開新存檔／新情境生效，舊檔保留原快照，不取代舊耗丹修煉。既有 `cultivation/advancement.json` 屬舊境界／功法規則，不能直接視為新四階段。

| 檔案 | 參數 | 目前值 | 單位／約束 |
|---|---|---|---|
| mortal-training.json | StaminaCostPerAction | 1 | 體力／完成一次修練，正整數且不得超過體力上限 |
| 同上 | ExperiencePerAction | 1 | 經驗／完成一次修練，正數；乘心境／疲勞倍率後保留decimal小數 |
| 同上 | BaseExperiencePerLevel | 100 | 經驗／級，正整數，三功各自套用 |
| 同上 | LevelsPerLayer | 10 | 級／重，正整數，不計出生屬性 |
| 同上 | ExperienceMultiplierPerLayer | 1.2 | 每重需求倍率，有限且≥1；由基值計算後向上取整 |
| 同上 | AttributePointsPerLevel | 1 | 屬性點／級，正整數；性命加到訓練目標，炁功隨機屬性 |
| 同上 | FreePointsPerLevel | 0 | 自由點／級，當前規則固定0；不代表可自行開啟自由配點 |
| qi-awakening.json | SuccessProbabilityPerCompletedMeditation | 0.01 | 機率0～1；有資格後每完成一次打坐獨立判定 |
| 同上 | RollOnThresholdCompletion | false | 布林；滿入門經驗當次不抽，下一次完成打坐起抽 |
| 同上 | PityEnabled | false | 布林；無保底，未設定任何保底次數 |
| 同上 | AttributePointsOnAwakening | 0 | 屬性點／得炁成功，非負整數；當前不直接加點 |
| 同上 | LevelGainOnAwakening | 0 | 等級／得炁成功，非負整數；當前只解鎖 |
| 同上 | ExperienceAfterAwakening | 0 | 正式炁功初始經驗，非負整數 |

角色體力上限100沿用 `characters/stamina.json` 的Maximum，不在此另存一份。工期／AP、入門經驗需求、四屬性隨機權重、疲勞小數與資格已獲確認，見下方mortal-actions設定。後三階段仍待確認。不新增熱更新或舊存檔轉換。

### mortal-actions.json（階段8）

| 參數 | 值 | 單位／約束 |
|---|---|---|
| DurationTicks／ActionPoints | 1／1 | 正整數；1AP＝1Tick，兩者必須相等且不超過每日AP |
| InitialAttributeValue | 10 | 各項基礎屬性初始值，非負整數 |
| InitialAspectLevel | 0 | 初始功等級；目前只支援0，不能憑空補成長來源 |
| InitiationExperienceRequired | 100 | 入門經驗需求，正數 |
| QiAttributeWeights | 四項各1 | 感應／氣感／悟性／氣運正權重；目前確認等機率，須等權重 |
| ApplyGrowthModifiers | true | 已確認套用心境／疲勞並累積小數 |
| BlockSevereInjury | true | 重傷禁止修練 |
| AllowCancellation | false | 不允許主動取消未完成修練 |
| RefundActionPointsOnInterruption | false | 受襲中斷不退AP；未完成不扣體力、不給經驗 |
| AiTrainingUtility | 20 | 正有限效用基值，乘既有角色GrowthWeight並依已獲成長遞減，經共用可行性過濾；非保證NPC一定修練 |

規則旗標目前限定上述已確認玩法，變更會拒絕載入，不能藉改設定啟用尚未實作功能。詳見[階段8驗收](../document/階段8_凡人修為實作與驗收.md)。
## 六種專職與有償生活服務（2026-10-18）

`characters/specialists.json` 是新建藥物測試的最後角色覆寫，取代上方歷史「丹道各3」的現行適用性。六個ActorId不可重複，對應醫師／藥師／鍛造師／性功／命功／炁功各1；Name與LocationId為名稱及位置，InitialStock是正整數有限創世物資，不會定期補貨。WorkUtility=50（工作與專職訓練效用）、SupplyUtility=60（非醫療委託效用）皆為正有限數。無該ID的小型舊Demo不增加人口；新模式清除丹道志向與煉丹師身分，原始角色JSON保留歷史核心情境用途。

`life/service-prices.json`：RequestActionPoints=1為委託提交AP（正整數），MedicalUtility=200為就醫效用（正有限數）；LaborFees為medical5、severe-injury15、ointment3、bleeding-powder5、iron-sword10、leather-armor10（錢財／次或件，正整數）；MaterialPrices為herb4、ointment12、iron-material5、beast-hide4（錢財／份，正整數）。師傅供料總價＝手工費＋配方用量×基準材料價；客供料不加材料費。缺材料價、無效服務、非法數字或上溢拒絕載入。

兩份已接新建情境、AI與世界快照；修改後新建世界生效。預設啟動為階段8凡人專職情境；其他舊式新建測試沿自身世界規則，不強加時間／三功。舊存檔不新增職業或服務。詳見[操作與驗收](../document/六種專職與有償服務_實作與驗收.md)。

## 六職十年量測工具

`testing/specialists-long-run.json`：Years（遊戲年，正整數）、Seed（非零亂數狀態）、ProgressEveryTicks（正整數進度間隔）、SamplesPerYear（正整數採樣數）、BatchSteps（正整數批次上限）、ValidateEveryTick（true逐Tick完整驗證；false每批次完整驗證）。Performance的Nearby／Regional／RemoteIntervalTicks固定1以保留原全頻率AI，DiagnosticRetentionTicks決定顯示事件在記憶體保留窗，LogCandidateDetails僅控制理由詳略；不修改價格、材料、成長或AP規則。

執行：`dotnet run --project src/Headless --configuration Release --no-restore -- --specialist-years <新輸出目錄>`。輸出季度samples.jsonl、最後report.json及初始／最後快照；非必要成長／AP／休息顯示事件存archived-events.jsonl，經濟帳本／服務收據／去重ID／AP歷史仍保留。10年使用現年曆為36000世界Tick；玩家戰鬥以既有共用自動戰鬥推進，不把凍結的戰鬥輪數當世界Tick。

量測與正常逐步模式120步完全一致、240步歸檔重組後實質世界一致，另有批次失敗還原驗收。長測結果是否完成以report.json的Completed與CompletedTicks為準，不把預備試跑或部分樣本當10年；正式結果與限制見開發進度及最終報告。

### 聚落建築首批（2026-10-22）

settlements/buildings.json：八類建築的Kind、Terrain（Land／Plains／Forest／Mountain）及RequiresEmployees。無成本、產量、工資等未確認數值；Kind必須完整且不重複，非法列舉拒絕並回報設定路徑。已接SettlementIntegrity.ReadRules，讀取後由WorldState.SettlementBuildingRules保存快照；現有情境不自動載入，舊檔保留。農田限無森林與山地的平原，林場限森林，礦場限山區；湖泊不可建。一般建築Land是首批安全放置限制，後續特殊選址另訂。此檔本身只定義地形與僱員資格；2026-10-22另接下列runtime設定啟用新聚落情境營運，未確認功能仍不啟用。

### 聚落營運設定（2026-10-22確認並接入）

- `settlements/runtime.json`：InitialLevel正整數、InitialPopulation非負居民數、InitialHappiness介於MinimumHappiness／MaximumHappiness（0～100）；InitialStones為非負創世錢財。HallCapacity／HousingCapacity／HallCapacityPerLevel是居民容量，前者不得小於初始人口，後兩者為正整數。
- EmployeeLimit為每棟員工上限正整數；DailyWage為每員每遊戲日錢財、DailyOutputPerEmployee為每員每遊戲日資源份數，皆正整數且拒絕乘法上溢。每日由共用時間設定TicksPerDay決定，不複製30的有效來源。歷史存檔按配置滿一日結算、改配置重新計期；2026-10-23新聚落改為下述固定每日結算。無錢付薪當期停工。
- Outputs只對應Farm→food／Forestry→wood／Mine→mineral。Levels記錄升級目標Level、RequiredPopulation、Stones、Wood，成本非負，目標等級不重複。Prices記錄商鋪每份Buy收購價、Sell販售價，須為正且Sell大於Buy。報價固定，不啟用未確認的動態調價。
- MaterialInfluence為每份實際捐材點數、BuildingInfluence為每棟建造點數，皆非負。MaterialInfluence已接木材／礦物援助；捐錢財不給點。BuildingInfluence=10已接入完成建造的影響力收據；同一建造不再重複給捐材點。
- `settlements/demo.json`保存SiteId及Forest／Mountain測試位置，須不同且存在、不覆蓋其他據點入口；只覆寫聚落新情境的地形，不更改既有六角情境。
- `settlements/items.json`保存糧食／木材／礦物品項，加入共用Items目錄，初始未發放免費物資。

生效方式：建立「聚落營運測試」或在該模式開新存檔時讀入，驗證後存入世界快照；讀舊檔不重讀、不遷移。初始員工均未僱用、員工可用數暫等於抽象居民人口；幸福變化、人口成長、維護費、製造與影響力用途均未接入。詳細操作與驗收見 `document/聚落營運_實作與驗收.md`。
### 聚落組織出資建造（2026-10-22確認並接入）

`settlements/construction.json`：Kind必須完整且不重複地列出Housing／Farm／Forestry／Mine／Shop；Stones（錢財／棟）、Wood（木材份／棟）均正整數，目前各20／10。立即完成不另設工期，沿用共用世界行動AP。每棟影響力取runtime.json的BuildingInfluence，不在建造檔複製點數；民居增加容量取HousingCapacity。

新聚落情境建立時讀入WorldState.SettlementConstructionRules；舊聚落存檔未保存該欄時不自動啟用，需開新情境。讀檔使用當時成本快照，改JSON後須新建世界才生效。ActionIntent攜帶獨立建造位置，核心驗證聚落領地、地形與管理／出資權限；錢財木材由出資組織帳戶轉至consumed，建築放入聚落Buildings，資源及營運費用仍走聚落帳戶。鐵匠鋪、藥局及大廳不在此新建清單，未藉建造啟用製造或維護費。
### 聚落所有權、據點、建築升級與稅務（2026-10-23已接入）

本節取代前述2026-10-22「新建築歸聚落／所有設施需僱員」的現行描述；舊存檔保留原模式。

- `settlements/organization-sites.json`：Status=Enabled；InitialLevel=1；ConstructionStones／Wood與UpgradeStones／Wood為每棟／每次升級成本，均正整數，現在20／10。Sites只允許Branch／Headquarters，各基礎容量3，每級增量1／2；公式BaseBuildingCapacity＋(Level－InitialLevel)×BuildingsPerLevel。每組織每聚落一座，不計據點及初始建築。重複Kind、非法等級、負配額或非正增量拒絕。
- `settlements/operations.json`：InitialBuildingLevel正整數1，EmployeesPerLevel正整數5（每升級新增員工上限），UpgradeStones／Wood正整數20／10。1級員工上限沿runtime.json的EmployeeLimit，不複製5。升級目前限Farm／Forestry／Mine，由建築主人付費。
- `settlements/taxes.json`：ProductionRate為0～1的小數稅率，2026-10-23依使用者更新為0.3（30%）；僅新建情境載入，已保存世界保留原稅率快照。僅組織資源生產課實物稅；公有不向自己課稅。每棟小數餘額保留到下期並存檔，無交易稅設定。不存在用未定稅率當生效值的情況。
- 生產基準及工資仍只取runtime.json的DailyOutputPerEmployee／DailyWage；有效人數為公有建築上限或組織建築實際僱員，產量不再乘等級。公有工資為0、不占實際居民僱員，組織由其帳戶付薪。建築定義RequiresEmployees在新模式表示營業設施，公有自動營運不另僱人，組織仍實際僱人。

新建「聚落營運測試」載入並驗證以上規則，再存入WorldState設定快照；缺少規則的舊檔不自動啟用、不重新指派所有權。升級／建造／產出與稅額皆有收據及帳本驗證。錯誤拒絕會指出operations.json、taxes.json或organization-sites.json與欄位類別。詳見`document/聚落組織據點與建築營運_實作與驗收.md`。
## 貨幣分類（2026-10-23）

`items/currencies.json` 保存兩種分類：Money＝錢財，Enabled=true、ResourceId=spirit-stone；SpiritStone＝新版靈石，Enabled=false、ResourceId=null，僅保存待確認分類，尚未生效。Kind須完整且唯一、Name不可空白；Money須對應items/catalog.json的既有貨幣項目，名稱必須一致；新版靈石目前禁止設為啟用或分配資源ID。沒有新增價值、匯率或數量設定。

現行所有價格、工資、成本、初始資金與報酬的單位均改為錢財，數字維持不變。spirit-stone、Stones、InitialStones、StoneDelta等為相容舊檔保留的技術名稱，現在皆指錢財，不代表新版靈石。catalog.json的Name為物品顯示名稱，必須與分類名稱相符，載入時不一致會拒絕。

新建情境讀入並保存WorldState.Currencies設定快照；修改JSON需新建世界，讀檔不讀目前config。舊檔只將歷史spirit-stone項目的顯示名稱「靈石」改為「錢財」，不轉換數量、不改帳本及價格，也不補入新貨幣規則。歷史文件與驗收紀錄中的舊「靈石」均按現行錢財解讀。
## 怪物掉落與獸皮（2026-10-23，已接入）

`combat/dungeon.json` 的 `Loot` 保存掉落品項與數量，建立副本情境及其衍生新世界時讀入 `WorldState.Dungeon.Loot`，隨存檔保存；修改後需新建情境，目前運行世界及既有存檔不會重讀JSON。

| 欄位 | 用途與目前值 |
| --- | --- |
| Loot.Wolf | 狼必掉獸皮，ResourceId=beast-hide，MinimumQuantity=1、MaximumQuantity=1 |
| Loot.BanditMoney | 山匪的錢財掉落，ResourceId=spirit-stone，數量1～5；機率沿用BanditStoneChance=0.8 |
| Loot.BanditRecovery | 山匪恢復品，ResourceId=$recovery，數量1～1；機率沿用BanditPillChance=0.1 |

ResourceId須為當前世界已登記物品ID，或特殊符號`$recovery`（依世界模式解析成藥膏ointment／歷史丹藥pill）；不能填物品中文名稱。兩個山匪掉落欄位解析後不得同種物品，避免重複發放。更換品項沿用該欄位的機率，不自動新增任務或市場需求。

MinimumQuantity／MaximumQuantity單位為物品份數，均為正整數、最大值不可小於最小值；隨機區間的最大值減最小值須小於UInt32.MaxValue，超出拒絕載入。相同上下限為固定數量。各Chance為0～1有限數，非法值回報combat/dungeon.json與欄位。狼掉落固定保證取得，山匪兩項各自獨立抽取，沿用既有規則。

單人戰利品與組隊分配共用此掉落表，不再將非錢財一律截為1份；錢財仍受`combat/combat.json`的MaximumLootStones（目前25／場）限制。背包容量不足仍依原規則不領取該批戰利品。AI估算採設定平均數量與已知價格，不把期望值當實際掉落。

`items/catalog.json`已將beast-hide顯示名稱改為「獸皮」；任務、收購、鍛造配方、材料報價與庫存沿用同一ID和原數值。舊存檔載入只將歷史物品名「狼皮」改為「獸皮」，不改帳本／配方／數量；無Loot快照的舊世界沿用專供相容的原掉落規則，不套用新JSON。歷史文件、事件與驗收紀錄保留原文。
## 聚落固定日結算、居民消費與升級保留（2026-10-23，已接入）

`settlements/operations.json`新增FixedDailySettlement=true，新聚落在世界Tick為TicksPerDay整數倍（目前30、60、90…）統一結算；TicksPerDay僅取time/calendar.json，不複製數字。先計算當日生產／工資／生產稅，再處理聚落公有庫存的居民消費。按結算時在營運的建築等級及員工數計算完整當日產量與工資，未另設按小時比例；升級或調整人數不再重啟日界時間。商鋪不自動產生固定利潤，鐵匠鋪／藥局製造仍未啟用。

`settlements/daily-economy.json`保存Consumption兩項：food及wood，各自UnitsPerResident=1（份／居民／日）、MoneyPerUnit=1（錢財／份）。兩數為正整數，須保證int.MaxValue人口的消耗及所得錢財不溢出long。ResourceId不可重複，必須完整列出food及wood；無效值拒絕載入並指出設定檔欄位。

每日每項消耗＝min（人口×UnitsPerResident，max（實際庫存－下一級升級保留量，0））；所得錢財＝實際消耗×MoneyPerUnit。糧食、木材獨立結算，缺任一項不影響另一項；不足只消耗可用數量，不透支，不新增飢荒、人口死亡或幸福懲罰。

下一級升級保留量直接讀runtime.json的Levels：現行1級保留100錢財＋20木材，2級保留300錢財＋50木材，已達最高級保留0。人口尚未達標也先保留，不另設重複成本。公有商鋪交易與公有建築升級只能動用保留外資產；聚落升級本身可花保留款與材料，仍需人口達標並手動操作，升級後改保留再下一級需求。組織建築與組織庫存不參與公有居民消耗。

居民消耗是使用者確認的物資換錢規則：物資記入consumed，錢財由明確增發來源$settlement-residents入帳，並保存每格物資消耗／錢財收入收據；沒有從居民已領工資帳戶扣款。帳本驗證檢查實際消耗前庫存、保留量、人口上限、收益、日界及重複付款。

新情境讀入WorldState.SettlementDailyEconomy並保存規則與收據，修改設定需新建情境。舊存檔缺少這些欄位時不啟用居民消耗；缺少FixedDailySettlement時保留原相對週期。UI顯示固定下次結算日、升級保留、可動用庫存與居民消費收支。初始人口20但農田／林場各日產10份，糧食不足20時只消耗當日10份，木材先累積20保留量；這次沒有擅自調整初始產量。
## 農田與林場產量調整（2026-10-23）

`settlements/runtime.json`新增DailyOutputOverrides：Farm=5、Forestry=5，單位是每位有效員工／每日產出份數。礦場沒有覆寫，仍沿用DailyOutputPerEmployee=2。覆寫鍵僅可為Outputs中已有的生產建築，值須為正整數且最大員工數乘積不溢出long；非法值指出DailyOutputOverrides欄位。公有建築視為滿員，初始5人上限即農田25糧食／日、林場25木材／日；組織建築依實際員工數及原稅制計算。

新世界保存此設定快照，修改後需新建情境；舊存檔缺少DailyOutputOverrides時保留當時的DailyOutputPerEmployee，不套用目前JSON。這項產量調整已接入並通過專項驗證。

使用者後續已確認消耗區間採盡量消耗、滿100增1人口，現已接入下述人口成長規則；先前待確認紀錄保留在進度表歷史中。
## 消耗區間與人口增長（2026-10-23，已接入，取代先前固定上限）

`settlements/daily-economy.json`的Consumption仍有food／wood兩項。UnitsPerResident=1現在是最低需求（份／人口／日）；新增MaximumUnitsPerResident=2作每日消耗上限，MoneyPerUnit=1仍為錢財／實際消耗份數。每日消耗採min（人口×MaximumUnitsPerResident，扣除下級升級保留後的可用庫存），不是隨機，不強制達到最低需求。每項各自結算收入，完整組數取糧食／木材實際消耗較小者；未配對的單項物資也依原兌換規則給錢，但不算完整組數。最高值須不小於最低值，均為正整數；驗證人口×上限及收入不溢出long。未保存MaximumUnitsPerResident的舊快照仍以UnitsPerResident為固定上限。

同檔PopulationGrowth參數如下，建立新世界時保存快照：

| 欄位 | 目前值 | 單位與驗證 |
| --- | --- | --- |
| InitialValue | 0 | 初始增長點，須介於MinimumValue與Threshold之間，未滿門檻 |
| MinimumValue | 0 | 增長值下限，非負且小於Threshold |
| Threshold | 100 | 每次增加人口消耗的點數，正整數 |
| PointsPerGroup | 1 | 每多／少一完整組增減點數，正整數且驗證最大每日變化不溢出 |
| PopulationPerThreshold | 1 | 每次達門檻增加的居民數，正整數 |

兩種物資的UnitsPerResident必須相同，避免完整組最低需求不一致。每日增減＝（min（實際糧食消耗，實際木材消耗）－人口×最低需求）×PointsPerGroup。計算後不低於MinimumValue，然後每滿Threshold增加PopulationPerThreshold人口並扣對應點數；同日可結算多次門檻，超出保留。住房滿額時不增加人口、不扣無法兌換的點數；容量增加後，在下次日結算重新檢查。沒有新增人口減少或自動聚落／建築升級。新增居民依既有抽象人口模型一併增加可工作居民數。

20人口的例子：實際消耗21糧食＋21木材，增長+1；各有41份可用時最多各消耗40，增長+20並保留多餘庫存；兩者均消耗0時增長−20，但最低0；40糧食＋19木材只有19完整組，增長−1。

狀態保存在SettlementState.GrowthProgress，日結算收據保存於WorldState.SettlementPopulationGrowth，並驗證物資消耗、門檻、人口與成長點歷史連續性及重複結算。讀檔不讀現在config；舊快照未保存PopulationGrowth時不啟用人口增長。改JSON須新建情境，介面顯示消耗區間、增長值／門檻與每日變化。
## 國家功能移除（2026-10-23）

organizations/hex-world.json已移除青雲國，map/layout.json移除其國家管轄範圍及StateLayer欄位，scenes/sites.json移除國家治所。現行只有宗門／幫派／山寨三種組織與單層排他領地，搭配村莊／城鎮／城市，共六種獨立場景。國家場景StateSite.tscn已刪除，不再接受State類型或StateLayer設定。其他組織及聚落數值保持原設定。

非國家類型的序列化整數ID維持不變，舊國家編號0不重用。含國家組織／治所／非空StateId的舊存檔會明確拒絕載入，原檔不更動；無國家的舊存檔可繼續讀取，舊StateId=null欄位忽略。沒有進行資產自動刪除或隱藏轉移。

## 村內NPC組織觀察情境（2026-10-23）

`settlements/resident-organization.json`僅由「村內組織測試」情境讀取，原聚落營運情境不載入。Organization保存柳溪幫ID／名稱／Gang類型及NPC首領ID；LeaderTemplateId=npc:12沿用既有普通角色能力，LeaderName為顯示名稱，新增一名NPC，不更動玩家控制角色或原六種專職。SettlementId指定柳溪村，SceneTemplateId沿用既有幫派畫面資源，建立村內獨立入口；InitialSiteKind=Headquarters沿用organization-sites.json的初始等級與建築配額。

InitialStock為開局一次性有限物資，spirit-stone=1000（現行錢財）、wood=100份，已由使用者確認；僅允許這兩種資源，須足以支付現有總舵建造費。建立初始總舵按共用規則扣20錢財／10木材，因此剩980錢財／90木材，不無限補貨。沒有額外配發首領個人資金，也不複製角色範本持有物品。

初始總舵另存InitialPlacement創世收據，僅允許已保存此觀察情境設定的指定組織／聚落／總舵類型在Tick0使用，仍核對資金與材料扣款；一般執行中的建造仍走原資格及付款驗證，不因NPC身份放寬。此筆初始建造不算自主發展。

設定讀入WorldState.SettlementOrganizationDemo，隨世界快照保存；改JSON需新建該觀察情境。舊存檔與普通聚落不自動加入此組織。新開存檔會重建此專用情境。無效首領範本、組織ID、聚落／場景範本、類型或不夠支付的初始資源會拒絕載入並指出resident-organization.json欄位。

UI情境按鈕為「村內組織測試」，命令列入口--village-organization-demo。聚落管理的建造與援助頁顯示各組織總舵等級、配額、資金、木材及成員數，可由世界地塊進入柳溪幫場景；角色紀錄可選柳溪幫主觀察。沒有新增自主建造／總舵升級／招工策略，現有社交AI如有招募結果則依實測報告記錄，不能將開局配發當成AI自主成長。
## 組織自動發展（村內組織測試）

`settlements/organization-development.json` 僅於新建「村內組織測試」由 `SettlementOrganizationScenario` 載入，保存在 `WorldState.OrganizationDevelopment`。讀檔沿用快照；舊檔缺少快照時不啟用，普通聚落情境不啟用。以下均為2026-10-23使用者確認、已接入的首版測試規則。

| 欄位 | 用途／單位 | 合法範圍 |
| --- | --- | --- |
| BuildOrder | 各建一座的生產建築順序 | 非空、不重複、必須是已啟用且可建造的資源建築 |
| TargetBuildingLevel | 生產建築目標等級 | 整數，不低於初始建築等級 |
| MaximumSiteLevel | 配額不足時總舵升級上限；不是必須升到的目標 | 整數，不低於初始總舵等級 |
| ReserveMoney／ReserveWood | 自主建造、升級支出後最低保留錢財／木材 | 非負整數；不改寫既有每日工資規則 |
| AutoStaff | 優先依順序補聘可用居民至員工上限 | 布林；須足以保留錢財並支付該棟一日工資 |

建造、升級費用及員工上限仍分別讀取 `construction.json`、`organization-sites.json`、`operations.json` 與 `runtime.json`，不另複製。首領只管理自身測試組織所在聚落；返回聚落後透過共用指令執行，不能遠端施工。沒有自動買賣、補款或跨組織操控。

## 常駐額外市場（已接入，2026-10-23確認）

`economy/external-market.json` 僅在新建「村內組織測試」載入；世界快照為 `ExternalMarket.Rules`，舊存檔缺少此欄位時保留無市場模式。`SettlementId` 必須為該世界既有聚落；`PurchaseMultiplier` 是市場收購倍率（0～1、不含端點，確認0.8）；`SaleMultiplier` 為市場販售倍率（確認2）；`ActionPoints` 為每筆交易AP／Tick（1～30，沿用交易測試成本1）；`OrganizationAutoTrade` 控制柳溪幫自主交易。無資金／庫存上限為此測試市場特性，不代表玩家或組織有無限財產。

基準價不重複寫入另一份有效設定：材料沿用 `life/service-prices.json.MaterialPrices`；糧食、木材、礦物沿用 `settlements/runtime.json.Prices[].Buy`；其餘成品按 `life/recipes.json` 原料數量×材料基準價，加 `service-prices.json.LaborFees`。新建時解析結果存入 `ExternalMarket.Rules.BasePrices`，讀檔不重新套目前config。藥膏已有基準12，優先於配方估價；止血散17、鐵劍20、皮甲23為目前解析結果。所有價格單位為錢財。

玩家購買總價＝數量×基準價×販售倍率；出售總價＝向下取整（數量×基準價×收購倍率）。總價不足1不成交，並檢查數量、錢財、庫存及整數溢位。貨幣／貢獻點與禁用丹藥不列商品。裝備購買每次單件普通品質，販售獨立裝備依其種類基準價，不新增品質溢價；裝備須先卸下。

組織只由首領在市場所在聚落交易；保留木材＝原發展策略保留量＋既定建造與升級尚需木材，避免剛賣就買回。出售超過保留需求的物資，沒有可出售物資時，才用保留錢財外資金補建材。仍消耗AP／Tick，交易完成時重新檢查。玩家只能操作本人或本人擔任首領的組織帳戶。

## 聚落人口投入與組織人力（2026-10-23確認，已接入）

新建聚落情境載入以下設定並保存世界快照；新建村內組織測試再啟用柳溪幫自主目標。讀檔不查詢目前config，缺少對應快照的舊檔不自動啟用。新建聚落沿已確認藥物模式初始化生活物資及規則，招攬初始物資不含丹藥。組織招攬先檢查首領權限、所在地、據點、資金與容量，玩家與NPC共用規則。

| 設定路徑／欄位 | 用途與單位 | 合法範圍／目前確認值 |
| --- | --- | --- |
| `settlements/taxes.json.ProductionRate` | 組織資源生產實物稅率 | 0～1；目前0.3，歷史10%快照保留 |
| `settlements/population-investment.json.GroupsPerAttempt` | 每筆人口投入組數 | 正整數；10 |
| `ResourcesPerGroup` | 每組實際消耗的物資 | 正整數，目前food1／wood1；須有此兩種資源 |
| `SuccessProbability` | 每次投入獨立增加人口的機率 | 0～1；0.1，使用可存讀亂數狀態 |
| `PopulationPerSuccess`／`ActionPoints` | 成功增加居民／每筆AP與Tick | 正整數／1～30；目前各1 |
| `organizations/labor.json.RecruitmentCost` | 每名抽象雜役招攬錢財 | 正整數；100 |
| `DailySalary` | 每名雜役每日錢財，含未派工者 | 正整數；5 |
| `DormitoryBaseCapacity`／`DormitoryCapacityPerLevel` | 1級雜役宿舍容量及每升1級增加容量 | 正整數；20／10 |
| `BaseMemberCapacity`／`SeniorDormitoryCapacityPerLevel` | 每組織基礎成員上限及每級高級宿舍增加上限 | 正整數；10／5；含首領 |
| `ActionPoints` | 招攬／派工每筆AP與Tick | 1～30；1 |
| `organizations/recruitment.json.MemberTemplateId` | 固定普通成員屬性來源 | `characters/autonomous.json`的既有普通角色ID；npc:03，存入快照，並非複製執行中的角色 |
| `MemberName` | 新成員固定名稱前綴 | 非空；後接順序號，未啟用隨機姓名 |
| `MemberCost` | 組織實際轉給新成員的招募費 | 正整數；1000 |
| `MoneyMinimum`／`MoneyMaximum` | 額外初始錢財均勻抽取範圍（含端點） | 正整數最小值≤最大值；1～100，不是從組織免費再扣一次 |
| `Supplies` | 額外初始物資各自均勻抽取範圍 | 已知非貨幣物品，0≤Minimum≤Maximum；藥草0～5、藥膏0～2、獸皮0～3 |
| `organizations/labor-autonomy.json.TargetLaborers`／`TargetMembers` | 柳溪幫自主目標 | 非負／正整數；雜役5、成員總數3（含首領） |
| `SalaryReserveDays` | 招攬後至少保留的雜役薪資日數 | 正整數；10，另加既有100錢財保留量，木材仍保留20 |
| `DormitoryTargetLevel` | 自主建立兩類宿舍之目標等級 | 不低於初始建築等級；1 |

兩種宿舍新增在 `settlements/buildings.json` 與 `construction.json`，同樣占組織據點建築配額；各新建20錢財＋10木材，升級沿用 `operations.json` 的20錢財＋10木材。不複製升級成本；不增加聚落人口住房。每座雜役宿舍容量＝20＋（等級－初始等級）×10；組織成員容量＝10＋所有高級宿舍等級×5，主建築升級不額外加成。

抽象雜役不進具名NPC名冊或聚落人口，生產建築中「居民僱員＋派駐雜役」不得超過原員工上限。居民照原工資、雜役另在每日組織薪資帳領取5，不重複按居民工資扣款；缺錢無法支付整批雜役薪資則該日雜役停工、不負債、不擅自解僱。宿舍及招攬均沒有無限補款。

人口投入是實際消耗，成功或失敗都不退料；滿住房或材料不足則拒絕且不消耗。公有帳戶受原升級資源保留限制；組織供料依 `runtime.json.MaterialInfluence` 計每份材料影響力，目前每次20點，不另複製此數字。
# 功法方案數值（2026-10-24）

青雲拳法使用規格更新：UseStaminaCost=0、CooldownRounds=1；CooldownRounds為非負整數，單位戰鬥Round，不是世界Tick，null表示該技能冷卻尚未指定。模型／讀取驗證已支援，戰鬥招式尚未接入，所以冷卻尚未在戰鬥中生效。修練仍依TrainingRules扣1體力，不受本次使用成本變更影響；柳溪劍法保持原使用成本。

升層資格更新：`methods-proposal.json.UseCurrentAttributesForUpgrade=true`（布林，已確認且接入）使新功法世界依目前總屬性檢查升層，而非僅永久值。總值由CurrentAttributeCalculator統一合計基礎、永久成長、Character.CurrentAttributeModifiers各來源修正；攻擊／防禦加成不冒充力量／體質。舊世界快照缺少此欄位為false，保留舊判定，不自動改檔。入門開始修練資格未改。使用者已確認加成消失時重新計算可用層級，熟練不減；加成恢復且資格符合時重新取得對應可用層級，不記錄永久升層。未啟用功法被動。

`cultivation/method-test-npc.json`：新建功法情境額外NPC，TemplateActorId必須為該情境既有角色、ActorId不可重複、Name非空、InitialMoney為非負整數錢財（預設有限300）。範本沿用既有基礎屬性／體力／性格設定，職位改普通成員，初始技能熟練仍沿用method-scenario.json。其功法訓練目標保存於World.Methods.TrainingFocusActors，只篩選該NPC訓練候選，不新增經驗／體力優惠；休息及其他共用規則保持。設定已接入，只影響重新建立功法情境，不向舊檔補人或補款。

**後續接入更新（取代下文較早的僅保存狀態）**：`cultivation/methods-proposal.json`現由新建「功法修練測試」情境載入並保存在World.Methods快照；TrainingRules的Tick／AP／體力／技能熟練／三功經驗已生效，資格採永久基礎屬性＋永久成長、累計門檻與自動層級查詢已接入。心境／疲勞沿用共用設定。`cultivation/method-scenario.json`保存初始熟練（非負decimal）、玩家及各組織功法ID分配（必須存在於目錄）。只影響該新情境，原凡人情境／舊檔不自動啟用。Status為TrainingEnabled_EffectsPending：被動屬性、主動招式傷害／戰鬥熟練尚未接入，對應效果欄位仍僅保存規格，修改不會改變戰鬥。現有AI成長權重沿用mortal-actions.json，不新增隱藏功法偏好倍率。

`cultivation/methods-proposal.json`：**僅保存已確認規格，尚未生效**，已提供CultivationMethodConfiguration.Read明確讀取／驗證入口，尚未由情境呼叫，未接世界快照。層數／熟練需求為正整數，屬性加值與傷害加值非負，體力使用成本為非負整數；傷害倍率採倍率（1.2即120%），屬性係數為非負數。已確認技能獨立熟練、累計門檻100／200／300；青雲入門10／10，升2／3層20／20。柳溪劍法倍率1.1。TrainingRules保存每次1Tick／1AP／1體力／1基礎技能熟練及1基礎三功經驗，均為正值；倍率沿用既有共用設定。力大飛磚獨立修練。以上仍僅保存，尚未接入程式。詳見[功法方案](../document/功法系統_技能標籤與熟練方案.md)。

# 目前主要測試情境（2026-10-25）

`testing/scenarios.json`已接入TestScenarioCatalog與Godot主要情境選單：DefaultId必須對應Scenarios中的ID；每筆Id只能為methods／settlement／battle且唯一，Name／Description不可空。這是情境選擇與顯示設定，不新增平衡參數或覆寫其他系統數值。啟動及重新建立時讀取，世界規則仍由既有各工廠載入並保存快照；歷史情境保留，新局不覆蓋存檔。詳見[功能總覽與情境](../document/目前功能總覽與測試情境.md)。

# 獸皮資源ID（2026-10-27）

現行獸皮唯一ID為`beast-hide`，已同步物品目錄、掉落、配方、價格、任務、AI與招募配置；`wolf-hide`不再作現行ID。修改後重新建立情境載入；依使用者要求不處理舊存檔，不提供舊ID別名或自動轉換。較早保留wolf-hide的敘述為歷史，現以本規則為準；歷史實測JSON不改寫。

# 玩家行動驅動時間（2026-10-29）

`time/action-driven.json`已接目前兩個主要世界時間情境與新局重建，保存到World.Time.ActionDriven：IdleRetryTicks（正整數Tick，預設5）是無可行行動NPC的重試間隔；受傷／移動／新可見通知可提前喚醒。MaximumAdvanceSteps（正整數步數，預設10000）是單次玩家活動執行安全上限，超限還原活動前狀態。`time/action-costs.json.Wait`為等待AP／Tick成本（預設1、合法1～30），其餘行動仍讀原成本。新模式預設主角自主排程關閉，GUI可選開啟；非法指令不推時、查詢免費、合法活動完成後停下。舊檔缺少ActionDriven保留原模式，不自動遷移。詳見[操作與驗收](../document/玩家行動驅動時間與NPC決策_實作與驗收.md)。

# 分鐘制AP（2026-10-29最新確認）

`time/action-driven.json`已接新主要情境：MinutesPerActionPoint=10（正整數分鐘，也是內部排程步長）、MinutesPerDay=1440（正整數分鐘，須整除步長）、RestActionPointsPerStep=1（正整數AP，合法1～30）、WaitActionPoints=0（AP，合法0～30）、WaitDurationTicks=1（正整數內部步數，現為10分鐘）。等待只經過時間，休息不扣AP並每10分鐘回1AP，其他活動每10分鐘扣1AP；上限及跨日補滿維持30。新情境由MinuteTimeProfile換算日長及日制週期後保存規則快照；修改設定須重建。舊快照缺欄位保持舊規則，無自動遷移。較早「等待扣AP／休息付AP」文字僅保留為歷史，最新規則以本段為準。

# 批次修練（2026-10-29）

`time/action-driven.json.TrainingBatchLimit=30`（正整數次數）已接入新模式屬性修練／打坐／功法修練，UI可選次數並預覽總時間與AP。開始扣總AP、每段套用當下疲勞／心境並結算體力與經驗；受襲或體力不足停止剩餘段數，AP不退。缺少設定的歷史快照維持單次。原單段成本仍各由cultivation設定供應，沒有另建批次收益值。詳見[操作與驗收](../document/批次修練_操作與驗收.md)。

