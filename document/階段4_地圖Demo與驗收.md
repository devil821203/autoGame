# 階段4 單地區地圖Demo首批

2026-10-17。使用者授權採選項1製作Demo，正式地圖系統仍待確認。本批對應4-01與4-03，維持測試中。

## 操作

Godot F5 →「建立情境／階段4 地圖Demo」→「地圖Demo」頁。建立情境會重建記憶體世界，先另存需要保留的進度。地圖顯示青雲鎮、青雲宗、藥圃與連線，金色圓點為玩家，括號為據點角色數（旅行者抵達前仍計於起點）。

點「前往…」，再單步或繼續推進時間。鎮與其餘據點之間3 Tick，宗門與藥圃之間5 Tick，均從接受指令的Tick開始計算；不含提交前等待Tick。這些是Demo測試值，不是最終規格。

## 核心規則與範圍

ActionKind.Travel使用共用控制權、資格驗證及固定Tick排程；不提前改變位置，抵達後才更新DemoLocationId。旅行中不可交易、修煉或發送其他行動，交戰也檢查參與者是否忙碌。同地旅行／未知目的地／未啟用Demo均拒絕。旅行不產生或轉移資產；同指令ID沿用去重。角色位置、啟用旗標與旅行排程保存在既有JSON，舊存檔缺欄位預設未開啟Demo、位置為鎮。

Demo不改變既有RegionId。NPC可透過同一意圖旅行，但尚未加入自主目的地選擇；經濟／社交等既有行動目前仍按同區規則，不以三個Demo據點作距離限制。所有角色初始位於鎮。未宣稱已完成空間經濟整合、角色自由行走或完整階段4。

## 檔案

新增 `src/Domain/DemoMap.cs`、`src/Simulation/TravelActions.cs`、`src/Presentation/DemoMapPanel.cs`。修改Domain/Models、Simulation/ActionValidator、ActionExecutor、SimulationEngine、WorldIntegrity、Presentation/Main與tests/Acceptance/GovernanceTests，及進度表／README／本文件。

## 實測

```powershell
dotnet run --project tests/Acceptance --no-restore
dotnet build src/Presentation/CultivationGame.csproj --no-restore
& '.tools/godot43/Godot_v4.3-stable_mono_win64/Godot_v4.3-stable_mono_win64_console.exe' --headless --path src/Presentation -- --map-smoke
```

核心 **93/93 passed**，Godot建置0警告／0錯誤；**MAP_UI_SMOKE_PASS**驗證按鈕指令、旅行中實際檔案存讀與到達；**SAVE_PICKER_UI_SMOKE_PASS**回歸通過。核心新增驗收涵蓋未提前抵達、途中拒絕交易、固定Tick到達、存讀摘要一致、同地拒絕、玩家不可操控NPC、NPC共用規則。

人工視覺、滑鼠操作及效能未驗證。NPC自主選路、據點行動整合與完整遊玩流程尚未完成；正式地圖系統統一列於「待確認及後續擴充」，不得自主視為已確認設計。

## 4-01 據點行動與NPC旅行整合（2026-10-17）

本增量延續已授權Demo，不決定正式地圖系統。採集須在藥圃，買賣須在鎮，發布／接受／交付／取消宗門任務須在宗門；只在DemoMapEnabled生效，其他舊情境不變。玩家與NPC同用ActionValidator位置檢查。修煉、煉丹等自身行動目前不綁據點；社交／戰鬥仍按既有同區規則，不代表已有完整距離或視線系統。

NPC評估既有工作意圖；若工作本身可執行而缺少位置，改提出Travel候選。非位置資格仍先驗證，旅行也再次走共用驗證。效用按旅行Tick相對既有PlanLifetimeTicks折算，不新增固定劇本或強制指定NPC。任務期限不足以旅行再接續時不產生旅行替代。抵達後重新規劃，沒有鎖定報酬或保證仍有庫存；既有多步工作計畫於需要旅行時重新建立。

地圖以金色顯示玩家、藍色小點顯示旅行NPC，沿直線按模擬Tick顯示進度；抵達前不改角色正式據點資料。據點人數排除旅行者。尚非連續自由行走或最終美術。

修改 `src/Simulation/ActionValidator.cs`、`src/Simulation/TravelActions.cs`、`src/Simulation/AutonomousDecisions.cs`、`src/Presentation/DemoMapPanel.cs`、`tests/Acceptance/GovernanceTests.cs`，及README／進度表／本文件。

驗證指令沿用上方Acceptance、Godot建置及--map-smoke。首輪93/94：以整段旅行Tick除工作效用導致NPC採集後不返回出售；改按計畫期限折算後 **94/94 passed**。新增測試確認玩家／NPC在鎮不可直接採集、NPC自行前往藥圃、返回鎮出售及旅行中存讀後40 Tick自主續跑摘要一致。Godot建置0警告／0錯誤，**MAP_UI_SMOKE_PASS**。未以模擬結果當效能壓測。

本節取代首批「NPC尚無自主目的地／經濟行動無位置限制／旅行者仍算起點人數」的歷史限制。正式地圖仍待確認；人工視覺、滑鼠操作、長期平衡與效能未驗證。4-01保持測試中，已通過的Demo行為如上，不額外擴張正式地圖規格。
