# 階段4 Demo端到端與結案驗收

2026-10-17。結論：使用者授權的單地區Windows／Godot Demo範圍完成。正式地圖系統與角色切換仍列待確認，不以未確認功能阻擋Demo結案；不是發行版本或全遊戲完成。

## 原定項目核對

| 項目 | Demo實作與證據 |
|---|---|
| 4-01 | 地區／鎮／宗門／藥圃2D據點、固定Tick旅行、NPC自主工作前往返與位置驗證；MAP_UI_SMOKE_PASS。正式地圖另列待確認 |
| 4-02 | 固定Tick有容量的藥圃再生、來源帳本、再生事件、當地角色通知與事件／玩家通知查詢；WORLD_EVENTS_UI_SMOKE_PASS |
| 4-03 | 命名建立預設角色、新世界、附近角色摘要、玩家輸入與共用控制權；PLAYER_DEMO_UI_SMOKE_PASS |
| 4-04 | 角色／庫存／報價／買賣／成交／AI理由、任務玩家接單交付及既有組織頁；GAMEPLAY_UI_SMOKE_PASS，治理頁另有前批驗收 |
| 4-05 | JSON原子另存、不覆寫、排程／去重／亂數／計畫／記憶保存；核心檔案測試通過 |
| 4-06 | schema1→2記憶體遷移、錯誤不替換世界、選取任意既有存檔；SAVE_PICKER_UI_SMOKE_PASS及本次獨立程序重啟 |
| 4-07 | 暫停、單步、10Tick、1／2／4倍；TIME_UI_SMOKE_PASS。使用者已確認離線不推進 |
| 4-08 | 以下雙程序端到端驗收通過，使用本機Windows Godot4.3 .NET，不宣稱已確認最終發行平台 |

## 雙程序驗收

第一個Godot程序經與介面相同的建立／操作方法，建立命名玩家、選擇NPC觀察但不改控制權、在鎮購買／出售藥材、發出旅行並在旅行中存檔。輸出保存路徑、當前世界摘要與由該快照預算4Tick後的摘要至全新manifest檔，然後退出。

第一程序結束後，另啟第二個Godot程序，由FileSelected訊號載入該存檔。確認摘要完全相同且暫停；向時間入口提供86400秒仍不推進，再手動續跑4Tick，確認抵達藥圃且摘要與第一程序預算結果相同。這同時驗證NPC自主決策、亂數、旅行與世界狀態續接。

```powershell
dotnet build CultivationGame.sln --no-restore
dotnet run --project tests/Acceptance --no-restore
dotnet build src/Presentation/CultivationGame.csproj --no-restore
$enginePath = '.tools/godot43/Godot_v4.3-stable_mono_win64/Godot_v4.3-stable_mono_win64_console.exe'
$manifestPath = Join-Path (Get-Location) ('.tools/e2e-' + [guid]::NewGuid().ToString('N') + '.json')
& $enginePath --headless --path src/Presentation -- --e2e-create $manifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $enginePath --headless --path src/Presentation -- --e2e-resume $manifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($check in @('--player-demo-smoke','--map-smoke','--world-events-smoke','--gameplay-smoke','--save-picker-smoke','--time-smoke')) {
    & $enginePath --headless --path src/Presentation -- $check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

本次實際結果：核心 **99/99 passed**；核心與Godot建置均0警告／0錯誤。雙程序分別 **E2E_CREATE_PASS**、**E2E_RESUME_PASS**，退出碼0。六組UI皆PASS、退出碼0。測試以獨立Godot程序執行，不只是重新建立SimulationEngine。

## 變更與限制

本次僅於 `src/Presentation/Main.cs` 新增可重跑的E2E驗收入口，不新增玩法或改核心；同步更新README、需求狀態與唯一進度表。manifest與成功存檔保留供稽核，manifest採CreateNew，不覆寫既有檔案。

人工點擊、原生選檔對話框視覺、排版／解析度、效能、長期經濟平衡、正式封裝與其他平台未驗證。既有文件所述「跨程序重啟未驗證」已由本次雙程序測試補足，但不代表人工操作或最終發行驗收。Demo關閉前仍須手動存檔；不會自動儲存，也不補算離線時間。

正式地圖／角色切換待確認，既有候選數值不升格為正式規格。後續依原計畫為階段5效能與LOD；本次沒有開始階段5或加入額外功能。
