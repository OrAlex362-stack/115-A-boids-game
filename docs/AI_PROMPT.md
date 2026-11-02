# 給網頁 GPT／Gemini 的提示（學生貼過去用）

把下面整段貼給 AI，最後一行換成你想要的性格，再把 AI 回答的 C# 存成 `students\boid_你的名字.cs`。

---

你是 C# 助教。請幫我寫一個 Grasshopper（Rhino 8）C# Script 用的類別，規則如下，全部用繁體中文註解。

【場景】
10 個隊伍在 2D 平面（所有東西的 Z 都是 0，Z 方向的力會被引擎丟掉）的正方形場地裡，每隊 50 隻 Boid。
一回合約 3 分鐘，分三階段：場地不縮、有矩形障礙物（`world.Obstacles`，與座標軸平行，有 `Center`、`HalfWidth`、`HalfHeight`，撞到會被彈開）→ 場地慢慢縮小（中心跑到場外的障礙物會消失）→ 場地不再縮、食物不再出現（`world.Phase` 是 1、2、3）。
地圖上隨機出現食物，吃到食物就複製成兩隻；偶爾出現五芒星，吃到的那隻無敵 100 步（不會分裂）（全場上限 500 隻，滿了就不能出生）。
異隊碰到時：A 的前方 60 度碰到 B 的正後方 60 度，A 殺死 B；正面對撞或側邊碰撞則互相彈開。
同隊之間的分離由引擎處理，你不用寫。
最後存活最多的隊伍獲勝。

【你只能寫這一個類別】

```csharp
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    public class Boid_你的名字 : IBrain, IDeclareRelations
    {
        public string Name { get { return "你的名字"; } }

        // 開賽前呼叫一次（選用）：決定誰是友軍、敵軍、不相關。預設同隊友軍、異隊敵軍
        public void Declare(RelationTable table)
        {
            // table.MyName、table.AllNames（這一場所有隊伍的名字）
            // table.Ally("某隊名")；table.Enemy("某隊名")；table.Neutral("某隊名")
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            // 回傳「想往哪轉」的向量，引擎會限制大小
        }
    }
}
```

【可以用的資料】
- `self.Name`（我的隊名）、`self.Position`、`self.Velocity`、`self.Team`
- `neighbors`：視野內每隻鳥的 `Name`（它的隊名）、`Relation`（`Relation.Ally` 友軍、`Relation.Enemy` 敵軍、`Relation.Neutral` 不相關）、`Position`、`Velocity`、`Team`、`IsFriend`（同隊）、`Distance`
- 身份只是行為模式用的標籤，**不改變擊殺規則**：異隊碰到時照樣依 60 度錐判定，不管你標成什麼
- `foods`：視野內的食物位置（`List<Point3d>`）
- `self.Stars`：視野內的五芒星位置（吃到就無敵 100 步：殺不死，但照樣能殺別人）；`self.InvincibleSteps`：我還剩幾步無敵；`neighbor.InvincibleSteps`：對方還剩幾步無敵（無敵的殺不死，不用追）
- 開賽前 `Declare` 的 `table`：`MyName`、`AllNames`（有誰）、`Ally`／`Enemy`／`Neutral`（設定身份）、`Get(隊名)`；沒設定的異隊是敵軍，同隊永遠是友軍
- `world.HalfSize`（場地半邊長）、`world.Center`、`world.Step`、`world.ViewRadius`、`world.MaxSpeed`、`world.MaxForce`（全部唯讀）
- `world.Obstacles`（每個有 `Center`、`HalfWidth`、`HalfHeight`，另有 `DistanceTo(點)` 到邊緣的距離、`ClosestPoint(點)` 邊上最近的點）、`world.Phase`（1 穩定期、2 縮小期、3 決戰期）
- 這是 2D 遊戲：向量的 Z 一律當 0，不要在 Z 方向做事
- `self.Rng`：要亂數一律用它（例如 `self.Rng.NextDouble()`），比賽才能重播
- `Steering.Seek(想去的方向, self.Velocity, world)`：把方向變成轉向力
- `Steering.Unit(向量)`：單位向量（不要用 `Vector3d.Unitize`）

【禁止】
- 不可使用檔案、網路、執行緒（`Thread`、`Task`、`async`、`Parallel`）、`System.IO`、`System.Net`、`Process`、反射。
- 不可自己 `new Random()`，不可用 `DateTime.Now`、`Guid.NewGuid`、`Environment`（會破壞固定種子）。
- 不可使用 static 可變欄位（`static readonly` 可以），不可碰 `Arena`、`Agent`、`Roster`，不可碰 Rhino／GH 文件。
- 只能用 `Vector3d`、`Point3d`、`System.Math`、`System.Linq`；不要用 `Vector3d.Unitize`，改用 `Steering.Unit`。
- 不用寫顏色：每隊的顏色由老師的主程式註冊後隨機分配高對比色，學生不能選。
- 不可有無窮迴圈或無限遞迴；每次 `Decide` 要很快跑完（全隊 50 隻加起來每步 15 毫秒內）。
- 如果需要額外的輔助類別，名稱前面加上我的名字（例如 `你的名字_Helper`），避免和同學撞名。
- 寫錯（丟例外、回傳 NaN、太慢）整隊會被淘汰，所以請處理 `neighbors`、`foods` 是空清單的情況。

【我想要的性格】
（在這裡寫，例如：喜歡成群行動、有食物就搶、看到背後有敵人就快逃）
