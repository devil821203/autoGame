> 2026-10-29來源歸位更新：使用者已授權寫回，三份正常檔名現為有效編譯來源，三份DESKTOP副本保留但排除編譯；原檔與副本寫入前均已備份。以下舊來源選擇／不可讀敘述為歷史盤點，現況見[歸位與驗收](../驗收/來源管理/Domain來源歸位_驗收.md)。

# Domain來源版本差異

日期：2026-10-29。僅唯讀比較，不修改來源或Domain.csproj。

一般環境讀取Models.cs／CharacterProgression.cs仍遭拒，但本次以提高權限的唯讀命令已成功讀取完整內容。因此「完全無法讀取」不再是現況；尚未將這兩份原檔補入R0來源備份，Git／完整回退仍未建立，不據此宣稱R0全部完成。

## Models.cs與Models-DESKTOP-K1FV25D.cs

| 區域 | 原檔 | 現行編譯副本 |
| --- | --- | --- |
| CharacterState | 早期身分、志向、境界、攻防、社交 | 另有TestProfession、MortalCultivation、CurrentAttributeModifiers、重傷、製作裝備／攻防加成／破損旗標、LifeSkills、Mindset、Stamina、HexPosition、DemoLocationId與MonsterKind |
| Aspiration | 均衡／求財／丹道／修煉 | 另有Combat；保留歷史Alchemy列舉不代表新模式恢復煉丹 |
| ActionKind | 截至RewardMember | 原有值順序保留，再增加繼任、旅行、副本、宗門工作／貢獻、組隊、清剿、建造、休息、棋盤、醫療製藥鍛造、三功、服務、聚落、分舵、市場、人力及Wait |
| EconomyRules | 早期價格、工期、熟練與決策參數 | 增加GrowthUtilityMultiplier與KeepGrowthPill |
| OrganizationState | 無組織種類欄位 | 增加Kind，預設Sect |
| ActionIntent | 截至OrganizationId | 增加StockReservation、BoardTurn、ServiceMaterials、SettlementBuildPosition、SettlementShopId |
| 通知 | 無WorldNotification及Notifications集合 | 有定向通知模型與集合 |
| WorldState | SchemaVersion預設1，早期角色／帳本／任務／戰鬥／社交 | CurrentSchemaVersion=3；新增貨幣、聚落、組織發展／人力／稅務／收據、生活系統、時間AP／自主排程、棋盤、凡人修為／功法、體力、六角地圖、效能LOD、組隊／清剿、道具／志向、收購／俸祿／宗門設施等狀態 |
| Inventory | 每次掃描Ledger | 增加BeginFrame與LedgerBalanceIndex快取；部分特殊帳戶仍掃Ledger，不另造獨立資產餘額 |

原檔主要型別與既有欄位仍可在副本找到；差異包含擴充及版本／查詢行為修改，不能視為兩份等價備份。直接改編原檔會缺少大量現行引用的型別成員，不能只移除排除設定。

## CharacterProgression.cs與CharacterProgression-DESKTOP-K1FV25D.cs

列舉、AdvancementRules、CharacterStats，以及Threshold／MethodLevel／GrowthPerPill相同。差異集中在Stats的有AdvancementRules分支：

- 原檔只以`EquippedWeaponId == rules.WeaponId`認定舊式武器裝備。
- 副本增加`!actor.LegacyWeaponBroken`，並接受`legacy-gear:`實例ID，支援每件耐久與破損不給加成。
- 副本攻擊另加`EquipmentAttackBonus`，防禦另加`EquipmentDefenseBonus`，銜接製作裝備。
- `rules is null`分支兩者都直接回BaseAttack／BaseDefense；本次只記錄，不順便改公式。

回編原檔會遺失製作裝備攻防與舊式裝備耐久判斷，不只是檔名不同。

## 額外核對EconomyModels

原EconomyModels.cs目前讀到內容為空；現行副本包含MarketListing／MarketQuote／TradeReceipt、任務與交付模型，以及依庫存調價的MarketPricing。這也是Domain.csproj第三份排除來源。未更動空檔或副本。

## 建議

保留目前有效編譯來源。之後若要整理正常檔名，先補齊三組來源備份／hash，再明確核准以哪份內容歸位，同一次變更更新Compile排除規則並重跑完整回歸；不能讓兩套同名類別同時編譯。本次未實作歸位，未修改玩法／設定／存檔，未重跑編譯或驗收。
