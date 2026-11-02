using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;
using BoidArena;
using BoidArena.Tests;

// 無頭測試（不用開 Rhino）
//   dotnet run -- race [步數]      10 種性格跑一場，看規則會不會僵局
//   dotnet run -- fault            防錯測試：壞掉的隊伍只讓自己消失，其他隊結果和沒有壞隊時完全相同
//   dotnet run -- roster           用反射找 Boid_ class，壞掉的建構子只被記成未參賽
class Program
{
    static List<IBrain> GoodBrains()
    {
        //                         name        color            align cohes food chase flee
        return new List<IBrain>
        {
            new ParamBrain("swarm",   Color.Red,        1.0, 1.0, 0.5, 0.0, 0.0),
            new ParamBrain("hunter",  Color.Orange,     0.5, 0.5, 0.5, 1.5, 0.0),
            new ParamBrain("coward",  Color.Yellow,     1.0, 1.0, 0.5, 0.0, 2.0),
            new ParamBrain("glutton", Color.Green,      0.3, 0.3, 2.0, 0.0, 0.5),
            new ParamBrain("mixed",   Color.Cyan,       0.8, 0.8, 1.0, 0.8, 1.0),
            new ParamBrain("loner",   Color.Blue,       0.0, 0.0, 1.0, 0.5, 1.0),
            new ParamBrain("pack",    Color.Purple,     1.5, 1.5, 0.5, 1.0, 0.0),
            new ParamBrain("wander",  Color.Pink,       0.0, 0.0, 0.0, 0.0, 0.0),
            new ParamBrain("greedy",  Color.Brown,      0.5, 0.5, 1.5, 0.5, 0.5),
            new ParamBrain("guard",   Color.Gray,       1.0, 1.0, 0.5, 0.5, 1.5),
        };
    }

    static string Report(Arena arena)
    {
        int[] alive = arena.AliveByTeam();
        return string.Join("  ", Enumerable.Range(0, arena.Brains.Count)
            .Select(t => arena.TeamNames[t] + "=" + alive[t]));
    }

    static int Race(int steps)
    {
        var arena = new Arena(GoodBrains(), Arena.GlobalSeed);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int s = 1; s <= steps; s++)
        {
            arena.RunStep();
            if (s % 500 == 0)
                Console.WriteLine("step " + s + " total " + arena.AliveCount + " food " + arena.Foods.Count
                    + " half " + arena.HalfSize.ToString("F1") + " | " + Report(arena));
        }
        Console.WriteLine("kills " + string.Join(" ", arena.TeamKills) + "  time " + sw.ElapsedMilliseconds + " ms");
        return 0;
    }

    // 獵殺範本 vs 9 種範例性格：看範本有沒有比別人更會殺、更會活
    static int Hunt(int steps)
    {
        var brains = GoodBrains().Take(8).ToList();
        brains.Add(new Boid_template_hunter());
        brains.Add(new Boid_orlex());
        var arena = new Arena(brains, Arena.GlobalSeed);
        for (int s = 1; s <= steps; s++)
        {
            arena.RunStep();
            if (s % 500 == 0)
                Console.WriteLine("step " + s + " " + arena.PhaseName + " total " + arena.AliveCount + " obstacles "
                    + arena.Obstacles.Count + " | " + Report(arena));
        }
        Console.WriteLine("kills " + string.Join(" ", arena.TeamKills));
        return 0;
    }

    // 回合時間與三階段：0.5 分鐘 = 900 步，穩定期 20%、縮小期 50%、決戰期 30%
    static int Round()
    {
        int failures = 0;
        var withZ = GoodBrains().Take(5).ToList();
        withZ.Add(new ZPushBrain());   // 學生亂回傳 Z 方向的力
        var arena = new Arena(withZ, Arena.GlobalSeed, 0.5);
        int nonPlanar = 0;
        int foodBeforeLast = -1, obstaclesAtStableEnd = -1;
        double halfAtStableEnd = -1, halfAtShrinkEnd = -1;
        int foodSpawnedInPhase3 = 0, lastFoodCount = 0;
        for (int s = 1; s <= 1000; s++)   // 多跑 100 步，確認時間到之後不再前進
        {
            arena.RunStep();
            if (arena.Step == arena.StableEnd) { halfAtStableEnd = arena.HalfSize; obstaclesAtStableEnd = arena.Obstacles.Count; }
            if (arena.Step == arena.ShrinkEnd) halfAtShrinkEnd = arena.HalfSize;
            if (arena.Phase == 3 && arena.Foods.Count > lastFoodCount) foodSpawnedInPhase3++;
            lastFoodCount = arena.Foods.Count;
            foodBeforeLast = arena.Foods.Count;
            foreach (var a in arena.Agents)
                if (a.Position.Z != 0 || a.Velocity.Z != 0) nonPlanar++;
            foreach (var f in arena.Foods) if (f.Z != 0) nonPlanar++;
            foreach (var o in arena.Obstacles) if (o.Center.Z != 0) nonPlanar++;
        }
        if (nonPlanar != 0) { Console.WriteLine("FAIL 有東西離開 Z = 0：" + nonPlanar + " 次"); failures++; }
        Console.WriteLine("2D 檢查：Boid、食物、障礙物的 Z 都是 0（zpush 隊伍存活 "
            + arena.AliveByTeam()[5] + " 隻）");
        Console.WriteLine("RoundSteps " + arena.RoundSteps + "  StableEnd " + arena.StableEnd + "  ShrinkEnd " + arena.ShrinkEnd
            + "  Step " + arena.Step + "  Finished " + arena.Finished);
        Console.WriteLine("階段 1 結束：場地 " + halfAtStableEnd.ToString("F1") + "，障礙物 " + obstaclesAtStableEnd);
        Console.WriteLine("階段 2 結束：場地 " + halfAtShrinkEnd.ToString("F1") + "，階段 3 新增食物次數 " + foodSpawnedInPhase3
            + "，勝者 " + (arena.Winner >= 0 ? arena.TeamNames[arena.Winner] : "無"));
        if (arena.RoundSteps != 900 || arena.StableEnd != 180 || arena.ShrinkEnd != 630) { Console.WriteLine("FAIL 步數換算"); failures++; }
        if (arena.Step != 900 || !arena.Finished) { Console.WriteLine("FAIL 時間到沒有停"); failures++; }
        if (Math.Abs(halfAtStableEnd - Arena.StartHalf) > 1e-9) { Console.WriteLine("FAIL 階段 1 場地不該縮"); failures++; }
        if (Math.Abs(halfAtShrinkEnd - Arena.EndHalf) > 1e-9) { Console.WriteLine("FAIL 階段 2 結束場地沒縮到底"); failures++; }
        if (obstaclesAtStableEnd != Arena.ObstacleCount) { Console.WriteLine("FAIL 階段 1 障礙物不該被移除"); failures++; }
        if (foodSpawnedInPhase3 != 0) { Console.WriteLine("FAIL 階段 3 不該再出現食物"); failures++; }
        // 輸入不合理（0、負數、NaN）要退回預設 3 分鐘
        foreach (double bad in new[] { 0, -1, double.NaN })
        {
            var fallback = new Arena(GoodBrains(), Arena.GlobalSeed, bad);
            if (fallback.RoundSteps != 5400) { Console.WriteLine("FAIL 預設分鐘 " + bad); failures++; }
        }
        Console.WriteLine(failures == 0 ? "PASS round" : "FAIL round " + failures);
        return failures == 0 ? 0 : 1;
    }

    // 轉向是否平順：跑 600 步，統計每隻每步速度方向的變化角度（碰撞彈開、被擊殺的那一步也算進去）
    static int Turn()
    {
        var arena = new Arena(GoodBrains(), Arena.GlobalSeed);
        var previous = new Dictionary<Agent, Vector3d>();
        var angles = new List<double>();
        var causes = new Dictionary<string, int>();
        for (int s = 0; s < 600; s++)
        {
            arena.RunStep();
            foreach (var a in arena.Agents)
            {
                Vector3d now = Steering.Unit(a.Velocity);
                Vector3d before;
                if (previous.TryGetValue(a, out before) && before.Length > 0 && now.Length > 0)
                {
                    double angle = Math.Acos(Math.Max(-1, Math.Min(1, before * now))) * 180 / Math.PI;
                    angles.Add(angle);
                    if (angle > 20)
                    {
                        bool wall = Math.Abs(a.Position.X) > arena.HalfSize - 0.01 || Math.Abs(a.Position.Y) > arena.HalfSize - 0.01;
                        bool block = arena.Obstacles.Any(o => o.DistanceTo(a.Position) < 0.01);
                        bool hit = arena.Agents.Any(b => b != a && b.Team != a.Team && b.Position.DistanceTo(a.Position) < 1.3);
                        string why = wall ? "wall" : block ? "obstacle" : hit ? "agent" : "steer";
                        causes[why] = (causes.ContainsKey(why) ? causes[why] : 0) + 1;
                    }
                }
                previous[a] = now;
            }
        }
        angles.Sort();
        Console.WriteLine("SteerBlend " + Arena.SteerBlend + "  樣本 " + angles.Count
            + "  平均 " + angles.Average().ToString("F2") + " 度/步"
            + "  中位數 " + angles[angles.Count / 2].ToString("F2")
            + "  p95 " + angles[(int)(angles.Count * 0.95)].ToString("F2")
            + "  p99 " + angles[(int)(angles.Count * 0.99)].ToString("F2")
            + "  超過 20 度的步數比例 " + (angles.Count(x => x > 20) / (double)angles.Count * 100).ToString("F2") + "%");
        Console.WriteLine("超過 20 度的原因：" + string.Join("  ", causes.Select(kv => kv.Key + " " + kv.Value)));
        return 0;
    }

    // 提早結束：開賽 2 隊，一隊第 5 步被淘汰，場上只剩一隊就該結束；只有 1 隊參賽則不算提早結束
    static int Early()
    {
        int failures = 0;
        var arena = new Arena(new List<IBrain> { new ParamBrain("solo", Color.Blue, 1, 1, 1, 0, 0), new Boid_throwLater() }, Arena.GlobalSeed);
        for (int s = 0; s < 100; s++) arena.RunStep();
        Console.WriteLine("兩隊其中一隊出錯：Step " + arena.Step + "  EndedEarly " + arena.EndedEarly + "  勝者 " + arena.TeamNames[arena.Winner]);
        if (!arena.EndedEarly || !arena.Finished || arena.Step > 10 || arena.TeamNames[arena.Winner] != "solo") { Console.WriteLine("FAIL 只剩一隊沒有提早結束"); failures++; }

        var single = new Arena(new List<IBrain> { new ParamBrain("only", Color.Blue, 1, 1, 1, 0, 0) }, Arena.GlobalSeed);
        for (int s = 0; s < 50; s++) single.RunStep();
        if (single.EndedEarly || single.Step != 50) { Console.WriteLine("FAIL 單隊測試不該提早結束"); failures++; }
        Console.WriteLine(failures == 0 ? "PASS early" : "FAIL early");
        return failures;
    }

    // 身份：Declare 的設定要反映在鄰居的 Relation，Name 要是對方隊名；Declare 出錯的隊要被淘汰；擊殺規則不受身份影響
    static int Relations()
    {
        int failures = 0;
        var a = new RelationBrain("ra", "rb", "rc");
        var b = new RelationBrain("rb", null, null);
        var c = new RelationBrain("rc", null, null);
        var bad = new BadDeclareBrain();
        var arena = new Arena(new List<IBrain> { a, b, c, bad }, Arena.GlobalSeed);
        for (int s = 0; s < 200; s++) arena.RunStep();
        Console.WriteLine("ra 看到的身份：" + string.Join("  ", a.Seen.Select(kv => kv.Key + "=" + string.Join("/", kv.Value))));
        Console.WriteLine("Declare 時的全部隊名：" + string.Join(",", a.AllNamesAtDeclare) + "  self.Name=" + a.SelfNameSeen);
        if (a.Seen.ContainsKey("rb") && (a.Seen["rb"].Count != 1 || !a.Seen["rb"].Contains(Relation.Ally))) { Console.WriteLine("FAIL rb 應該是友軍"); failures++; }
        if (a.Seen.ContainsKey("rc") && (a.Seen["rc"].Count != 1 || !a.Seen["rc"].Contains(Relation.Neutral))) { Console.WriteLine("FAIL rc 應該是不相關"); failures++; }
        if (b.Seen.ContainsKey("ra") && !b.Seen["ra"].SetEquals(new[] { Relation.Enemy })) { Console.WriteLine("FAIL 預設異隊應該是敵軍"); failures++; }
        if (!a.Seen.ContainsKey("rb") || !a.Seen.ContainsKey("rc")) { Console.WriteLine("FAIL 200 步內沒看到別隊，測試無效"); failures++; }
        if (!a.Seen.ContainsKey("ra") || !a.Seen["ra"].SetEquals(new[] { Relation.Ally }) || a.NeighborSelfSeen) { Console.WriteLine("FAIL 同隊永遠是友軍（Declare 改不了自己這隊）"); failures++; }
        if (a.SelfNameSeen != "ra" || !a.AllNamesAtDeclare.Contains("rc")) { Console.WriteLine("FAIL self.Name 或隊名清單不對"); failures++; }
        if (!arena.Disqualified[3] || !arena.Status[3].Contains("Declare")) { Console.WriteLine("FAIL Declare 出錯的隊沒有被淘汰"); failures++; }
        int[] alive = arena.AliveByTeam();
        if (alive[3] != 0) { Console.WriteLine("FAIL 被淘汰的隊場上還有鳥"); failures++; }
        Console.WriteLine(failures == 0 ? "PASS relations" : "FAIL relations");
        return failures;
    }

    // 五芒星：被碰到就無敵 100 步（異隊從背後撞也殺不死）、星星消失、階段 3 不再出現
    // 顏色：由老師分配，10 隊兩兩清楚不同、離畫面固定色夠遠、同種子同結果、不同種子洗牌、淘汰的隊是灰色
    static int Colors()
    {
        int bad = 0;
        Func<Color, Color, double> dist = (x, y) => Math.Sqrt(Math.Pow(x.R - y.R, 2) + Math.Pow(x.G - y.G, 2) + Math.Pow(x.B - y.B, 2));
        var fixedColors = new[] { Color.FromArgb(160, 160, 160), Color.FromArgb(50, 205, 50), Color.FromArgb(200, 150, 80), Color.FromArgb(0, 170, 150) };
        var arena = new Arena(GoodBrains(), Arena.GlobalSeed);
        double minTeam = double.MaxValue, minFixed = double.MaxValue;
        for (int a = 0; a < 10; a++)
        {
            Console.WriteLine(arena.TeamNames[a] + "  " + arena.TeamColors[a]);
            foreach (Color f in fixedColors) minFixed = Math.Min(minFixed, dist(arena.TeamColors[a], f));
            for (int b = a + 1; b < 10; b++) minTeam = Math.Min(minTeam, dist(arena.TeamColors[a], arena.TeamColors[b]));
        }
        Console.WriteLine("隊伍兩兩最小距離 " + minTeam.ToString("F0") + "；離固定色最小距離 " + minFixed.ToString("F0"));
        if (minTeam < 120) { Console.WriteLine("FAIL 隊伍之間太像"); bad++; }
        if (minFixed < 110) { Console.WriteLine("FAIL 和畫面固定色太像"); bad++; }

        var again = new Arena(GoodBrains(), Arena.GlobalSeed);
        if (!arena.TeamColors.SequenceEqual(again.TeamColors)) { Console.WriteLine("FAIL 同種子顏色不同"); bad++; }
        int differ = 0;
        for (int seed = 1; seed <= 5; seed++)
            if (!arena.TeamColors.SequenceEqual(new Arena(GoodBrains(), seed).TeamColors)) differ++;
        Console.WriteLine("換種子後顏色順序不同的次數 " + differ + "/5");
        if (differ < 4) { Console.WriteLine("FAIL 不同種子沒有洗牌"); bad++; }

        // 隊名重複的那隊被淘汰：灰色，而且不佔用色盤
        var dup = new List<IBrain> { new ParamBrain("a", Color.Red, 0, 0, 0, 0, 0), new ParamBrain("a", Color.Red, 0, 0, 0, 0, 0), new ParamBrain("b", Color.Red, 0, 0, 0, 0, 0) };
        var arena3 = new Arena(dup, Arena.GlobalSeed);
        if (arena3.TeamColors[1] != Color.Gray) { Console.WriteLine("FAIL 淘汰的隊該是灰色"); bad++; }
        if (arena3.TeamColors[0] == Color.Gray || arena3.TeamColors[2] == Color.Gray || arena3.TeamColors[0] == arena3.TeamColors[2]) { Console.WriteLine("FAIL 存活隊顏色錯"); bad++; }
        // 學生自己在 class 裡寫的 Color（舊檔還有）完全不影響：三隊都寫紅，拿到的卻不是紅
        if (arena3.TeamColors[0] == Color.Red) { Console.WriteLine("FAIL 學生的 Color 不該有作用"); bad++; }

        Console.WriteLine(bad == 0 ? "colors OK" : "colors FAIL " + bad);
        return bad == 0 ? 0 : 1;
    }

    // 食物：新出現的食物要落在「存活最少且不是 0 的隊」平均位置周圍 10 單位內（容許那一步的移動／換隊誤差）
    static int FoodTest()
    {
        var arena = new Arena(GoodBrains(), Arena.GlobalSeed);
        int total = 0, near = 0, weakChanged = 0;
        for (int s = 1; s <= 1500; s++)
        {
            int[] alive = arena.AliveByTeam();
            int weakest = -1;
            for (int t = 0; t < alive.Length; t++) if (alive[t] > 0 && (weakest < 0 || alive[t] < alive[weakest])) weakest = t;
            var mine = arena.Agents.Where(a => a.Team == weakest).ToList();
            var before = new HashSet<Point3d>(arena.Foods);
            arena.RunStep();
            if (mine.Count == 0) continue;
            double cx = mine.Average(a => a.Position.X), cy = mine.Average(a => a.Position.Y);
            var fresh = arena.Foods.Where(f => !before.Contains(f)).ToList();
            int[] after = arena.AliveByTeam();
            bool same = Enumerable.Range(0, alive.Length).All(t => (alive[t] > 0) == (after[t] > 0)) && after[weakest] == alive[weakest];
            foreach (var f in fresh)
            {
                if (!same) { weakChanged++; continue; }
                total++;
                if (Math.Sqrt((f.X - cx) * (f.X - cx) + (f.Y - cy) * (f.Y - cy)) <= 10 + 1.5) near++;
            }
        }
        Console.WriteLine("新食物 " + total + " 個（最弱隊當步有變動、略過 " + weakChanged + "），落在 10 單位圓內 " + near);
        bool ok = total > 100 && near >= total * 0.95;
        Console.WriteLine(ok ? "food OK" : "food FAIL");
        return ok ? 0 : 1;
    }

    static int Star()
    {
        int failures = 0;
        for (int mode = 0; mode < 2; mode++)   // 0 = 沒有星星：B 會被殺；1 = 吃到星星：B 殺不死
        {
            var arena = new Arena(new List<IBrain> { new RelationBrain("A", null, null), new RelationBrain("B", null, null) }, Arena.GlobalSeed);
            Agent a = arena.Agents.First(x => x.Team == 0), b = arena.Agents.First(x => x.Team == 1);
            arena.Agents.RemoveAll(x => x != a && x != b);
            a.Position = new Point3d(-10, 0, 0); a.Velocity = new Vector3d(0.3, 0, 0);
            b.Position = new Point3d(0.6, 0, 0); b.Velocity = new Vector3d(0.3, 0, 0);
            arena.Obstacles.Clear();
            if (mode == 1) arena.Stars.Add(new Point3d(0.9, 0, 0));   // 第 1 步 B 走到 0.9，吃到星星
            arena.RunStep();
            Console.WriteLine("mode " + mode + "：第 1 步後 B 無敵到第 " + b.InvincibleUntil + " 步  星星剩 " + arena.Stars.Count);
            if (mode == 1 && (b.InvincibleUntil != arena.Step + Arena.StarSteps || arena.Stars.Count != 0))
            { Console.WriteLine("FAIL 吃到星星後應該無敵 100 步、星星消失"); failures++; }
            // 第 2 步：A 從 B 的正後方貼上去（A 的前方碰到 B 的後方 → 本來會殺）
            a.Position = new Point3d(b.Position.X - 0.6, 0, 0); a.Velocity = new Vector3d(0.3, 0, 0); b.Velocity = new Vector3d(0.3, 0, 0);
            arena.RunStep();
            bool bAlive = arena.Agents.Contains(b);
            Console.WriteLine("mode " + mode + "：第 2 步後 B 存活 " + bAlive);
            if (mode == 0 && bAlive) { Console.WriteLine("FAIL 沒有星星時 B 應該被殺"); failures++; }
            if (mode == 1 && !bAlive) { Console.WriteLine("FAIL 無敵的 B 不該被殺"); failures++; }
        }
        // 長跑：星星會出現、階段 3 不再出現、場上最多 MaxStars 個
        var run = new Arena(GoodBrains(), Arena.GlobalSeed, 1);
        int seenStars = 0, maxStars = 0, starsInPhase3 = 0, taken = 0;
        int lastCount = 0;
        for (int s = 0; s < run.RoundSteps; s++)
        {
            run.RunStep();
            if (run.Stars.Count > maxStars) maxStars = run.Stars.Count;
            if (run.Stars.Count > lastCount) seenStars++;
            if (run.Stars.Count < lastCount) taken++;
            lastCount = run.Stars.Count;
            if (run.Phase == 3 && run.Step > run.ShrinkEnd + 1 && run.Stars.Count > lastCount) starsInPhase3++;
            if (run.Finished) break;
        }
        Console.WriteLine("1 分鐘：出現 " + seenStars + " 次  被吃或移除 " + taken + " 次  同時最多 " + maxStars + " 顆  階段 3 新增 " + starsInPhase3);
        if (seenStars == 0) { Console.WriteLine("FAIL 一分鐘內沒出現五芒星"); failures++; }
        if (maxStars > Arena.MaxStars) { Console.WriteLine("FAIL 超過上限"); failures++; }
        // starRate：0 = 整場不出現；1 = 階段 1～2 一直補到上限
        var none = new Arena(GoodBrains(), Arena.GlobalSeed, 1, 0);
        var many = new Arena(GoodBrains(), Arena.GlobalSeed, 1, 1);
        int noneMax = 0, manyMax = 0;
        for (int s = 0; s < 600; s++) { none.RunStep(); many.RunStep(); noneMax = Math.Max(noneMax, none.Stars.Count); manyMax = Math.Max(manyMax, many.Stars.Count); }
        Console.WriteLine("starRate=0 最多 " + noneMax + " 顆；starRate=1 最多 " + manyMax + " 顆");
        if (noneMax != 0 || manyMax != Arena.MaxStars) { Console.WriteLine("FAIL starRate 輸入沒生效"); failures++; }
        Console.WriteLine(failures == 0 ? "PASS star" : "FAIL star");
        return failures;
    }

    static int Fault()
    {
        const int steps = 800;
        int failures = 0;
        // 第一隊用 self.Rng，排在壞隊前面：壞隊出錯重算時，它已經抽過一次亂數
        var good = new List<IBrain> { new JitterBrain() };
        good.AddRange(GoodBrains().Take(2));
        var baseline = new Arena(good, Arena.GlobalSeed);
        for (int s = 0; s < steps; s++) baseline.RunStep();

        // A. 一開始就出錯（NaN、Name 例外、隊名重複）：其他隊要和完全沒有壞隊時一模一樣
        var instant = new List<IBrain> { good[0], new Boid_nan(), good[1], new Boid_badName(), good[2], new Boid_dupName() };
        var arenaA = new Arena(instant, Arena.GlobalSeed);
        for (int s = 0; s < steps; s++) arenaA.RunStep();
        Console.WriteLine("A. 一開始就出錯");
        failures += Check(arenaA, new[] { 1, 3, 5 });
        int[] goodIndex = { 0, 2, 4 };
        for (int g = 0; g < 3; g++)
        {
            var p = baseline.Agents.Where(x => x.Team == g).Select(x => x.Position).ToList();
            var q = arenaA.Agents.Where(x => x.Team == goodIndex[g]).Select(x => x.Position).ToList();
            bool same = p.Count == q.Count && p.Zip(q, (u, v) => u.DistanceTo(v)).All(d => d < 1e-9);
            Console.WriteLine("  " + good[g].Name + " 對照 " + p.Count + " 隻，實驗 " + q.Count + " 隻，位置"
                + (same ? "完全相同" : "不同"));
            if (!same) failures++;
        }

        // B. 跑到一半才出錯（第 5 步丟例外、太慢）：壞隊消失，比賽繼續跑完，其他隊不受例外影響
        var later = new List<IBrain> { good[0], new Boid_throwLater(), good[1], new Boid_slow(), good[2] };
        var arenaB = new Arena(later, Arena.GlobalSeed);
        for (int s = 0; s < steps; s++) arenaB.RunStep();
        Console.WriteLine("B. 跑到一半才出錯（跑完 " + arenaB.Step + " 步）");
        failures += Check(arenaB, new[] { 1, 3 });
        foreach (int t in new[] { 0, 2, 4 })
            if (arenaB.Disqualified[t]) { Console.WriteLine("FAIL 正常隊被誤判：" + arenaB.TeamNames[t]); failures++; }

        Console.WriteLine(failures == 0 ? "PASS fault" : "FAIL fault " + failures);
        return failures == 0 ? 0 : 1;
    }

    // 壞隊都要被淘汰，而且場上一隻都不剩
    static int Check(Arena arena, int[] badTeams)
    {
        int failures = 0;
        int[] alive = arena.AliveByTeam();
        for (int t = 0; t < arena.Brains.Count; t++)
            Console.WriteLine("  " + arena.TeamNames[t] + "：" + arena.Status[t] + "　存活 " + alive[t]);
        foreach (int t in badTeams)
        {
            if (!arena.Disqualified[t] || alive[t] != 0)
            {
                Console.WriteLine("FAIL 壞隊沒有被淘汰：" + arena.TeamNames[t]);
                failures++;
            }
        }
        return failures;
    }

    static int RosterTest()
    {
        List<string> rejected;
        var brains = Roster.Find(typeof(Program).Assembly, out rejected);
        Console.WriteLine("  參賽：" + string.Join(", ", brains.Select(b => b.GetType().Name)));
        foreach (string r in rejected) Console.WriteLine("  未參賽：" + r);
        var arena = new Arena(brains, Arena.GlobalSeed);
        for (int s = 0; s < 50; s++) arena.RunStep();
        bool ok = rejected.Any(r => r.StartsWith("Boid_badCtor")) && brains.All(b => !(b is Boid_badCtor));
        Console.WriteLine(ok ? "PASS roster" : "FAIL roster");
        return ok ? 0 : 1;
    }

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "race";
        if (mode == "fault") return Fault();
        if (mode == "roster") return RosterTest();
        if (mode == "round") return Round();
        if (mode == "turn") return Turn();
        if (mode == "star") return Star();
        if (mode == "colors") return Colors();
        if (mode == "food") return FoodTest();
        if (mode == "relations") return Relations();
        if (mode == "early") return Early();
        if (mode == "hunt") return Hunt(args.Length > 1 ? int.Parse(args[1]) : 3000);
        return Race(args.Length > 1 ? int.Parse(args[1]) : 3000);
    }
}
