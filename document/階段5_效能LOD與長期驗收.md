# 階段5：效能、AI LOD與長期世界驗收

日期：2026-10-17。對應需求規格階段5／開發Prompt的效能階段。限定既有5-01～5-06，不增加正式地圖玩法。

## 實作範圍

| 項目 | 實作 |
|---|---|
| 5-01 AI LOD | 相對玩家分為同地點詳細、同區不同地點低頻、其他區域遠方；預設1／4／16Tick，可由`config/world/performance.json`設定。保存個別角色上次評估時間／層級，切換層級不清空資產或排程 |
| 5-02 索引與資料控制 | 每次決策批次建立角色、區域、據點、排程與通知索引；同角色通知合併為最新喚醒，不擴散給其他角色。固定決策預算仍為12，輪替游標保存在世界中；每個角色均不做全NPC兩兩比較 |
| 5-02 帳本 | 私有交易副本使用由帳本推導的增量餘額索引；原帳本仍為唯一真相。完整性檢查的來源／動作／收據以查詢索引取代反覆掃全表，仍逐筆驗證，不放寬來源與非負庫存規則 |
| 5-02 診斷裁剪 | 預設保留120Tick的Decision／DecisionBudget／PlanInvalidated／PartyWaiting，候選評分詳列可設定開關；不裁剪資源來源、通知依據、帳本、工作／交易收據及去重ID |
| 5-03 遠方戰鬥 | 不可見的遠方一對一戰鬥每16Tick集中處理到期真實回合，沿用原攻防／丹藥／隨機／戰利品規則，保留個人生命與結果；不以期望值代替戰鬥。組隊戰保留詳細更新 |
| 5-04 量測 | 每Tick總耗時、複製／AI／驗證耗時、決策數、排程／戰鬥佇列、交易、配置量、Managed記憶體；Headless報告另列進程WorkingSet／Peak。Godot有畫面時顯示實際FPS，無畫面明確標不適用 |
| 5-05 分級情境 | 獨立合成壓測可建立50～20,000 NPC，實測100／1,000／5,000／10,000／20,000；複用能力範本、建立實際個體及創世庫存，四種非均衡志向各3，其餘均衡 |
| 5-06 續跑／長測 | LOD切換中的原料預留／排程、遠方戰鬥及存讀續跑測試；另有1200Tick宗門經濟長測，在600Tick實際寫檔讀回並同步續跑比對 |

原有遊玩情境不強制開啟LOD。新「階段5 效能情境（100 NPC）」啟用設定，按開新存檔可重設同規模情境。長測工具會為宗門測試啟用LOD。合成情境的remote只是既有RegionId欄位的測試分區，不代表已實作正式地圖或跨區旅行。

遠方NPC只能依本地可得資訊行動。尚無設施的遠方測試分區只做個人丹藥修煉／療傷與本地戰鬥判斷，不查青雲鎮市場，也不假造遠方商店。自身修煉／療傷透過同一Validator／Executor，玩家在相同狀態下適用相同規則。生產排程仍於真實完成Tick結算，不跳過消耗。定向通知、位置／層級改變及緊急傷勢可提前喚醒，仍受決策預算限制。

遠方戰鬥的結算可能延至批次邊界；孤立戰鬥實測與詳細模式的回合數、勝負、個人生命、有限戰利品及亂數狀態一致。多場戰鬥的全域亂數使用順序可能因批次順序改變，不宣稱不同LOD設定會有完全相同世界結果；同一設定及輸入的存讀續跑必須相同。

## 設定、檔案與操作

- Domain：`PerformanceModels.cs`、`LedgerBalanceIndex.cs`、WorldState與Inventory。
- Simulation：`AiLodScheduling.cs`、`WorldSpatialIndex.cs`、`RemoteCombat.cs`、`PerformanceIntegrity.cs`、`TickDiagnostics.cs`、`ScaleScenario.cs`；接線Engine／AutonomousDecisions／Combat與完整性檢查。`WorldCodec.Clone`改用UTF-8位元組做完整深層複製，保留提交失敗不改動原世界的規則。
- Headless：`PerformanceBenchmark.cs`、`LongRunProbe.cs`及CLI入口。
- Presentation：`PerformancePanel.cs`、Main效能情境／指標／驗收入口。
- Tests：`PerformanceTests.cs`，驗證更新頻率、定向喚醒、跨LOD預留、遠方戰鬥、診斷裁剪／去重、規模／志向配額與續跑。

Godot F5→「階段5 效能情境（100 NPC）」→「效能量測」，推進Tick查看實測。沒有NPC個體Godot場景的全量實例化；此處以資料及既有繪圖／表格觀察。10,000人以上的基準採Headless，不等同前端能維持某個FPS。

```powershell
dotnet run --project tests/Acceptance --no-restore
dotnet build src/Presentation/CultivationGame.csproj --no-restore
dotnet build src/Headless -c Release --no-restore
dotnet run --project src/Headless -c Release --no-build -- --benchmark 100 20 report-100.json
dotnet run --project src/Headless -c Release --no-build -- --benchmark 1000 20 report-1000.json
dotnet run --project src/Headless -c Release --no-build -- --benchmark 5000 10 report-5000.json
dotnet run --project src/Headless -c Release --no-build -- --benchmark 10000 10 report-10000.json
dotnet run --project src/Headless -c Release --no-build -- --benchmark 20000 5 report-20000.json
dotnet run --project src/Headless -c Release --no-build -- --soak 1200 12345 soak.json
```

報告採CreateNew，不覆寫既有檔；重跑請換檔名。每個基準有5Tick暖機。記憶體與耗時不寫入遊戲存檔，避免污染同種子重現摘要；LOD設定／輪替與上次評估狀態會存入存檔。舊存檔缺欄位時不啟用LOD。

## 基線與改善

執行環境依原始JSON：Windows 10.0.26300、.NET8.0.32、X64、20 logical cores、Intel Family6 Model183；這是本次執行環境，不是最低硬體需求。下列皆為Headless Release，固定12次決策預算，並非每Tick完整評估所有NPC。

| NPC | 基線平均Tick ms | 基線P95 ms | 複製 ms | AI ms | 驗證 ms | WorkingSet MiB |
|---|---:|---:|---:|---:|---:|---:|
| 100 | 10.27 | 14.39 | 7.98 | 0.83 | 1.24 | 58.1 |
| 1,000 | 35.59 | 46.44 | 27.40 | 3.52 | 4.05 | 81.7 |
| 5,000 | 124.83 | 150.55 | 92.97 | 14.03 | 15.11 | 140.4 |
| 10,000 | 234.22 | 403.95 | 169.66 | 25.87 | 33.36 | 212.9 |
| 20,000 | 604.91 | 758.26 | 424.20 | 49.43 | 123.03 | 391.7 |

原始基線為`document/evidence/stage5-*-release.json`。辨識複製與驗證瓶頸後，採UTF-8深層複製及來源查詢索引；最終報告與比較見下方附錄。基線大規模樣本只更新部分角色（10,000人測試涵蓋180位），因此不宣稱所有人都完整決策過，也不以短測宣稱長期容量。20,000人基線已超過目前250ms的Tick間隔，未達即時目標。

## 驗收與限制

目前140/140核心測試通過；最終Godot與Headless Release建置0警告／0錯誤。PERFORMANCE／EXPEDITION／SECT_ECONOMY／NEW_SAVE／GAMEPLAY／COMBAT六組無視窗UI通過。

優化前1200Tick基線長測未完成即中止，沒有宣稱通過；該程序鎖住Windows的DLL，曾造成MSB3027／3021建置失敗。改獨立輸出後建置成功，再執行優化長測；程序結束後標準Release輸出也重建成功。失敗及未完成紀錄保留於總表。

稽核帳本、來源收據與去重ID仍會增長，僅診斷歷史有保留上限。這是可驗證的階段5 MVP及瓶頸報告，不是無限期固定記憶體、穩態經濟或10,000人渲染FPS保證。人工視覺、正式地圖、真實多區域市場、大量角色前端FPS及正式發行封裝未驗證。

## 最終分級量測與長測結果

最終原始資料為`document/evidence/stage5-<人數>-final-release.json`：

| NPC | 暖機／樣本Tick | 平均Tick ms | P95 ms | 複製 ms | AI ms | 驗證 ms | WorkingSet MiB | 曾評估角色 |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 100 | 5／20 | 9.67 | 13.19 | 7.54 | 0.76 | 1.16 | 61.3 | 100 |
| 1,000 | 5／20 | 34.72 | 43.19 | 25.06 | 4.00 | 4.95 | 73.4 | 300 |
| 5,000 | 5／10 | 122.71 | 159.05 | 90.02 | 13.96 | 15.99 | 110.7 | 180 |
| 10,000 | 5／10 | 153.73 | 192.57 | 100.32 | 18.08 | 32.32 | 181.9 | 180 |
| 20,000 | 5／5 | 281.73 | 294.27 | 194.90 | 24.15 | 58.48 | 270.7 | 120 |

最終採UTF-8深層複製、逐動作查詢的來源／收購收據驗證，並讓額外壓測角色保持均衡志向，符合既定四類各3的配額。基線與最終版本的角色配置亦有此修正，且短樣本受JIT／GC／排程影響，不宣稱表格差異為嚴格單因素加速比。

10,000人這次短測平均及P95低於250ms Tick間隔，但每Tick只評估最多12人，且只涵蓋180位角色的決策，不代表10,000人全量AI或渲染容量。20,000人平均281.73ms仍超過250ms間隔；複製與完整驗證仍是主要瓶頸，未達20,000人即時目標。5000以上長時間、大量戰鬥／社交負載及有畫面FPS未驗證。

1200Tick最終長測原始資料：[stage5-soak-1200-final-release.json](evidence/stage5-soak-1200-final-release.json)。第600Tick使用實體檔案存讀，後600Tick同步續跑，`IdenticalContinuation=true`；整體包含對照分支約80.44秒。最後保留13位宗門角色，宗門靈石47,377，成功產丹884；最後300Tick產丹255、收購253，無漏發俸祿、無考核逐出。這是既有無限外部收購測試下的有限時間運轉證據，不代表封閉經濟穩態。

稽核資料實測：初始存檔49,984 bytes，最後4,802,565 bytes；帳本13,768筆、去重ID6,433筆。取樣診斷最多1,114筆、最後535筆，保留期裁剪有作用；仍清楚存在稽核資料增長，沒有宣稱記憶體永遠固定。

前一輪長測同樣完成且續跑一致，但進度寫stderr令PowerShell回傳非零；已改為有輸出檔時將進度寫stdout，最終完整重跑回傳0。最後另補遠方戰鬥轉回附近時補齊到期回合的驗收，140/140仍通過；不影響本次純規模基準及同區宗門長測結果。最終建置0警告／0錯誤、六組UI通過；人工視覺未驗證。

階段5以此有限MVP範圍結案。若要無限期稽核封存、去重ID壓縮或更高即時容量，需另行確認設計與驗收範圍，不自動新增為結案門檻。