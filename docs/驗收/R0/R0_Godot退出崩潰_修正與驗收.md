# R0 Godot退出崩潰修正與驗收

2026-10-29。範圍：Godot 4.3 mono本機退出，不改模擬、AP、經濟、存檔或R1排程。

## 重現與定位

scenario-catalog顯示成功後signal11／exit -1073741819；[本輪修正前](../../../document/evidence/r0-exit-fix-20261029/before.log)已重現。未把SMOKE_PASS等同程序exit0。

先嘗試移除三個面板_ExitTree內RichTextLabel.Clear，單次看似改善，但[五次重測](../../../document/evidence/r0-exit-fix-20261029/repeat-results.json)仍3次崩潰；[Fallback文字後端](../../../document/evidence/r0-exit-fix-20261029/fallback-results.json)與[先釋放場景再退出](../../../document/evidence/r0-exit-fix-20261029/shutdown-results.json)也未解決。這些實驗修改已撤回，原退出流程與清理保留，不新增延後關閉或強制終止。

五個自繪元件直接從C#取得ThemeDB.FallbackFont。Godot官方倉庫[issue #123521](https://github.com/godotengine/godot/issues/123521)有4.3 C#引擎單例字型與退出問題的相近使用者回報，但該議題並非維護者確認的本案根因；本次以本機前後重測作證據。

## 最終修改

新增PresentationFonts.Create，每個自繪元件持有自有SystemFont，使用Microsoft JhengHei／Noto Sans CJK TC／Arial候選字型。DemoMapPanel、HexMapCanvas、SectCourtyard、SiteCourtyard與ValueChart改用自有資源，避免C#繪圖路徑持有引擎擁有的FallbackFont單例。不升級Godot、不強制退出、不隱藏返回碼。

字型屬呈現資源，不是遊戲平衡參數；未改config或角色狀態。字形與文字寬度可能隨系統可用字型略有差異，人工視覺待驗證。

## 驗證

- [最終建置](../../../document/evidence/r0-exit-fix-20261029/final-build.log)：0警告0錯誤。
- [第一輪自有字型重測](../../../document/evidence/r0-exit-fix-20261029/font-results.json)：8/8程序exit0；當時尚未編回原ExitTree清理，僅作定位證據。
- 最終編譯已恢復原ExitTree清理，使用[可重跑腳本](../../../tests/PresentationExit/Run.ps1)驗證scenario-catalog、character-follow、action-driven、board-battle、character-attributes，各3次；[最終結果](../../../document/evidence/r0-exit-fix-20261029/final-repeat/results.json)實際15/15通過、全exit0。腳本要求exit0及SMOKE_PASS，遇SMOKE_FAIL／CrashHandlerException判失敗。
- [完整核心驗收](../../../document/evidence/r0-exit-fix-20261029/acceptance.log)：353/353，exit0；此次無模擬邏輯改動，核心驗收在退出定位期間完成，不宣稱測過其後新增的呈現字型程式。
- 屬性驗收在一般沙箱曾存讀失敗；在允許user目錄存讀環境原斷言通過。本輪未放寬測試或修改Load；測試沿用GUID新存檔，不覆寫使用者檔案。
- 腳本首次直接呼叫被PowerShell執行政策阻止；重試僅以單次PowerShell程序ExecutionPolicy Bypass執行本專案可檢閱腳本，未變更系統政策。

Godot在一般沙箱可能報憑證儲存區錯誤，未將本機測試當作TLS驗證。桌面人工關閉、縮放與FPS未驗證，不保證所有機器及後端；以本輪重複headless流程為驗收範圍。失敗／中間結果均保留，不覆蓋舊證據。
最終一般啟動20幀後退出另測3次皆exit0，見[正常退出結果](../../../document/evidence/r0-exit-fix-20261029/final-normal-results.json)。R0限定基線驗收已通過；最新版本Git還原建置另記進度，不進R1。

修正提交3604ba6已於隔離worktree重新檢出，[還原後編譯](../../../document/evidence/r0-exit-fix-20261029/restored-final-build.log)0警告0錯誤。原工作目錄未覆蓋，未推送遠端。
