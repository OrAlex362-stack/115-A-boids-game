using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;

namespace BoidArena
{
    // 競技場的 GH 外殼，只有老師改：建立比賽（Setup）、往前跑並排程下一次運算（Run）、畫在視埠（Draw）
    // GH 的 C# Script 只留 #load 與三行呼叫，細節都在這裡
    public class GameHost
    {
        // ── 畫面與播放設定 ──
        static readonly Color HeaderColor = Color.FromArgb(0, 170, 150);   // 不屬於任何隊的文字
        const int ScheduleMs = 10;         // 自動前進時兩次運算的間隔
        const int MaxCatchUpSteps = 4;     // 一次運算最多補幾步（電腦太慢就比即時慢）
        const double BoidScale = 1.0;      // Boid 圖形整體縮放（只改畫面，不影響碰撞與擊殺）
        const double BoidLength = 1.2;     // 圖形頭到尾的長度（固定大小，只表示位置與方向）
        const int OutlineThickness = 1;    // 外框像素；無敵時圖形長寬與外框都是兩倍
        const double StarRadius = 1.2;     // 五芒星外圍半徑
        const int MaxLineChars = 60;       // 文字每行最多幾個字
        // Boid 圖形：封閉折線＋填色的飛鏢；局部座標 +X = 前進方向、+Y = 逆時針 90 度；X 方向正中央放在 Boid 位置
        static readonly double[,] ShapeLocal = new double[,] { { 1, 0 }, { -0.656112, 0.754664 }, { 0, 0 }, { -0.656112, -0.754664 } };
        const double ShapeMinX = -0.656112, ShapeMaxX = 1.0;

        // ── 跨次運算保留的狀態 ──
        Arena arena;
        List<string> rejected = new List<string>();
        string engineError = "";
        bool scheduled;
        bool justReset;                    // 這次運算剛重來，不前進
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastTick;                   // 上次運算的時間（秒）
        double playSeconds;                // 累積播放時間，只有 run 為 True 才增加

        // 沒有輸出幾何時，GH 靠這個才知道有東西可畫（Zoom Extents 也靠它抓到場地）
        public BoundingBox Box
        {
            get
            {
                double h = Arena.StartHalf + BoidLength * Math.Max(1.0, BoidScale) * 2;
                return new BoundingBox(-h, -h, -1, h, h, 1);
            }
        }

        // 建立比賽：reset 為 True 或還沒建過時，從 assembly 找出所有 Boid_ class 開新的一場
        public void Setup(bool reset, object seed, double minutes, object starRate, Assembly assembly)
        {
            justReset = false;
            if (!reset && arena != null) return;
            try
            {
                List<IBrain> brains = Roster.Find(assembly, out rejected);
                arena = new Arena(brains, SeedValue(seed), minutes, StarChanceValue(starRate));
                engineError = "";
            }
            catch (Exception ex)
            {
                engineError = ex.GetType().Name + ": " + ex.Message;
            }
            playSeconds = 0;
            justReset = true;
        }

        // 跑比賽：run 為 True 時照即時速度前進並自己排下一次運算（不用 Timer）；回傳畫面文字
        public string Run(bool run, IGH_Component component)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - lastTick, 0.2);   // 暫停很久再開始時不要一次補很多
            lastTick = now;
            if (arena == null) return "引擎建立失敗：" + engineError;
            if (!justReset && run && engineError == "" && !arena.Finished)
            {
                try
                {
                    playSeconds += dt;
                    int target = (int)(playSeconds * Arena.StepsPerSecond);
                    int done = 0;
                    while (arena.Step < target && done < MaxCatchUpSteps && !arena.Finished)
                    {
                        arena.RunStep();
                        done++;
                    }
                    if (arena.Step < target) playSeconds = arena.Step / (double)Arena.StepsPerSecond;
                }
                catch (Exception ex)
                {
                    engineError = ex.GetType().Name + ": " + ex.Message;   // 停在目前這一步，不讓 GH 爆掉
                }
            }
            // scheduled 避免按 reset 時排出兩條
            if (run && engineError == "" && !arena.Finished && !scheduled)
            {
                GH_Document doc = component.OnPingDocument();
                if (doc != null)
                {
                    scheduled = true;
                    doc.ScheduleSolution(ScheduleMs, d =>
                    {
                        scheduled = false;
                        component.ExpireSolution(false);
                    });
                }
            }
            return string.Join("\n", InfoLines().Select(line => line.Key));
        }

        // seed、starRate 輸入不設型別提示，所以自己轉型；不接、不是數字、seed 為 0 都用預設
        static int SeedValue(object raw)
        {
            IGH_Goo goo = raw as IGH_Goo;
            if (goo != null) raw = goo.ScriptVariable();
            try { int value = Convert.ToInt32(raw); return value != 0 ? value : Arena.GlobalSeed; }
            catch (Exception) { return Arena.GlobalSeed; }
        }

        static double StarChanceValue(object raw)
        {
            IGH_Goo goo = raw as IGH_Goo;
            if (goo != null) raw = goo.ScriptVariable();
            if (raw == null) return Arena.StarRate;
            try { return Convert.ToDouble(raw); }
            catch (Exception) { return Arena.StarRate; }
        }

        // 畫面文字：每行配一個隊伍編號（-1 = 不屬於任何隊）
        List<KeyValuePair<string, int>> InfoLines()
        {
            int[] alive = arena.AliveByTeam();
            var lines = new List<KeyValuePair<string, int>>();
            int secondsLeft = (int)Math.Ceiling((arena.RoundSteps - arena.Step) / (double)Arena.StepsPerSecond);
            lines.Add(new KeyValuePair<string, int>(string.Format("階段 {0}/3 {1}　剩餘 {2}:{3:00}　步 {4}/{5}　種子 {6}",
                arena.Phase, arena.PhaseName, secondsLeft / 60, secondsLeft % 60, arena.Step, arena.RoundSteps,
                arena.Seed), -1));
            lines.Add(new KeyValuePair<string, int>(string.Format("總數 {0}/{1}　食物 {2}　障礙物 {3}　場地半邊 {4:F1}",
                arena.AliveCount, Arena.Cap, arena.Foods.Count, arena.Obstacles.Count, arena.HalfSize), -1));
            if (arena.Finished)
            {
                int winner = arena.Winner;
                lines.Add(new KeyValuePair<string, int>(winner < 0 ? "回合結束：沒有隊伍存活"
                    : string.Format("回合結束{2}　勝者：{0}（存活 {1}）", arena.TeamNames[winner], alive[winner],
                        arena.EndedEarly ? "（只剩一隊，提早結束）" : ""), winner));
            }
            if (engineError != "") lines.Add(new KeyValuePair<string, int>("引擎錯誤（已暫停）：" + engineError, -1));
            // 排名：存活越多越上面；同存活比擊殺數；都一樣就同名次（照隊伍原本順序排）
            var order = Enumerable.Range(0, arena.Brains.Count)
                .OrderByDescending(t => alive[t]).ThenByDescending(t => arena.TeamKills[t]).ThenBy(t => t).ToList();
            foreach (int team in order)
            {
                int rank = 1 + order.Count(t => alive[t] > alive[team] || (alive[t] == alive[team] && arena.TeamKills[t] > arena.TeamKills[team]));
                lines.Add(new KeyValuePair<string, int>(string.Format("{0}. {1}　存活 {2}　擊殺 {3}　{4}",
                    rank, arena.TeamNames[team], alive[team], arena.TeamKills[team], arena.Status[team]), team));
            }
            foreach (string text in rejected) lines.Add(new KeyValuePair<string, int>("未參賽：" + text, -1));
            return lines;
        }

        // 畫在視埠：場地、障礙物、Boid、五芒星、食物、左上角文字
        public void Draw(DisplayPipeline display)
        {
            if (arena == null) return;

            double h = arena.HalfSize;
            display.DrawPolyline(new Polyline(new Point3d[] { new Point3d(-h, -h, 0), new Point3d(h, -h, 0), new Point3d(h, h, 0),
                new Point3d(-h, h, 0), new Point3d(-h, -h, 0) }), Color.FromArgb(160, 160, 160), 2);

            foreach (Obstacle o in arena.Obstacles)
            {
                double w = o.HalfWidth, hh = o.HalfHeight;
                Point3d c = o.Center;
                display.DrawPolyline(new Polyline(new Point3d[] { new Point3d(c.X - w, c.Y - hh, 0), new Point3d(c.X + w, c.Y - hh, 0),
                    new Point3d(c.X + w, c.Y + hh, 0), new Point3d(c.X - w, c.Y + hh, 0), new Point3d(c.X - w, c.Y - hh, 0) }),
                    Color.FromArgb(200, 150, 80), 2);
            }

            foreach (Agent agent in arena.Agents)
            {
                if (agent.Velocity.Length < 1e-9) continue;
                Vector3d direction = Steering.Unit(agent.Velocity);
                double scale = arena.Step <= agent.InvincibleUntil ? 2.0 : 1.0;   // 無敵中：圖形與外框兩倍
                double unit = BoidLength * BoidScale * scale / (ShapeMaxX - ShapeMinX);
                double centerX = (ShapeMinX + ShapeMaxX) / 2;
                Vector3d side = new Vector3d(-direction.Y, direction.X, 0);
                var corners = new Point3d[ShapeLocal.GetLength(0)];
                for (int i = 0; i < corners.Length; i++)
                    corners[i] = agent.Position + direction * ((ShapeLocal[i, 0] - centerX) * unit) + side * (ShapeLocal[i, 1] * unit);
                Color color = arena.TeamColors[agent.Team];
                // 填色：飛鏢中間凹進去，拆成兩個三角形
                display.DrawPolygon(new Point3d[] { corners[0], corners[1], corners[2] }, color, true);
                display.DrawPolygon(new Point3d[] { corners[0], corners[2], corners[3] }, color, true);
                // 封閉折線外框
                display.DrawPolyline(new Polyline(new Point3d[] { corners[0], corners[1], corners[2], corners[3], corners[0] }),
                    color, OutlineThickness * (int)scale);
            }

            foreach (Point3d star in arena.Stars)
            {
                var pts = new Point3d[6];   // 外圍 5 個點，隔一個連一次
                for (int i = 0; i <= 5; i++)
                {
                    double angle = Math.PI / 2 + (i * 2) % 5 * 2 * Math.PI / 5;
                    pts[i] = new Point3d(star.X + StarRadius * Math.Cos(angle), star.Y + StarRadius * Math.Sin(angle), 0);
                }
                display.DrawPolyline(new Polyline(pts), Color.Gold, 3);
            }

            foreach (Point3d food in arena.Foods)
                display.DrawPoint(food, PointStyle.RoundSimple, 5, Color.LimeGreen);

            int y = 20;
            foreach (KeyValuePair<string, int> line in InfoLines())
            {
                Color color = HeaderColor;
                int team = line.Value;
                if (team >= 0 && team < arena.Brains.Count)
                    color = arena.Disqualified[team] ? Color.Gray : arena.TeamColors[team];
                string text = line.Key;
                if (text.Length > MaxLineChars) text = text.Substring(0, MaxLineChars) + "…";
                display.Draw2dText(text, color, new Point2d(20, y), false, 16);
                y += 22;
            }
        }
    }
}
