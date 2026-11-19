# 首次專項測試摘要

2026-11-04，首次 `--construction-lifecycle-only` 結果為 2/4：

- `R2 construction reserves organization funds and completes exactly once`：`Missing reservation`。
- `R2 construction cancel refunds once and invalidates completion`：`Cancel/refund wrong`。
- 暫停後續建、存讀與缺失預留拒絕案例通過。

調查發現上述兩個測試比較的是新局初始錢財，但世界在建造開始前已經歷收購收入。測試改為以建造開始前的餘額比較，沒有修改遊戲收入規則。原始終端輸出未另存檔；此為依當次執行結果記錄的摘要，最終詳細結果另見 `construction-lifecycle-only.log`。
