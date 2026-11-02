//@LOADS@
// Boids 競技場主程式：GH C# Script 的樣板，只有老師改；由 tools\gen_host.py 產生 host_script.cs 後貼進 GH
// 輸入（seed、minutes、starRate 改了要按 reset 才生效；不接就用預設）
//   reset     bool    重來（接 Button）
//   run       bool    True 自動連續前進、False 暫停（接 Boolean Toggle，不需要 Timer）
//   seed      object  亂數種子；不接或 0 = Arena.GlobalSeed。同種子＋同學生檔，結果相同
//   minutes   double  一回合分鐘數；不接或 <= 0 = 3
//   starRate  object  五芒星每步出現機率 0～1；不接 = Arena.StarRate，0 = 不出現
// 輸出：只有 info（除錯文字）；場地、障礙物、Boid、食物、五芒星、文字全部由本程式畫在視埠，其他輸出不用接
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using BoidArena;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    // ── 跨次運算保留的狀態 ──
    private Arena arena = null;
    private List<string> rejected = new List<string>();
    private string engineError = "";
    private bool scheduled = false;
    private System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private double lastTick = 0;        // 上次運算的時間（秒）
    private double playSeconds = 0;     // 累積的播放時間，只有 run 為 True 才增加

    // ── 畫面與播放設定 ──
    private static readonly Color HeaderColor = Color.FromArgb(0, 170, 150);   // 不屬於任何隊的文字
    private const int ScheduleMs = 10;         // 自動前進時兩次運算的間隔
    private const int MaxCatchUpSteps = 4;     // 一次運算最多補幾步（電腦太慢就比即時慢）
    private const double BoidScale = 1.0;      // Boid 圖形整體縮放（只改畫面，不影響碰撞與擊殺）
    private const double BoidLength = 1.2;     // 圖形頭到尾的長度（固定大小，只表示位置與方向）
    private const int OutlineThickness = 1;    // 外框像素；無敵時圖形長寬與外框都是兩倍
    private const double StarRadius = 1.2;     // 五芒星外圍半徑
    private const int MaxLineChars = 60;       // 文字每行最多幾個字
    // Boid 圖形：封閉折線＋填色的飛鏢；局部座標 +X = 前進方向、+Y = 逆時針 90 度；X 方向正中央放在 Boid 位置
    private static readonly double[,] ShapeLocal = new double[,] { { 1, 0 }, { -0.656112, 0.754664 }, { 0, 0 }, { -0.656112, -0.754664 } };
    private const double ShapeMinX = -0.656112, ShapeMaxX = 1.0;

    // seed、starRate 輸入不設型別提示，所以自己轉型
    private static int SeedValue(object raw)
    {
        IGH_Goo goo = raw as IGH_Goo;
        if (goo != null) raw = goo.ScriptVariable();
        try { int value = Convert.ToInt32(raw); return value != 0 ? value : Arena.GlobalSeed; }
        catch (Exception) { return Arena.GlobalSeed; }
    }

    private static double StarChanceValue(object raw)
    {
        IGH_Goo goo = raw as IGH_Goo;
        if (goo != null) raw = goo.ScriptVariable();
        if (raw == null) return Arena.StarRate;
        try { return Convert.ToDouble(raw); }
        catch (Exception) { return Arena.StarRate; }
    }

    private void RunScript(
		bool reset,
		bool run,
		object seed,
		double minutes,
		object starRate,
		ref object points,
		ref object colors,
		ref object foods,
		ref object box,
		ref object info)
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - lastTick, 0.2);   // 暫停很久再開始時不要一次補很多
        lastTick = now;
        try
        {
            if (reset || arena == null)
            {
                List<IBrain> brains = Roster.Find(typeof(Script_Instance).Assembly, out rejected);
                arena = new Arena(brains, SeedValue(seed), minutes, StarChanceValue(starRate));
                playSeconds = 0;
                engineError = "";
            }
            else if (run && engineError == "" && !arena.Finished)
            {
                // 照即時速度播放：一回合的分鐘數 ≈ 真的跑那麼久（電腦太慢就比較久，結果不變）
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
        }
        catch (Exception ex)
        {
            // 引擎自己出錯：停在目前這一步，不讓 GH 整個爆掉
            engineError = ex.GetType().Name + ": " + ex.Message;
        }
        if (arena == null)
        {
            info = "引擎建立失敗：" + engineError;
            return;
        }
        info = string.Join("\n", InfoLines().Select(line => line.Key));
        // 輸出全是 null 時，GH 認為這個元件沒有東西可預覽，不會呼叫 DrawViewportWires；給一個原點讓預覽開啟
        points = Point3d.Origin;

        // run 為 True 時自己排下一次運算，不用接 Timer；scheduled 避免按 reset 時排出兩條
        if (run && engineError == "" && !arena.Finished && !scheduled)
        {
            GH_Document doc = Component.OnPingDocument();
            if (doc != null)
            {
                scheduled = true;
                doc.ScheduleSolution(ScheduleMs, d =>
                {
                    scheduled = false;
                    Component.ExpireSolution(false);
                });
            }
        }
    }

    // 畫面上的文字：每行配一個隊伍編號（-1 表示不屬於任何隊，用黑色）
    private List<KeyValuePair<string, int>> InfoLines()
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
                : string.Format("回合結束{2}　勝者：{0}（存活 {1}）", arena.TeamNames[winner], alive[winner], arena.EndedEarly ? "（只剩一隊，提早結束）" : ""), winner));
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

    // 沒有輸出幾何時，GH 要靠這個才知道這個元件有東西可畫（也讓 Zoom Extents 抓得到場地）
    public override BoundingBox ClippingBox
    {
        get
        {
            double h = Arena.StartHalf + BoidLength * Math.Max(1.0, BoidScale) * 2;
            return new BoundingBox(-h, -h, -1, h, h, 1);
        }
    }

    // 場地方塊、障礙物、每隻 Boid 的飛鏢（封閉折線＋填色，顏色是主程式分配給該隊的）、食物、左上角文字，全部畫在視埠
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        if (arena == null) return;
        DisplayPipeline display = args.Display;

        double h = arena.HalfSize;
        var square = new Polyline(new Point3d[] { new Point3d(-h, -h, 0), new Point3d(h, -h, 0), new Point3d(h, h, 0),
            new Point3d(-h, h, 0), new Point3d(-h, -h, 0) });
        display.DrawPolyline(square, Color.FromArgb(160, 160, 160), 2);

        foreach (Obstacle obstacle in arena.Obstacles)
        {
            double w = obstacle.HalfWidth, hh = obstacle.HalfHeight;
            Point3d c = obstacle.Center;
            display.DrawPolyline(new Polyline(new Point3d[] { new Point3d(c.X - w, c.Y - hh, 0), new Point3d(c.X + w, c.Y - hh, 0),
                new Point3d(c.X + w, c.Y + hh, 0), new Point3d(c.X - w, c.Y + hh, 0), new Point3d(c.X - w, c.Y - hh, 0) }),
                Color.FromArgb(200, 150, 80), 2);
        }

        foreach (Agent agent in arena.Agents)
        {
            if (agent.Velocity.Length < 1e-9) continue;
            Vector3d direction = Steering.Unit(agent.Velocity);
            // 吃到五芒星、還在無敵中：圖形兩倍大、外框兩倍粗
            double scale = arena.Step <= agent.InvincibleUntil ? 2.0 : 1.0;
            double unit = BoidLength * BoidScale * scale / (ShapeMaxX - ShapeMinX);
            double centerX = (ShapeMinX + ShapeMaxX) / 2;   // 圖形正中央就是 Boid 的位置
            Vector3d side = new Vector3d(-direction.Y, direction.X, 0);
            var corners = new Point3d[ShapeLocal.GetLength(0)];
            for (int i = 0; i < corners.Length; i++)
                corners[i] = agent.Position + direction * ((ShapeLocal[i, 0] - centerX) * unit) + side * (ShapeLocal[i, 1] * unit);
            Color color = arena.TeamColors[agent.Team];
            // 填色：飛鏢中間凹進去，拆成兩個三角形（頭-左翼-凹點、頭-凹點-右翼）
            display.DrawPolygon(new Point3d[] { corners[0], corners[1], corners[2] }, color, true);
            display.DrawPolygon(new Point3d[] { corners[0], corners[2], corners[3] }, color, true);
            // 封閉折線外框：頂點順序和局部座標一樣，最後接回第一點
            display.DrawPolyline(new Polyline(new Point3d[] { corners[0], corners[1], corners[2], corners[3], corners[0] }), color, OutlineThickness * (int)scale);
        }

        foreach (Point3d star in arena.Stars)
        {
            // 五芒星：外圍 5 個點，隔一個連一次
            var pts = new Point3d[6];
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
