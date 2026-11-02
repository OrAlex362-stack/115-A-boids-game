# boids_game：多人 Boids 競技場

10 位學生各寫一個 Boid 行為 class，老師的主程式負責場地、食物、出生、擊殺、縮圈與顏色，存活最多的隊伍獲勝。
學生用 VS Code 編輯 `.cs`，GH 的 C# Script 用 `#load` 載入，用 Git 共同上傳。

## 資料夾

| 路徑 | 誰改 | 內容 |
|---|---|---|
| `core\Contracts.cs` | 老師 | 學生可用的介面與資料：`IBrain`、`SelfInfo`、`Neighbor`、`World`、`Obstacle`、`RelationTable`、`Steering` |
| `core\Arena.cs` | 老師 | 競技場：運動、碰撞擊殺、食物、五芒星、縮圈、防錯、顏色 |
| `core\Roster.cs` | 老師 | 用反射找出所有 `Boid_` class（名稱以 `Boid_template` 開頭的不參賽） |
| `host\GameHost.cs`、`host\host_template.cs` | 老師 | `GameHost` 包住播放、排程與畫面（`Setup`／`Run`／`Draw`）；`host_template.cs` 是貼進 GH 的薄樣板，只剩 `#load` 與三行呼叫。`host\host_script.cs` 是 `gen_host.py` 產生的完整腳本，不手改、不提交 |
| `students\boid_{名字}.cs` | 學生 | 自己的 `public class Boid_{名字} : IBrain` |
| `students\boid_template.cs` | 老師 | 學生範本：列出所有可用資料與函式，不參賽 |
| `docs\AI_PROMPT.md` | 學生 | 貼給網頁 GPT／Gemini 的提示 |
| `tools\gen_host.py` | 大家 | 產生 `host_script.cs`；老師加 `--check` 先檢查學生檔 |
| `tools\check\`、`examples\ParamBrain.cs` | 老師 | `--check` 的試跑程式與對手 |
| `Boids_Game.gh` | 老師 | GH 定義（C# Script 元件與輸入） |

## 規則

- **場地與時間**：2D（Z 恆為 0），以原點為中心的正方形，半邊長 30。一回合預設 3 分鐘（30 步／秒 = 5400 步），時間到顯示勝者（存活最多；同數量比擊殺數）。開賽時有 2 隊以上、場上只剩 1 隊時**提早結束**。
- **三階段**（百分比寫在 `Arena.StablePercent` 20、`ShrinkPercent` 50）：
  1. 穩定期：場地不縮，8 個隨機矩形障礙物（與座標軸平行，全寬／高 3～10，種子固定）。
  2. 縮小期：場地線性縮到半邊長 8；中心跑到場外的障礙物、食物、五芒星移除。
  3. 決戰期：場地不再縮，食物不再出現。
- **隊伍**：10 隊、每隊 50 隻、全場上限 500（滿了不能出生）。速度上限 0.5、轉向力上限 0.05，全隊相同。同隊互相分離（引擎處理）。
- **擊殺**：異隊距離 < 1 時，A 的前方 60 度碰到 B 的正後方 60 度，A 殺死 B；正面、側邊互相彈開（只交換 60% 速度）；背對背不處理。同一步的擊殺依步初狀態同時結算。
- **食物**：出現在**存活最少（不是 0）的隊**平均位置周圍 10 單位內（扶弱）；吃到就複製成兩隻。
- **五芒星**：很少出現（預設每步 0.4%，場上最多 2 顆，階段 3 不出現）；吃到無敵 100 步（殺不死，照樣能殺人）。
- **轉向手感**：引擎統一平滑轉向（`SteerBlend`），靠牆 7、靠障礙物 4 單位內且還在衝時輕輕推回。不影響擊殺規則。
- **身份**：`neighbor.Relation` 為友軍／敵軍／不相關，開賽前可在 `Declare` 設定；只是行為標籤，不改變擊殺規則。
- **顏色**：由老師分配，學生不能選。色盤是離畫面固定色與彼此都最遠的 10 個高對比色（兩兩 RGB 距離 ≥ 125），依種子洗牌後分給沒被淘汰的隊。
- **種子**：預設 `Arena.GlobalSeed = 20261103`；同種子＋同學生檔，結果完全相同。每隊的起點與 `self.Rng` 只由「種子＋隊名＋步數」決定，別隊出錯不影響。

## 防錯

比賽中出錯的隊伍被**淘汰**（整隊移除，`info` 寫原因，其他隊照常；出錯那一步會扣掉壞隊重算）。

| 錯誤 | 在 GH 比賽中 | `--check` |
|---|---|---|
| 編譯錯誤、兩人同名 class | ⚠️ 整支 script 失敗，全班不能跑 | 排除該檔 |
| `Decide` 丟例外、回傳 NaN／無限大、`Declare` 丟例外 | 淘汰 | 排除該檔 |
| 太慢（每隊每步 > 15 毫秒累積 3 次，或單步 > 2 秒） | 淘汰 | 排除該檔 |
| `Name` 丟例外、隊名重複 | 淘汰 | 排除該檔 |
| 建構子丟例外 | 未參賽 | 排除該檔 |
| 無窮迴圈、無限遞迴 | ⛔ GH 卡死、攔不到 | 試跑逾時 60 秒，排除該檔 |
| 禁用寫法（檔案、網路、執行緒、反射、`new Random`、`DateTime.Now`、static 可變欄位、碰引擎類別或 GH 文件） | 不會擋 | 排除該檔 |

所以**比賽前一定用 `--check` 產生 host**，GH 裡的防錯只是第二道。

## 學生怎麼加入

1. 複製 `students\boid_template.cs` 為 `students\boid_{名字}.cs`，class 改名 `Boid_{名字}`、`Name` 改成自己的。
2. 只改這個檔，**不要動 `core\`、`host\`**。
3. 執行 `python tools\gen_host.py`，把 `host\host_script.cs`（約 60 行：`#load` 清單＋薄樣板）整份貼進 GH 的 C# Script 元件，按編輯器綠色執行鈕（GH 不會自動重新編譯）。
4. 確認沒有編譯錯誤、`info` 裡自己隊伍的狀態是「正常」再提交。有 .NET SDK 的話，提交前跑 `python tools\gen_host.py --check`。

## GH 元件

整個畫面由主程式畫在視埠（場地、障礙物、Boid、食物、五芒星、依排名排列的文字），**不用接其他元件**；建議 Rhino 切到 Top 視圖。
輸出只有 `info`（除錯文字）；`points` 輸出原點，只為了讓 GH 開啟預覽。

| 輸入 | 說明（不接就用預設；改了要按 reset） |
|---|---|
| reset | bool，接 Button |
| run | bool，接 Boolean Toggle：True 自動前進（30 步／秒），False 暫停 |
| seed | 整數，預設 20261103（0 = 預設） |
| minutes | 一回合分鐘數，預設 3 |
| starRate | 五芒星每步機率 0～1，預設 0.004；0 = 不出現 |

新增輸入：GH 畫面放大到約 400%，元件輸入端出現小圓點，點最下面的新增，右鍵改名。
畫面調整在 `host\GameHost.cs` 開頭：`BoidScale`（Boid 整體縮放，預設 1.0）、`BoidLength`、`OutlineThickness`、`ScheduleMs`。

## 老師怎麼合併

- 學生各開自己的分支，只改自己的 `boid_{名字}.cs`，開 PR；`core\`、`host\` 設為老師獨有，主分支開啟分支保護。
- pull 完一定執行 `python tools\gen_host.py --check`：逐檔掃描禁用寫法、單獨編譯並試跑 300 步，再把通過的檔合起來試跑一次；有問題的檔不寫進 `#load`，原因寫在 `host_script.cs` 最後的 `// 檢查 …` 註解。
- 合併試跑時被淘汰的隊直接排除那一隊；超過 10 隊的檔會指出「不會參賽」。

## 已知限制

- `#load` 只吃寫死的絕對路徑，不支援相對路徑、萬用字元或巢狀載入（已在 GH 實測），所以每次增減學生檔都要重跑 `gen_host.py` 再貼進 GH。
- GH 裡「太慢」的門檻對 10 隊同時跑是否合適，只在命令列驗證過。
