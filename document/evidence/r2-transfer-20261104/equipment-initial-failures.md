# 獨立裝備在途測試的首次失敗摘要

2026-11-04 新增獨立裝備在途測試時，第一次編譯因測試將 `int?` 耐久當成 `int` 而失敗；修正測試型別後可編譯。首次執行為 5/6：裝備已移到 escrow，但 `LifeSystemIntegrity` 只計算角色與組織帳戶而回報 `Invalid equipment stats or ownership`。

已調整為只把合法、執行中的 R2 轉移 escrow 納入獨立裝備持有量，並在製作裝備／舊制獨立武器測試中檢查身分與耐久；製作裝備另檢查在途 JSON／SQLite 續跑。原始終端輸出未另存，此為依執行結果記錄的摘要；最終詳情見 `scheduled-transfer-lifecycle-only.log`。
