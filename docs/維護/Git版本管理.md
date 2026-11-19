# 重構版本管理

2026-10-29使用者授權建立`rebuild_Codex`管理重建內容。沿用既有Git倉庫，由`feature/rebuild20261029`的目前HEAD建立分支；未重建倉庫、不覆寫歷史、不推送遠端。

Git實際路徑：`%LOCALAPPDATA%/Atlassian/SourceTree/git_local/cmd/git.exe`，本輪版本2.51.0.windows.1；PATH尚未配置不代表未安裝。代理寫入.git需提高權限，未更改系統權限。

本次基線收錄正常檔名來源歸位、編譯設定、規格整合與R0文件、設定／測試的既有已追蹤版本，以及可供稽核的測試證據。私人存檔、工具、編譯快取、暫存日誌與大型ZIP／checkpoint／archived-events產物留本機，不移除。Git不包含私人存檔的回退，存檔仍須另行備份。

之後每個獲授權階段只提交相關檔案，先檢查`git status`、`git diff`及暫存內容，再編譯／測試並提交；保留失敗紀錄，禁止擅自reset --hard、clean、強制推送或刪分支。完成一階段後停下，等使用者確認下一階段。未追蹤的使用者修改不混入下一個功能提交。

回復先在隔離目錄使用指定commit／tag檢出與驗證；不得直接覆蓋目前工作目錄。需要還原共享歷史時優先新增revert提交，實際回復動作另行確認。R1仍未授權；本次沒有修改遊戲規則。

## 本輪結果

- 分支rebuild_Codex，基線commit為31b803f，tag為r0-codex-baseline-20261029；提交前為4dbf74f。未推送origin。
- 隔離worktree位於.tools/r0-git-restore-check，detached檢出基線；git status為乾淨。原工作目錄未被還原。
- 隔離建置首次因NuGet線上弱點資料NU1900失敗，保留[失敗紀錄](../../document/evidence/git-r0-restore-build-20261029.log)；以命令列-p:NuGetAudit=false和--ignore-failed-sources使用本機套件重試，[建置通過](../../document/evidence/git-r0-restore-offline-build-20261029.log)，0警告0錯誤。不將離線驗證稱為已完成線上弱點檢查。
- 未新增遊戲邏輯，沿用歸位時353/353結果；本輪沒有重跑完整測試或Godot。R0的Git／受控來源還原缺口已解除，但Godot退出崩潰仍未解決。
- git diff --cached --check曾提示既有來源及證據檔尾端空白行；為保留副本寫回與證據原貌，未清理備份內容。這不影響編譯。
- 版本庫排除的存檔／工具／大型產物沒有被備份到Git；本機Git也不等於異機備援。

2026-10-30：使用者已授權R1並選整數秒，本輪核心／修練切片在rebuild_Codex分次提交；R1仍測試中，R0標籤保留，不新增R1完成標籤，不推送遠端。19/19、372/372、Godot建置及5次UI通過，詳見R1文件。

2026-10-31：使用者明確指定R2，首批15/15、387/387及6次UI通過；仍在rebuild_Codex分次保存，不將R1／R2整體標為完成，不推送遠端。
