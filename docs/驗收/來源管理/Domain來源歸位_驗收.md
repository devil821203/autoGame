# Domain來源歸位

2026-10-29使用者明確授權「將副本內容寫回原檔」。範圍為既有三組Domain來源，不更改玩法、config、存檔或R1排程核心。

## 實際變更

- Models-DESKTOP-K1FV25D.cs內容逐位元組寫回Models.cs。
- CharacterProgression-DESKTOP-K1FV25D.cs內容逐位元組寫回CharacterProgression.cs。
- EconomyModels-DESKTOP-K1FV25D.cs內容逐位元組寫回原為空檔的EconomyModels.cs。
- Domain.csproj改為編譯上述正常檔名，排除三份DESKTOP副本；副本仍留在原處，避免同名類別同時編譯。

寫入前已先保存六份來源與舊Domain.csproj，並記錄[備份hash](../../../document/evidence/domain-source-restore-20261029/before.json)。備份目錄為`document/evidence/domain-source-restore-20261029/`；內含原檔舊內容，不是把副本重新命名冒充原檔備份。寫入後三組原檔／副本SHA-256逐一相同。

[MSBuild Compile清單](../../../document/evidence/domain-source-restore-20261029/compile-items.json)提供歸位後有效來源；其他專案透過ProjectReference引用Domain，不需各自改來源。

## 驗證及限制

一般沙箱建置／測試先失敗：CS1504，Models.cs與CharacterProgression.cs存取被拒，見build.log及acceptance.log。提高權限環境成功讀寫及建置，build-elevated.log為0警告0錯誤。這是兩種執行環境的差異；未修改檔案ACL、OneDrive或系統安全設定，不保證一般沙箱已可編譯。

完整驗收acceptance-elevated.log實際353/353通過、exit0；Godot三主情境操作檢查另記ui.log及ui-elevated.log：兩次皆印SCENARIO_CATALOG_UI_SMOKE_PASS，但程序隨後signal 11崩潰、exit -1073741819；提高權限重試亦同，故整體啟動／退出驗收失敗，原因未確定，不以成功標記冒充exit0。未重跑4日自主修練或FPS，不套用舊結果冒充本輪。

R0的舊來源讀取／備份及有效來源歸位已取得證據，但Git／完整倉庫回退仍未建立；R0維持測試中，不開始R1。舊R0紀錄與版本比較保留歷史，後續來源以本文件及Domain.csproj為準。回退不得只還原原檔而忘記同時還原Compile排除設定；任何回退前先保存當前改動。
