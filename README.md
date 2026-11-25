# SonarPlugin（Sonar）

[SonarDistrib](https://github.com/FFXIV-Sonar/SonarDistrib) 的台服移植版。偵測並提醒危險標的（狩獵怪物）與 F.A.T.E.，顯示於清單與地圖上；開啟連線後也能接收其他 Sonar 使用者的跨伺服器回報。

## 連線與隱私

- **「連線 Sonar 伺服器」總開關預設關閉**（設定「一般」分頁）。關著時完全不連線、不送出任何資料，只顯示你自己遊戲裡偵測到的危險標的與 F.A.T.E.。
- 打開後才會連上 Sonar 伺服器並接收其他使用者的回報。連線期間會送出：角色名稱、原始世界、角色 ID 的雜湊，目前所在的世界、地圖、副本區與座標，伺服器配發給這份安裝的用戶端 ID，以及版本資訊（Sonar、插件、遊戲、Dalamud）與回報貢獻設定。
- 不會送出硬體識別碼、作業系統版本，也不會送出帳號 ID 的雜湊。
- **「全域回報貢獻」預設也關閉**：連線時是否把自己遇到的危險標的／F.A.T.E.（位置、HP、附近玩家人數）回報出去，要另外打開（也可用下面的 `/sonaron` 等指令切換）。

## 功能

- 危險標的與 F.A.T.E. 追蹤：清單顯示距離、位置等資訊（連線時含其他玩家的回報）
- 音效與訊息提醒
- 主視窗、追蹤器視窗、設定視窗各自獨立
- 玩家人數統計

## 指令

- `/sonar`：開啟主視窗
- `/sonarconfig`（`/sonarcfg`）：開啟設定
- `/sonartracker`：開啟追蹤器視窗
- `/sonarsupport`：開啟支援視窗（上游 Sonar 團隊的表單；只有你自己按下送出時，才會送出填寫的內容）
- `/sonaron`（`/sonarenable`）／`/sonaroff`（`/sonardisable`）／`/sonartoggle`：開啟／關閉／切換全域回報貢獻（後面加 `hunt` 或 `fate` 只切換該類）
- `/sonarerror`：顯示錯誤資訊

## 台服版的修改

- 新增「連線 Sonar 伺服器」總開關，預設關閉；「全域回報貢獻」預設改為關閉。
- 連線時不再送出硬體識別碼、作業系統版本與帳號 ID 的雜湊；設定裡的說明框列出實際會送出的項目。
- 修正開啟列舉下拉選單時遊戲當機的問題。
- 伺服器網址清單的背景更新不會因一次逾時就整場停掉；聊天訊息依序從 framework 執行緒送出。
- 載入設定後真的套用存好的 Sonar 記錄等級；設定檔讀不到而要重置成預設值前，先在原檔旁留一份備份。
- 多處降低記憶體用量。
- 介面繁體中文化（用台服官方的資料片名稱與用語）。

## 安裝

1. 在遊戲內輸入 `/xlsettings` 開啟「Dalamud 設定」，到「試驗性功能」分頁的「第三方插件倉庫」加入
   `https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
   並啟用、儲存。
2. 輸入 `/xlplugins` 開啟「插件管理員」，搜尋「Sonar」安裝。

同一個插件只裝這裡的版本，不要同時裝上游版。

原作者：[FFXIV-Sonar/SonarDistrib](https://github.com/FFXIV-Sonar/SonarDistrib)

授權：Proprietary，上游保留所有權利（見 LICENSE）
