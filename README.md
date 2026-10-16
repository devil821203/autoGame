# 自主修仙世界

階段 0 骨架：純 C# 固定 Tick 模擬、玩家／NPC 共用資源轉移規則、資源帳本、延遲行動、JSON 存讀檔與 Godot 除錯面板。

**狀態：已實作，尚未通過執行驗收。** 本機只有 .NET Runtime，缺少 SDK；Godot 未在 PATH 中找到。2026-10-16 嘗試 `dotnet build src/Headless/Headless.csproj`，因 `No .NET SDKs were found` 失敗。未安裝任何工具或依賴。

## 環境與啟動

- 核心與驗收入口：.NET 8 SDK，無第三方套件。
- 展示層：Godot 4.3 .NET 版；專案固定使用 `Godot.NET.Sdk/4.3.0`。其他引擎版本未驗證。首次建置展示層可能需要還原該 SDK。
- 核心 solution 不包含 Godot 專案，因此驗收核心不需要引擎。

在專案根目錄執行：

```powershell
dotnet build CultivationGame.sln
dotnet run --project tests/Acceptance
dotnet run --project src/Headless -- --seed 12345 --ticks 10
```

驗收入口是無套件的可執行測試程式，失敗時回傳非零退出碼；請使用上述 `dotnet run`，而非 `dotnet test`。預期輸出 `12/12 passed`，**尚未驗證**。本機已嘗試此驗收指令，同樣因缺少 SDK 而無法啟動。7 個專案 XML 與專案參照路徑已通過靜態檢查；此檢查不代表 C# 編譯通過。

Headless 會在新世界提交玩家與測試 NPC 各一筆延遲轉移，印出 Tick、排程數、帳本筆數及 SHA256。同種子、相同有序輸入應得到相同摘要；預設 10 Tick 後應無待完成排程，帳本應為 7 筆。這些預期結果均**未驗證**。

存檔與續跑：

```powershell
dotnet run --project src/Headless -- --seed 12345 --ticks 1 --save saves/checkpoint.json
dotnet run --project src/Headless -- --load saves/checkpoint.json --ticks 9
dotnet run --project src/Headless -- --seed 12345 --ticks 10
```

後兩次摘要預期一致。存檔只允許建立新檔；路徑已存在時會報錯，請另選檔名。讀檔不會重新提交展示用行動。

Godot 展示層：

```powershell
dotnet build src/Presentation/CultivationGame.csproj
godot --path src/Presentation --editor
```

亦可透過 Godot .NET 專案管理員匯入 `src/Presentation/project.godot`，建置後按 F6/F5 啟動。若執行檔不在 PATH，使用實際完整路徑。展示層建置與操作皆**未驗證**。

面板初始暫停，提供繼續／暫停、單步、玩家轉移 10 靈石、另存新檔、讀取本次存檔。NPC 測試意圖已排隊；玩家按鈕只操作指定角色。行動於下一 Tick 接受，再經兩個 Tick 完成。存檔位置顯示於面板，使用 Godot `user://saves`；讀取後暫停。

## 結構

```text
CultivationGame.sln             純 C# 核心與驗收入口
src/Domain/                    模型、意圖、帳本查詢
src/Simulation/                Tick、驗證、原子提交、排程、亂數、完整性檢查
src/AI/                        決策介面與明確標示的流程測試來源
src/Persistence/               IWorldStore、JSON 新檔儲存與載入
src/Headless/                  命令列可重現情境
src/Presentation/              獨立 Godot .NET 專案、場景與面板
tests/Acceptance/              12 組無外部套件驗收案例
document/階段0_架構與驗收.md     決策、資料流、限制與後續工作
```

產品需求仍以 `document/修仙遊戲_開發需求規格.md` 及本次已確認範圍為準。階段 1 尚未開始。
