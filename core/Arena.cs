using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 場上一隻 Boid 的狀態（引擎內部使用，學生碰不到）
    public class Agent
    {
        public Point3d Position;
        public Vector3d Velocity;
        public int Team;
        public int InvincibleUntil;   // 到這一步（含）為止無敵；0 = 沒有
        public Vector3d Steer;        // 上一步實際用的轉向力（用來平滑）
    }

    // 競技場：2D（Z 恆為 0），場地是以原點為中心的正方形。
    // 負責障礙物、食物、五芒星、出生、擊殺、縮圈、防錯與隊伍顏色；學生碰不到。
    public class Arena
    {
        // ── 規則 ──
        public const int GlobalSeed = 20261103;     // 預設種子（GH 的 seed 輸入不接或 0 時使用）
        public const int TeamSize = 50;             // 每隊起始隻數
        public const int Cap = 500;                 // 全場上限，滿了不能出生
        public const double ContactDistance = 1.0;  // 兩隻距離小於這個值算碰撞
        public const double ConeCos = 0.5;          // cos 60 度：前／後 60 度錐
        public const double MinSpeed = 0.1;         // 速度下限（上限 0.5、轉向力上限 0.05 寫在 World）
        public const double SeparationRadius = 3.0; // 同隊分離：半徑
        public const double SeparationWeight = 1.5; // 同隊分離：力的倍數

        // ── 手感：每步實際轉向力 = 上一步 × (1 − SteerBlend) + 這一步想要的 × SteerBlend ──
        public const double SteerBlend = 0.2;       // 1 = 不平滑
        public const double EdgeMargin = 7.0;       // 離牆多近開始被推回
        public const double ObstacleMargin = 4.0;   // 離障礙物表面多近開始被推開
        public const double EdgePushWeight = 2.0;   // 推力倍數（1 = 貼牆時剛好一個 MaxForce）
        public const double BounceStrength = 0.6;   // 異隊碰撞彈開時交換的速度比例（1 = 完全彈性）

        // ── 食物與五芒星 ──
        public const double FoodRate = 0.3;         // 每步出現食物的機率
        public const double FoodRadius = 10.0;      // 食物出現在最弱一隊平均位置周圍這個半徑內
        public const int MaxFood = 30;
        public const double StarRate = 0.004;       // 每步出現五芒星的預設機率（GH 的 starRate 輸入可改）
        public const int MaxStars = 2;
        public const int StarSteps = 100;           // 吃到五芒星的無敵步數

        // ── 場地、時間、障礙物 ──
        public const double StartHalf = 30;         // 場地起始半邊長
        public const double EndHalf = 8;            // 縮小期結束時的半邊長
        public const int StepsPerSecond = 30;       // 固定換算：3 分鐘 = 5400 步
        public const double DefaultMinutes = 3;
        public const double StablePercent = 20;     // 階段 1 穩定期（不縮、有障礙物）佔整回合的百分比
        public const double ShrinkPercent = 50;     // 階段 2 縮小期；階段 3 決戰期 = 剩下的（食物不再出現）
        public const int ObstacleCount = 8;
        public const double ObstacleMinHalf = 1.5;  // 障礙物半寬、半高的範圍
        public const double ObstacleMaxHalf = 5.0;
        public const double ObstacleSpread = 24;    // 障礙物中心在 ±這個範圍內

        // ── 防錯 ──
        public const double BudgetMsPerTeamStep = 15;   // 每隊每步所有 Decide 加起來的時間上限
        public const int SlowStrikesToDisqualify = 3;   // 連續幾步超時就淘汰
        public const int BudgetStartStep = 3;           // 前幾步有 JIT 編譯，不算超時
        public const double HardLimitMs = 2000;         // 單步超過這個值直接淘汰
        public const int MaxNameLength = 24;

        // ── 對外狀態（唯讀）──
        public readonly List<Agent> Agents = new List<Agent>();
        public readonly List<Point3d> Foods = new List<Point3d>();
        public readonly List<Point3d> Stars = new List<Point3d>();
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public readonly IList<IBrain> Brains;
        public readonly string[] TeamNames;
        public readonly Color[] TeamColors;      // 老師分配的高對比顏色（淘汰的隊是灰色）
        public readonly bool[] Disqualified;     // 淘汰（出錯、超時、隊名有問題）
        public readonly string[] Status;         // 每隊狀態文字，淘汰時寫原因
        public readonly int[] TeamKills;
        public readonly int Seed;                // 這一場實際用的種子
        public readonly double StarChance;       // 這一場實際用的五芒星每步機率（0～1）
        public readonly int RoundSteps;          // 整回合步數
        public readonly int StableEnd;           // 階段 1 到第幾步結束
        public readonly int ShrinkEnd;           // 階段 2 到第幾步結束
        public int Step { get; private set; }
        public double HalfSize { get; private set; }

        public int Phase
        {
            get { return Step <= StableEnd ? 1 : Step <= ShrinkEnd ? 2 : 3; }
        }

        // 開賽時有 2 隊以上、現在場上只剩 1 隊（或 0 隊）：提早結束
        public bool EndedEarly { get; private set; }
        public bool Finished { get { return Step >= RoundSteps || EndedEarly; } }

        // 勝者：存活最多的隊；同數量比擊殺數；沒有隊伍存活回傳 -1
        public int Winner
        {
            get
            {
                int[] alive = AliveByTeam();
                int best = -1;
                for (int team = 0; team < alive.Length; team++)
                {
                    if (alive[team] == 0) continue;
                    if (best < 0 || alive[team] > alive[best]
                        || (alive[team] == alive[best] && TeamKills[team] > TeamKills[best])) best = team;
                }
                return best;
            }
        }

        public string PhaseName
        {
            get { return Phase == 1 ? "穩定期" : Phase == 2 ? "縮小期" : "決戰期"; }
        }

        // ── 內部狀態 ──
        readonly Random random;           // 食物、場地隨機
        readonly Random starRng;          // 五芒星專用（不動到食物的亂數順序）
        readonly Random[] teamRng;        // 每隊的 self.Rng，每輪由「種子＋隊名＋步數」重建
        readonly int[] teamSeed;
        readonly double[] teamMs;         // 這一步每隊 Decide 累計毫秒
        readonly int[] slowStrikes;       // 每隊連續超時步數
        readonly Relation[][] relation;   // relation[我][對方隊]：開賽前由 Declare 決定
        int startTeams;                   // 開賽時沒被淘汰的隊數

        public Arena(IList<IBrain> brains, int seed, double minutes = DefaultMinutes, double starChance = StarRate)
        {
            if (double.IsNaN(starChance) || double.IsInfinity(starChance)) starChance = StarRate;
            StarChance = Math.Max(0, Math.Min(1, starChance));
            if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes <= 0) minutes = DefaultMinutes;
            minutes = Math.Min(minutes, 60);
            RoundSteps = Math.Max(10, (int)Math.Round(minutes * 60 * StepsPerSecond));
            StableEnd = (int)(RoundSteps * StablePercent / 100.0);
            ShrinkEnd = (int)(RoundSteps * (StablePercent + ShrinkPercent) / 100.0);
            Brains = brains;
            Seed = seed;
            int n = brains.Count;
            random = new Random(seed);
            starRng = new Random(seed ^ 0x57A2F00D);
            TeamNames = new string[n];
            TeamColors = new Color[n];
            Disqualified = new bool[n];
            Status = new string[n];
            TeamKills = new int[n];
            teamRng = new Random[n];
            teamSeed = new int[n];
            teamMs = new double[n];
            slowStrikes = new int[n];
            relation = new Relation[n][];
            HalfSize = StartHalf;

            // 障礙物只由全域種子決定，先建好，隊伍起點會避開它們
            var obstacleRng = new Random(seed ^ 0x0B57AC1E);
            for (int i = 0; i < ObstacleCount; i++)
            {
                var center = new Point3d((obstacleRng.NextDouble() * 2 - 1) * ObstacleSpread,
                    (obstacleRng.NextDouble() * 2 - 1) * ObstacleSpread, 0);
                double halfWidth = ObstacleMinHalf + obstacleRng.NextDouble() * (ObstacleMaxHalf - ObstacleMinHalf);
                double halfHeight = ObstacleMinHalf + obstacleRng.NextDouble() * (ObstacleMaxHalf - ObstacleMinHalf);
                Obstacles.Add(new Obstacle(center, halfWidth, halfHeight));
            }

            var usedNames = new HashSet<string>();
            for (int team = 0; team < n; team++)
            {
                TeamNames[team] = "team" + team;
                TeamColors[team] = Color.Gray;
                Status[team] = "正常";
                // 隊名只讀一次（讀不到或重複就淘汰），之後用快取
                try
                {
                    string name = brains[team].Name;
                    if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name 是空的");
                    name = name.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
                    TeamNames[team] = name;
                }
                catch (Exception ex)
                {
                    Disqualify(team, "Name 讀取失敗：" + Roster.Short(ex));
                    continue;
                }
                if (!usedNames.Add(TeamNames[team]))
                {
                    Disqualify(team, "隊名重複：" + TeamNames[team]);
                    continue;
                }
                // 起點與亂數只由「種子＋隊名」決定，增減其他隊不影響這隊
                int nameHash = StableHash(TeamNames[team]);
                teamSeed[team] = seed ^ nameHash;
                var initRng = new Random(seed ^ nameHash ^ 0x5bd1e995);
                for (int i = 0; i < TeamSize; i++)
                {
                    Point3d p;
                    int tries = 0;
                    do
                    {
                        p = new Point3d((initRng.NextDouble() * 2 - 1) * StartHalf,
                            (initRng.NextDouble() * 2 - 1) * StartHalf, 0);
                    } while (InsideObstacle(p, 0.5) && ++tries < 50);   // 起點不能在障礙物裡
                    var v = new Vector3d(initRng.NextDouble() - 0.5, initRng.NextDouble() - 0.5, 0);
                    v = Steering.Unit(v);
                    if (v.Length == 0) v = Vector3d.XAxis;
                    Agents.Add(new Agent { Team = team, Position = p, Velocity = v * 0.5 });
                }
            }
            DeclareRelations();
            Agents.RemoveAll(a => Disqualified[a.Team]);
            for (int team = 0; team < n; team++) if (!Disqualified[team]) startTeams++;
            AssignColors();
        }

        // ── 隊伍顏色：老師分配，學生不能選 ──
        // 色盤 = 離「畫面固定色」與彼此都最遠的高對比色；依種子洗牌後分給沒被淘汰的隊（同種子同結果）
        static readonly Color[] FixedColors = new Color[]   // 場地邊框灰、食物綠、障礙物棕黃、文字青
        {
            Color.FromArgb(160, 160, 160), Color.FromArgb(50, 205, 50), Color.FromArgb(200, 150, 80), Color.FromArgb(0, 170, 150)
        };
        static readonly Color[] Palette = BuildPalette(Roster.MaxTeams);

        static double ColorDistance(Color a, Color b)
        {
            double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        // 候選色 = RGB 各通道取 0／100／180／255 的組合（去掉灰、太亮、太暗），再逐一挑離已選顏色最遠的
        static Color[] BuildPalette(int count)
        {
            int[] levels = { 0, 100, 180, 255 };
            var candidates = new List<Color>();
            foreach (int r in levels) foreach (int g in levels) foreach (int b in levels)
            {
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                if (max - min < 45 || luma > 215 || luma < 25) continue;
                candidates.Add(Color.FromArgb(r, g, b));
            }
            var used = new List<Color>(FixedColors);
            var chosen = new List<Color>();
            while (chosen.Count < count && candidates.Count > 0)
            {
                Color best = candidates[0];
                double bestGap = -1;
                foreach (Color candidate in candidates)
                {
                    double gap = double.MaxValue;
                    foreach (Color u in used) gap = Math.Min(gap, ColorDistance(candidate, u));
                    if (gap > bestGap) { bestGap = gap; best = candidate; }
                }
                chosen.Add(best);
                used.Add(best);
                candidates.Remove(best);
            }
            return chosen.ToArray();
        }

        // 依種子洗牌色盤，依序分給沒被淘汰的隊；淘汰的隊是灰色
        void AssignColors()
        {
            var order = new List<Color>(Palette);
            var colorRng = new Random(Seed ^ 0x0C0102E5);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = colorRng.Next(i + 1);
                Color tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }
            int next = 0;
            for (int team = 0; team < TeamColors.Length; team++)
            {
                if (Disqualified[team]) { TeamColors[team] = Color.Gray; continue; }
                TeamColors[team] = order[next % order.Count];
                next++;
            }
        }

        // 開賽前問每隊 Declare 一次（友軍／敵軍／不相關）；沒設定：同隊友軍、異隊敵軍；Declare 出錯就淘汰
        void DeclareRelations()
        {
            var names = new List<string>();
            for (int team = 0; team < Brains.Count; team++) if (!Disqualified[team]) names.Add(TeamNames[team]);
            for (int team = 0; team < Brains.Count; team++)
            {
                relation[team] = new Relation[Brains.Count];
                var table = new RelationTable(TeamNames[team], names);
                var declarer = Brains[team] as IDeclareRelations;
                if (declarer != null && !Disqualified[team])
                {
                    try { declarer.Declare(table); }
                    catch (Exception ex) { Disqualify(team, "Declare 出錯：" + Roster.Short(ex)); }
                }
                for (int other = 0; other < Brains.Count; other++)
                    relation[team][other] = other == team ? Relation.Ally : table.Get(TeamNames[other]);
            }
        }

        bool Invincible(Agent a)
        {
            return Step <= a.InvincibleUntil;
        }

        // 開賽有 2 隊以上，現在場上只剩 1 隊（或 0 隊）
        bool OnlyOneTeamLeft()
        {
            if (startTeams < 2) return false;
            int teamsAlive = 0;
            foreach (int count in AliveByTeam()) if (count > 0) teamsAlive++;
            return teamsAlive <= 1;
        }

        static int StableHash(string text)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in text) { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }

        // 食物出現在「存活最少（不是 0）的隊」平均位置周圍 FoodRadius 的圓內（扶弱；同數量取編號小的）
        Point3d FoodPoint()
        {
            int[] counts = AliveByTeam();
            int weakest = -1;
            for (int t = 0; t < counts.Length; t++)
                if (counts[t] > 0 && (weakest < 0 || counts[t] < counts[weakest])) weakest = t;
            if (weakest < 0) return RandomPoint(HalfSize);
            double sx = 0, sy = 0;
            foreach (var a in Agents)
                if (a.Team == weakest) { sx += a.Position.X; sy += a.Position.Y; }
            sx /= counts[weakest]; sy /= counts[weakest];
            double angle = random.NextDouble() * 2 * Math.PI;
            double radius = FoodRadius * Math.Sqrt(random.NextDouble());   // 圓內均勻分布
            double limit = HalfSize - 0.5;
            return new Point3d(Math.Max(-limit, Math.Min(limit, sx + radius * Math.Cos(angle))),
                Math.Max(-limit, Math.Min(limit, sy + radius * Math.Sin(angle))), 0);
        }

        public int AliveCount { get { return Agents.Count; } }

        public int[] AliveByTeam()
        {
            var counts = new int[Brains.Count];
            foreach (var a in Agents) counts[a.Team]++;
            return counts;
        }

        // 淘汰一隊：整隊從場上消失，原因記在 Status，其他隊照常
        void Disqualify(int team, string reason)
        {
            if (Disqualified[team]) return;
            Disqualified[team] = true;
            Status[team] = "淘汰：" + reason;
        }

        bool InsideObstacle(Point3d p, double margin)
        {
            foreach (var o in Obstacles)
                if (o.DistanceTo(p) <= margin) return true;
            return false;
        }

        static bool OutsideField(Point3d p, double half)
        {
            return Math.Abs(p.X) > half || Math.Abs(p.Y) > half || Math.Abs(p.Z) > half;
        }

        Point3d RandomPoint(double half)
        {
            return new Point3d((random.NextDouble() * 2 - 1) * half, (random.NextDouble() * 2 - 1) * half, 0);
        }

        static bool IsBad(Vector3d v)
        {
            return double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z)
                || double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z);
        }

        public void RunStep()
        {
            if (Finished) return;
            Step++;
            // 場地：階段 1 不縮，階段 2 線性縮到 EndHalf，階段 3 維持
            double t = Math.Max(0.0, Math.Min(1.0, (Step - StableEnd) / (double)Math.Max(1, ShrinkEnd - StableEnd)));
            HalfSize = StartHalf + (EndHalf - StartHalf) * t;
            // 中心跑到場地外的障礙物、食物、五芒星移除
            Obstacles.RemoveAll(o => OutsideField(o.Center, HalfSize));
            Foods.RemoveAll(f => OutsideField(f, HalfSize));
            Stars.RemoveAll(f => OutsideField(f, HalfSize));

            // 每隊拿自己的唯讀 World 複本
            var obstacleView = new System.Collections.ObjectModel.ReadOnlyCollection<Obstacle>(Obstacles.ToArray());
            int phase = Phase;
            var worlds = new World[Brains.Count];
            for (int team = 0; team < worlds.Length; team++)
                worlds[team] = new World(Point3d.Origin, HalfSize, Step, 10, 0.5, 0.05, obstacleView, phase);
            var engineWorld = new World(Point3d.Origin, HalfSize, Step, 10, 0.5, 0.05, obstacleView, phase);

            var agents = Agents;

            // 步驟 1 轉向力：全部用步初快照；有隊伍出錯就整隊移除後重算，出錯的隊在這一步像不存在
            Vector3d[] steer;
            while (true)
            {
                agents.RemoveAll(a => Disqualified[a.Team]);
                if (ComputeSteer(agents, worlds, engineWorld, out steer)) break;
            }

            // 超時檢查：連續超過預算就淘汰
            for (int team = 0; team < Brains.Count; team++)
            {
                if (Disqualified[team] || Step <= BudgetStartStep) continue;
                if (teamMs[team] > BudgetMsPerTeamStep) slowStrikes[team]++;
                else slowStrikes[team] = 0;
                if (slowStrikes[team] >= SlowStrikesToDisqualify)
                    Disqualify(team, "運算太慢（每步 " + teamMs[team].ToString("F0") + " 毫秒）");
            }
            var survivorSteer = new List<Vector3d>(agents.Count);
            for (int i = 0; i < agents.Count; i++) survivorSteer.Add(steer[i]);
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                if (!Disqualified[agents[i].Team]) continue;
                agents.RemoveAt(i);
                survivorSteer.RemoveAt(i);
            }

            // 步驟 2 運動：速度與力的上限由引擎強制
            for (int i = 0; i < agents.Count; i++)
            {
                Agent a = agents[i];
                // Z 分量丟掉；轉向力用 SteerBlend 平滑（加權平均，不會超過 MaxForce）
                a.Steer = a.Steer * (1 - SteerBlend) + new Vector3d(survivorSteer[i].X, survivorSteer[i].Y, 0) * SteerBlend;
                a.Velocity += a.Steer;
                a.Velocity = new Vector3d(a.Velocity.X, a.Velocity.Y, 0);
                double speed = a.Velocity.Length;
                if (speed > engineWorld.MaxSpeed) a.Velocity *= engineWorld.MaxSpeed / speed;
                else if (speed < MinSpeed)
                    a.Velocity = (speed > 1e-9 ? Steering.Unit(a.Velocity) : Vector3d.XAxis) * MinSpeed;
                a.Position += a.Velocity;
                BounceObstacles(a);
                BounceWalls(a);
            }

            // 步驟 3 碰撞：先全部判定、再一起處理，結果不受順序影響
            var killed = new HashSet<Agent>();
            var bounces = new List<KeyValuePair<Agent, Agent>>();
            for (int i = 0; i < agents.Count; i++)
            {
                for (int j = i + 1; j < agents.Count; j++)
                {
                    Agent a = agents[i], b = agents[j];
                    if (a.Team == b.Team) continue;
                    Vector3d d = b.Position - a.Position;
                    double dist = d.Length;
                    if (dist >= ContactDistance || dist < 1e-9) continue;
                    d /= dist;
                    Vector3d av = Steering.Unit(a.Velocity), bv = Steering.Unit(b.Velocity);
                    bool aFront = av * d >= ConeCos;       // b 在 a 的前方 60 度錐內
                    bool aRear = av * d <= -ConeCos;       // b 在 a 的後方 60 度錐內
                    bool bFront = bv * d <= -ConeCos;      // a 在 b 的前方 60 度錐內
                    bool bRear = bv * d >= ConeCos;        // a 在 b 的後方 60 度錐內
                    if (aFront && bRear)
                    {
                        if (Invincible(b)) bounces.Add(new KeyValuePair<Agent, Agent>(a, b));   // 無敵：只彈開
                        else { killed.Add(b); Credit(a); }
                    }
                    else if (bFront && aRear)
                    {
                        if (Invincible(a)) bounces.Add(new KeyValuePair<Agent, Agent>(a, b));
                        else { killed.Add(a); Credit(b); }
                    }
                    else if (aRear && bRear) { /* 背對背：不處理 */ }
                    else bounces.Add(new KeyValuePair<Agent, Agent>(a, b));   // 正面對撞、側邊：互相彈開
                }
            }
            foreach (var pair in bounces)
                if (!killed.Contains(pair.Key) && !killed.Contains(pair.Value)) Bounce(pair.Key, pair.Value);
            agents.RemoveAll(a => killed.Contains(a));

            // 步驟 4 食物：先擊殺（空出名額）再吃；滿 Cap 吃不了，食物留著；階段 3 不再出現（亂數照抽以維持順序）
            double foodRoll = random.NextDouble();
            if (phase < 3 && foodRoll < FoodRate && Foods.Count < MaxFood)
            {
                Point3d food;
                int tries = 0;
                do { food = FoodPoint(); } while (InsideObstacle(food, 0) && ++tries < 20);
                Foods.Add(food);
            }
            var ate = new HashSet<Agent>();
            var born = new List<Agent>();
            for (int f = Foods.Count - 1; f >= 0; f--)
            {
                if (agents.Count + born.Count >= Cap) break;
                Agent best = null;
                double bestDist = ContactDistance;
                foreach (var a in agents)
                {
                    if (ate.Contains(a)) continue;
                    double d = a.Position.DistanceTo(Foods[f]);
                    if (d < bestDist) { bestDist = d; best = a; }
                }
                if (best == null) continue;
                ate.Add(best);
                Foods.RemoveAt(f);
                Point3d bornAt = best.Position + Steering.Unit(-best.Velocity) * 0.5;
                if (InsideObstacle(bornAt, 0) || OutsideField(bornAt, HalfSize)) bornAt = best.Position;
                born.Add(new Agent { Team = best.Team, Position = bornAt, Velocity = best.Velocity });
            }
            agents.AddRange(born);

            // 步驟 5 五芒星：用專用亂數；碰到的那隻無敵 StarSteps 步
            double starRoll = starRng.NextDouble();
            if (phase < 3 && starRoll < StarChance && Stars.Count < MaxStars)
            {
                Point3d star;
                int starTries = 0;
                do
                {
                    star = new Point3d((starRng.NextDouble() * 2 - 1) * HalfSize, (starRng.NextDouble() * 2 - 1) * HalfSize, 0);
                } while (InsideObstacle(star, 0.5) && ++starTries < 20);
                Stars.Add(star);
            }
            for (int s = Stars.Count - 1; s >= 0; s--)
            {
                Agent taker = null;
                double takerDist = ContactDistance;
                foreach (var a in agents)
                {
                    double d = a.Position.DistanceTo(Stars[s]);
                    if (d < takerDist) { takerDist = d; taker = a; }
                }
                if (taker == null) continue;
                taker.InvincibleUntil = Step + StarSteps;
                Stars.RemoveAt(s);
            }
            if (OnlyOneTeamLeft()) EndedEarly = true;
        }

        // 算這一步每隻的轉向力；有隊伍出錯就回傳 false（呼叫端移除該隊後重算）
        bool ComputeSteer(List<Agent> agents, World[] worlds, World engineWorld, out Vector3d[] steer)
        {
            steer = new Vector3d[agents.Count];
            Array.Clear(teamMs, 0, teamMs.Length);
            // 每輪重建亂數，重算時好隊抽到的亂數和第一次相同
            for (int t = 0; t < teamRng.Length; t++)
                teamRng[t] = new Random(unchecked(teamSeed[t] ^ (Step * (int)0x9E3779B1)));
            for (int i = 0; i < agents.Count; i++)
            {
                Agent me = agents[i];
                var neighbors = new List<Neighbor>();
                Vector3d away = Vector3d.Zero;
                for (int j = 0; j < agents.Count; j++)
                {
                    if (i == j) continue;
                    Agent other = agents[j];
                    double d = me.Position.DistanceTo(other.Position);
                    bool friend = other.Team == me.Team;
                    if (friend && d > 1e-6 && d < SeparationRadius)
                        away += (me.Position - other.Position) / (d * d);
                    if (d < engineWorld.ViewRadius)
                        neighbors.Add(new Neighbor { Position = other.Position, Velocity = other.Velocity,
                            Team = other.Team, Name = TeamNames[other.Team], Relation = relation[me.Team][other.Team],
                            IsFriend = friend, Distance = d,
                            InvincibleSteps = Math.Max(0, other.InvincibleUntil - Step + 1) });
                }
                var foods = new List<Point3d>();
                foreach (var f in Foods) if (me.Position.DistanceTo(f) < engineWorld.ViewRadius) foods.Add(f);
                var stars = new List<Point3d>();
                foreach (var f in Stars) if (me.Position.DistanceTo(f) < engineWorld.ViewRadius) stars.Add(f);

                Vector3d want;
                var timer = Stopwatch.StartNew();
                try
                {
                    want = Brains[me.Team].Decide(new SelfInfo { Position = me.Position, Velocity = me.Velocity,
                        Team = me.Team, Name = TeamNames[me.Team], Rng = teamRng[me.Team],
                        InvincibleSteps = Math.Max(0, me.InvincibleUntil - Step + 1), Stars = stars }, neighbors, foods, worlds[me.Team]);
                    if (IsBad(want)) throw new InvalidOperationException("回傳 NaN 或無限大");
                }
                catch (Exception ex)
                {
                    Disqualify(me.Team, Roster.Short(ex));
                    return false;
                }
                teamMs[me.Team] += timer.Elapsed.TotalMilliseconds;
                if (teamMs[me.Team] > HardLimitMs)
                {
                    Disqualify(me.Team, "運算時間過長");
                    return false;
                }

                // 引擎加上：同隊分離、靠邊推回
                want += Steering.Seek(away, me.Velocity, engineWorld) * SeparationWeight;
                want += EdgePush(me, engineWorld);
                if (want.Length > engineWorld.MaxForce) want *= engineWorld.MaxForce / want.Length;
                steer[i] = want;
            }
            return true;
        }

        // 靠牆、靠障礙物時輕輕推回（越近越大），只推還在往它衝的方向
        Vector3d EdgePush(Agent me, World world)
        {
            Vector3d push = Vector3d.Zero;
            double toWallX = world.HalfSize - Math.Abs(me.Position.X), toWallY = world.HalfSize - Math.Abs(me.Position.Y);
            if (toWallX < EdgeMargin && me.Velocity.X * me.Position.X > 0)
                push += new Vector3d(-Math.Sign(me.Position.X), 0, 0) * (1 - Math.Max(toWallX, 0) / EdgeMargin);
            if (toWallY < EdgeMargin && me.Velocity.Y * me.Position.Y > 0)
                push += new Vector3d(0, -Math.Sign(me.Position.Y), 0) * (1 - Math.Max(toWallY, 0) / EdgeMargin);
            foreach (var o in Obstacles)
            {
                double distance = o.DistanceTo(me.Position);
                if (distance >= ObstacleMargin || distance < 1e-9) continue;
                Vector3d outward = Steering.Unit(me.Position - o.ClosestPoint(me.Position));
                if (outward * me.Velocity < 0)   // 正在往障礙物靠近
                    push += outward * (1 - distance / ObstacleMargin);
            }
            return push * world.MaxForce * EdgePushWeight;
        }

        void Credit(Agent killer)
        {
            TeamKills[killer.Team]++;
        }

        // 撞到矩形障礙物：從穿透最少的面推回表面，朝內的速度沿法線反射
        void BounceObstacles(Agent a)
        {
            foreach (var o in Obstacles)
            {
                double dx = a.Position.X - o.Center.X, dy = a.Position.Y - o.Center.Y;
                double penX = o.HalfWidth - Math.Abs(dx), penY = o.HalfHeight - Math.Abs(dy);
                if (penX <= 0 || penY <= 0) continue;
                Vector3d n;
                if (penX < penY)
                {
                    n = new Vector3d(dx < 0 ? -1 : 1, 0, 0);
                    a.Position = new Point3d(o.Center.X + n.X * (o.HalfWidth + 1e-6), a.Position.Y, 0);
                }
                else
                {
                    n = new Vector3d(0, dy < 0 ? -1 : 1, 0);
                    a.Position = new Point3d(a.Position.X, o.Center.Y + n.Y * (o.HalfHeight + 1e-6), 0);
                }
                double vn = a.Velocity * n;
                if (vn < 0) { a.Velocity -= 2 * vn * n; a.Steer = Vector3d.Zero; }
            }
        }

        void BounceWalls(Agent a)
        {
            double h = HalfSize;
            double x = a.Position.X, y = a.Position.Y, z = a.Position.Z;
            double vx = a.Velocity.X, vy = a.Velocity.Y, vz = a.Velocity.Z;
            Wall(ref x, ref vx, h);
            Wall(ref y, ref vy, h);
            Wall(ref z, ref vz, h);
            if (vx != a.Velocity.X || vy != a.Velocity.Y) a.Steer = Vector3d.Zero;   // 反彈後舊轉向力作廢
            a.Position = new Point3d(x, y, z);
            a.Velocity = new Vector3d(vx, vy, vz);
        }

        static void Wall(ref double p, ref double v, double h)
        {
            if (p > h) { p = h; v = -Math.Abs(v); }
            else if (p < -h) { p = -h; v = Math.Abs(v); }
        }

        // 等質量碰撞：沿連線交換 BounceStrength 的速度，再推開到剛好不碰
        static void Bounce(Agent a, Agent b)
        {
            Vector3d n = Steering.Unit(b.Position - a.Position);
            if (n.Length == 0) return;
            double va = a.Velocity * n, vb = b.Velocity * n;
            if (va - vb > 0)
            {
                a.Velocity += (vb - va) * n * BounceStrength;
                b.Velocity += (va - vb) * n * BounceStrength;
                a.Steer = Vector3d.Zero;
                b.Steer = Vector3d.Zero;
            }
            double overlap = ContactDistance - a.Position.DistanceTo(b.Position);
            if (overlap > 0)
            {
                a.Position -= n * (overlap / 2 + 1e-6);
                b.Position += n * (overlap / 2 + 1e-6);
            }
        }
    }
}
