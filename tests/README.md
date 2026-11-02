# 測試（只在 `test` 分支；不開 Rhino）

| 路徑 | 內容 |
|---|---|
| `tests\arena_test\` | 引擎測試程式（命令列 .NET 8） |
| `tests\fixtures\` | 故意寫錯的學生檔，測 `--check` 會不會排除 |
| `tests\stress\` | 極限測試：10 份誠實 + 5 份故意出錯的學生檔（`tests\stress\students\`），丟進同一場 |
| `students\` 裡其他檔 | 範例對手（orlex、hunter、alice…）與壓測檔的複本 |

## 引擎測試

```
cd tests\arena_test
dotnet build -c Release
dotnet run --no-build -c Release -- <模式>
```

| 模式 | 驗證 |
|---|---|
| `race [步數]` | 10 種範例性格跑一場，每 500 步印各隊存活數 |
| `fault` | 壞隊（NaN、Name 例外、隊名重複、第 5 步丟例外、太慢）被淘汰，其他隊位置和沒有壞隊時完全相同 |
| `roster` | 建構子出錯的 class 只記成未參賽 |
| `round` | 0.5 分鐘 = 900 步；三階段分界、障礙物與食物規則、時間到停止、Z 恆為 0、不合理分鐘數退回 3 分鐘 |
| `hunt [步數]` | 獵殺範本與 orlex 對 8 種範例性格 |
| `turn` | 每步轉向角度與急轉原因（牆、障礙物、異隊碰撞、其他） |
| `early` | 只剩一隊提早結束；只有一隊參賽不算 |
| `relations` | `Declare` 的設定反映在 `Relation`；`Declare` 出錯被淘汰 |
| `star` | 無敵 100 步、星星消失、出現數量與階段限制、`starRate` 0／1 |
| `colors` | 10 隊顏色兩兩距離夠遠、同種子同結果、換種子洗牌、淘汰的隊是灰色、學生寫的 `Color` 無效 |
| `food` | 新食物落在最弱隊平均位置 10 單位內 |

## `--check` 測試

```
python tools\gen_host.py --check --students tests\fixtures
```

應該只有 alice、gina 通過（語法錯、無窮迴圈、`new Random`、class 重名、執行例外、helper 重名都被排除）。

## 極限測試

```
python tools\gen_host.py --check --students tests\stress\students
cd tests\stress
dotnet build -c Release
dotnet run --no-build -c Release -- all      # 所有能編譯的檔（含出錯的），取前 10 隊
dotnet run --no-build -c Release -- honest   # 只有誠實的 10 隊
```

結果：`--check` 排除 bad_syntax（編譯錯誤）、bad_random（違規）、bad_throw（試跑例外）、bad_loop（逾時）；bad_nan 單獨試跑會過，合併試跑才抓得到。
沒走 `--check` 直接比賽：bad_throw 第 1 步、bad_nan 第 159 步被淘汰，其他隊跑完 5400 步；bad_random 只有 `--check` 的掃描擋得下來。
Release 下 10 隊全場最慢一步約 60 毫秒，整場約 35～45 秒。單一隊伍容易滾雪球到 ~499/500（目前規則下的平衡問題）。
