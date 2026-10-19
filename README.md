# SonarPlugin（Sonar）

[SonarDistrib](https://github.com/FFXIV-Sonar/SonarDistrib) 的台服移植版。偵測並提醒危險標的（狩獵怪物）與 F.A.T.E.，顯示於清單與地圖上；開啟連線後也能接收其他 Sonar 使用者的跨伺服器回報。

## 連線與隱私

- **「連線 Sonar 伺服器」總開關預設關閉**（設定「一般」分頁）。關著時完全不連線、不送出任何資料，只顯示你自己遊戲裡偵測到的危險標的與 F.A.T.E.。
- 打開後才會連上 Sonar 伺服器，送出角色名稱、原始世界、座標、硬體識別碼與角色 ID 的雜湊，並接收其他使用者的回報；不會送出帳號 ID 的雜湊。
- **「全域回報貢獻」預設也關閉**：連線時是否把自己遇到的危險標的／F.A.T.E. 回報出去，要另外打開（也可用下面的 `/sonaron` 等指令切換）。

## 功能

- 危險標的與 F.A.T.E. 追蹤：清單顯示距離、位置等資訊（連線時含其他玩家的回報）
- 音效與訊息提醒
- 主視窗、追蹤器視窗、設定視窗各自獨立
- 玩家人數統計

## 指令

- `/sonar`：開啟主視窗
- `/sonarconfig`（`/sonarcfg`）：開啟設定
- `/sonartracker`：開啟追蹤器視窗
- `/sonarsupport`：開啟支援視窗
- `/sonaron`（`/sonarenable`）／`/sonaroff`（`/sonardisable`）／`/sonartoggle`：開啟／關閉／切換全域回報貢獻（後面加 `hunt` 或 `fate` 只切換該類）
- `/sonarerror`：顯示錯誤資訊

## 台服版的修改

- 新增「連線 Sonar 伺服器」總開關，預設關閉；「全域回報貢獻」預設改為關閉。
- 連線時不再送出帳號 ID 的雜湊。
- 修正開啟列舉下拉選單時遊戲當機的問題。
- 伺服器網址清單的背景更新不會因一次逾時就整場停掉；聊天訊息依序從 framework 執行緒送出。
- 載入設定後真的套用存好的 Sonar 記錄等級。
- 多處降低記憶體用量。
- 介面繁體中文化（用台服官方的資料片名稱與用語）。

原作者：[FFXIV-Sonar/SonarDistrib](https://github.com/FFXIV-Sonar/SonarDistrib)
