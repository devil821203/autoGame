# 宗門設施 smoke 首次失敗摘要

2026-11-04 執行 `--sect-facility-smoke` 首次退出碼 1：`Work UI ownership or points failed`。加入診斷後重跑仍失敗，輸出為 `contribution=4, herbs=10, initial=40`。

經核對，既有測試收購點在測試期間收走多餘藥草，宗門維持預留量；採藥貢獻正常。測試原本以初始庫存加一比較，沒有計入交易。修正為查驗採藥入庫與帳本淨變化後通過，沒有修改遊戲交易或採藥規則。原始終端輸出未另存，本檔為依當次輸出記錄的摘要；成功輸出見 `godot-sect-facility.log`。
