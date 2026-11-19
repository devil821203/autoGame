# R4 內容版本與 SQLite 快照

2026-10-31。使用者已授權開始R4。本輪沿用完整WorldState／IWorldStore，加入內容識別與交易式SQLite；不重建世界資料模型、不刪JSON或遷移舊檔，不進R5。R1～R3尚未完成的活動轉接及知識邊界仍保留。

## 功能與使用方式

Godot情境區選「R4內容／SQLite測試（重建）」建立明確啟用的新局；玩家可透過「另存SQLite新檔」保存，或沿用「另存新檔」保存JSON。「選取存檔」及「讀取本次存檔」依副檔名載入JSON／SQLite；讀取失敗不更換目前engine、暫停狀態或上次路徑。重建新檔會保留R4模式並重新載入config、重設進度。

SQLite目前只接受含R4內容識別的新世界，舊JSON仍可正常讀取／續存JSON，不會補入今天的config或自動宣稱已具備內容版本。沒有新增一鍵轉換舊檔。

## 內容與版本

- `config/content/manifest.json`：`SchemaVersion=1`、`ContentVersion="r4-1"`，屬工程格式／相容性識別，不是遊戲數值。只接受目前實作支援的版本；升版須同步增加讀取支援與驗收，不能隨意改字串後期待舊引擎接受。
- `GameConfiguration.CaptureContent`只在新R4局讀取config；驗證JSON，對全部config JSON原始位元組計SHA-256，以相對路徑排序保存在`Time.Content.FileHashes`。支援原有UTF-8 BOM。清單是整套安裝內容的指紋，並非宣稱每個測試範本都被目前情境使用。
- 完整內容識別由版本與檔案雜湊共同構成；同名版本但不同config雜湊不可視為相同內容。換行／BOM也會改變原始檔雜湊。新建期間應避免同時修改config；本輪不做跨檔案系統鎖定。
- 讀取時只驗證已保存的識別／格式與快照，不回讀config、不用今天的數值替換世界規則；保存原有角色、庫存、帳本、RNG、工作、預留及AI資料。
- JSON外部格式7保護R4欄位，格式1～6若夾帶R4內容識別會拒絕；原有1～6讀取政策保留。SQLite `application_id=CULT`、`user_version=1`，內含JSON格式7的完整快照；內部WorldState仍為原schema3。

## SQLite provider 評估

本機NuGet快取沒有SQLite套件。Microsoft.Data.Sqlite／SQLitePCLRaw是後續可選provider，但需增加套件與原生部署。本輪以Windows優先的既有需求，選用已存在的System32 `winsqlite3.dll`，自行封裝少量SQLite C API；不下載／安裝依賴、不重新散布原生DLL。只有Persistence知道原生介面，Domain／Simulation不依賴SQLite或Godot。

系統provider已實測為 **SQLite 3.53.4**；每份DB保存寫入provider版本。只從System32載入，使用UTF-8及參數綁定；中文路徑與含SQL符號的人名已驗證。使用系統庫的代價是版本由作業系統維護，不能宣稱已固定所有玩家的runtime；目前只驗證本機Windows x64，其他平台不提供此adapter，JSON仍保留。跨平台封裝不列為本輪已完成內容。

參考：[Microsoft系統SQLite說明](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)、[SQLite連線](https://www.sqlite.org/c3ref/open.html)、[執行與交易](https://www.sqlite.org/c3ref/exec.html)、[關閉連線會回滾未完成交易](https://www.sqlite.org/c3ref/close.html)。本輪沒有改動套件授權或帶入新的散布檔。

## 原子性與失敗處理

`SqliteWorldStore.Save`先驗證快照，建立同目錄唯一私有暫存檔，使用DELETE journal、synchronous FULL、BEGIN IMMEDIATE，將內容版本、內容指紋、完整payload及payload SHA-256寫在同一交易，COMMIT後關閉並flush，再以不覆寫的File.Move發佈目標。

中途例外會關閉連線、回滾未提交交易並移除本次暫存／journal；既有目標不曾以寫入模式開啟。即使同名目標已存在也只會失敗，不能默默覆寫。程序在發佈前被終止可能留下`.tmp`檔，但它不會出現在正式副檔名選檔清單，也不會當作成功存檔。

Load以read-only及讀取交易開啟，檢查application／schema、quick_check、唯一快照、payload雜湊、內容版本與指紋，再沿原SaveMigration／WorldIntegrity驗證。索引／優先佇列由既有載入與Engine流程重建，不序列化委派或Node。

本輪驗證例外注入與實際檔案保留，並非斷電、磁碟硬體故障或OneDrive多機衝突實驗；雜湊用於一致性檢查，不是防止有意改檔的簽章。未知DB／內容版本明確拒絕，不嘗試降版或原地改檔。未來格式遷移應另存新檔並保留原檔，本輪沒有啟用遷移器。

## 實際修改

| 模組 | 主要檔案 |
| --- | --- |
| 內容快照 | `src/Domain/ContentManifest.cs`、`WorldTime.cs`；`src/Simulation/ContentProfile.cs`、`GameConfiguration.cs`、`WorldIntegrity.cs`、`NewGameFactory.cs` |
| 持久化 | `src/Persistence/NativeSqlite.cs`、`SqliteWorldStore.cs`、`WorldStores.cs`、`SaveMigration.cs`；AssemblyInfo只供Acceptance注入失敗驗收 |
| 操作介面 | `src/Presentation/Main.Sqlite.cs`、`Main.TestScenarios.cs`、`Main.cs` |
| 設定與測試 | `config/content/manifest.json`、`config/README.md`、`tests/Acceptance/SqlitePersistenceTests.cs`、`Program.cs` |

沒有變更AP、配方、價格、經驗、AI權重或資料所有權。

## 驗收紀錄

```powershell
dotnet run --project tests/Acceptance --no-restore -- --sqlite-only
dotnet build src/Presentation/CultivationGame.csproj --no-restore
dotnet run --project tests/Acceptance --no-build --no-restore
# Godot參數尾端：-- --sqlite-smoke
```

證據：`document/evidence/r4-persistence-20261031/`。最終專項8/8、Godot建置0警告0錯誤。首輪0/8是manifest捕捉未處理既有JSON BOM，修正後8/8；失敗日誌保留。驗收覆蓋真SQLite／Unicode／參數綁定、活動／事件版本／AI待決策／RNG一致續跑、有償服務escrow與一次結算、交易前／提交後失敗、禁止覆寫、缺檔／損壞／未知schema及內容版本、JSON相容、設定快照與新局重建。

完整回歸401/401通過；最終Godot SQLite兩次、JSON選檔、新存檔、R3事件決策及棋盤各一次，共6/6成功標記且exit0。初輪5/6的新檔smoke錯誤，已在R3來源ee64d81重現；只修正預期範本須套用原有MedicineOnlyProfile，不改重建規則，失敗／對照／最終日誌均保留。來源提交`c440474`已在`.tools/r0-git-restore-check`隔離還原，Godot建置0警告0錯誤，見`git-restore-build.log`；未覆蓋主工作目錄、未推送遠端。沒有市場訂單／新控制資格的假資料驗收；它們尚屬R5／R6範圍，現有ControlledCharacterId及全世界已實作欄位則隨快照保存。未驗證人工桌面操作、Godot 4.7、跨平台與效能容量。
