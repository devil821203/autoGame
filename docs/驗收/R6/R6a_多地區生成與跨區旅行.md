# R6a 多地區生成與跨區旅行

2026-10-31。使用者確認先做R6a：3個地區、各7×7六角格，至少一處聚落／資源點及可達路線；沿用旅行成本與AP，不新增遭遇玩法。R6b未開發，R5及R1～R3未完成項目不因此結案。

## 使用方式與範圍

Godot情境列新增Seed輸入與「R6a多地區測試（重建）」。地圖的地區選單可選擇聚落、定位鏡頭，再按既有旅行按鈕；也可直接選地塊。抵達後可從原場景入口進入當地聚落。選地區只是查詢／選目的地，不會切換控制角色或瞬移。

原三類情境及歷史入口保留。只有新R6a情境啟用多區拓樸，不修改舊檔。R6a新局沿用既有人物／組織及屬性，角色初始位於青雲谷，沒有擅定新NPC人口配額或新增勢力。

## 設定與生成

`config/world/regions-demo.json`已接入：

| 欄位 | 單位／範圍 | 已確認測試值與用途 |
| --- | --- | --- |
| Seed | UInt64非負整數 | 預設12345，沿用原測試種子；UI可指定，實際種子存入世界 |
| Width／Height | 六角格數、正整數；須容納既有據點及地形範本 | 7／7 |
| Regions | 有序地區ID／名稱；首區保留valley、ID唯一且無冒號 | 青雲谷／青石原／落霞境，共3區；陣列長度決定數量 |
| HomeSiteOverrides | 首區內六角座標 | 新R6a將原望川城場景定位至(6,6)，使既有場景能放進7×7；原10×8情境不改 |

沿用原HexIntegrity工程容量限制：高度及總寬不超過128、總格數不超過16384；拒絕非法維度、重複ID、越界據點或不足以放入範本的配置，不截斷資料。

每區以原`config/map/layout.json`的地形種類／數量作範本，使用Seed衍生的獨立區域亂數流排列剩餘位置，不新增隱藏的地形生成機率。首區保護既有五據點、組織地塊與場景位置；新區各配置聚落與森林資源點，依原村莊場景範本提供主建築／建築格。

地區依設定順序串接，跨區只能走登記連線；保護聚落、資源點及連線端點間的通道，保證初始可達。路線端點及其他地形受Seed影響。生成器版本1，區域／路線／後續模擬使用分離的Seed流；不使用Random.Shared或畫面時間。

完整重現條件是相同Seed、生成器版本及內容指紋，不承諾跨任意程式版本重播。`config/content/manifest.json`現為`r6a-1`，仍接受已存在的r4-1／r5-1快照；讀取不載入今天的config。

## 有限資產與市場

新區資源量沿用建立時既有grove的設定範本，市場初始庫存／錢財沿用既有market範本；每筆新增初始資產有Tick0 Genesis帳本來源，並保存範本值供完整性核對。它們是有限初始庫存，未接無限市場發行，也不假裝有未製作的完整聚落生產／人口系統。

新區的市場／資源帳戶沿用既有OrganizationState作經濟所有者表示，不是新增有政治功能的宗門。各區有獨立R5 Venue，撮合不跨市場；公開觀察只能取得當地／同區未過期的資料，不能直接讀遠區訂單。R5核心仍是檢查點，獨立交易介面及完整NPC自主掛單驗收尚未補完，不能因本輪而標整個R5完成。

其他區域的採集使用當地資源帳戶，消耗有限藥草，沿用原採集成本、體力與經驗；不會扣到青雲谷藥園。首區宗門設施任務限制不改，新區不新增宗門工作權或遠距使用青雲宗設施的資格。新區未新增自動補貨／再生規則。

## 旅行與地點一致性

沿用HexNavigation尋路、每格地形成本、AP開始支付與既有逐格完成流程；新拓樸只限制跨區邊界必須有開放連線。較長旅程仍受現有每日AP上限約束，可分段選地塊／休息，不提高上限或透支。

區域內town／grove是本地別名，跨區可用完整region識別或全域六角座標；返鄉不會把town誤認為目前區域的城鎮。跨過連線時同步更新RegionId與HexPosition，抵達／中斷時再同步邏輯地點。路線失效時在當前格中斷，保留已付AP政策；Open欄位用於路徑狀態／中斷驗證，沒有新增玩家免費封路指令或遭遇系統。

舊模式的旅行目的地與路徑行為維持原狀。R1旅行仍由相容事件逐格推進，不能宣稱所有活動已改為獨立秒事件；NPC所有觀察來源也仍有R3缺口。R6a沒有改控制資格，玩家仍只能對ControlledCharacterId下令。

## 存檔與資料驗證

資料保存在`HexMap.Regional`，包含規則快照、實際Seed、生成器版本、區域、路線狀態與初始資產來源；共用原HexMap、角色、帳本、旅程與行動資料，沒有另一份角色座標或錢包。

外部格式9保護新區域資料，仍使用原SQLite DB格式1及完整JSON payload；低版標記夾帶區域資料會拒絕。載入後沿既有流程重建索引／事件堆積，檢查區域、地點、路線、角色座標與資產來源。舊檔不轉換、不覆寫。重建新局保留實際Seed，重新讀取config並重設行程。

## 主要檔案

| 模組 | 檔案 |
| --- | --- |
| 區域模型與幾何 | `src/Domain/RegionalWorld.cs`、`HexMap.cs`、`SceneSites.cs` |
| 生成及驗證 | `src/Simulation/RegionalWorldScenario.cs`、`RegionalWorldIntegrity.cs`、`WorldIntegrity.cs` |
| 舊系統轉接 | `HexTravel.cs`、`HexIntegrity.cs`、`ProductionActions.cs`、`ActionValidator.cs`、`ActorKnowledge.cs`、`NewGameFactory.cs` |
| 保存 | `ContentProfile.cs`、`src/Persistence/SaveMigration.cs` |
| 呈現 | `src/Presentation/Main.RegionalWorld.cs`、`Main.TestScenarios.cs`、`Main.cs`、`HexMapPanel.cs`、`HexMapCanvas.cs` |
| 設定與驗收 | `config/world/regions-demo.json`、`config/content/manifest.json`、`config/README.md`、`tests/Acceptance/RegionalWorldTests.cs`、`Program.cs` |

## 驗收

```powershell
dotnet run --project tests/Acceptance --no-restore -- --regional-world-only
dotnet build src/Presentation/CultivationGame.csproj --no-restore
dotnet run --project tests/Acceptance --no-build --no-restore
# Godot參數尾端：-- --regional-world-smoke
```

證據：`document/evidence/r6a-regions-20261031/`。9/9專項通過，最終Godot建置0警告0錯誤。包含相同／不同Seed、5個Seed的可達與合法性、旅行AP／抵達／中斷、區域採集、跨區市場隔離、兩種格式中途續跑、錯誤資料與新局重建。

首輪7/8發現返鄉town歧義，修正後8/8；之後加入區域撮合隔離及舊模式相容收尾，最終專項9/9。早期完整417/417及6次UI exit0紀錄保留；最終完整418/418通過；R6a兩次、SQLite、選檔、新存檔、原六角地圖各一次，共6次Godot 4.3檢查均有成功標記且exit0。來源提交94a760f已在.tools/r0-git-restore-check隔離還原，Godot建置0警告0錯誤，見git-restore-build.log；未覆蓋主工作目錄、未推送遠端。最終證據用focused-final／full-final／ui-verified-exits，不以舊二進位結果代替。

未驗證人工桌面視覺／FPS、Godot 4.7或跨平台；沒有10,000NPC容量、長年經濟或完整跨區NPC自主性的宣稱。R6b授予／撤銷與無合格角色後續仍待另行確認。
