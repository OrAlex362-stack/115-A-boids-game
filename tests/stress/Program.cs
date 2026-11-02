using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BoidArena;

// 極限測試：15 位「學生」的檔（部分故意出錯）全部丟進同一場比賽
//   dotnet run -c Release -- all      所有能編譯的檔（含會出錯的）：看出錯的隊會不會只讓自己消失
//   dotnet run -c Release -- honest   只有誠實的 10 隊：看排名與每隊每步耗時
class Program
{
    static List<IBrain> Brains(bool honestOnly)
    {
        var list = new List<IBrain>();
        foreach (Type t in typeof(Program).Assembly.GetTypes().Where(t => t.Name.StartsWith("Boid_") && typeof(IBrain).IsAssignableFrom(t)).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (honestOnly && t.Name.StartsWith("Boid_bad")) continue;
            try { list.Add((IBrain)Activator.CreateInstance(t)); }
            catch (Exception ex) { Console.WriteLine("建立失敗 " + t.Name + "：" + ex.GetType().Name); }
        }
        return list;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        bool honest = args.Length > 0 && args[0] == "honest";
        var brains = Brains(honest);
        // 與 GH 相同：最多 10 隊（名稱排序後取前 10）
        if (!honest) brains = brains.Take(10).ToList();
        Console.WriteLine("參賽 " + brains.Count + " 隊：" + string.Join(", ", brains.Select(b => b.GetType().Name)));
        var arena = new Arena(brains, Arena.GlobalSeed);
        var timer = Stopwatch.StartNew();
        double worstStepMs = 0;
        var disqAt = new Dictionary<int, int>();
        while (!arena.Finished)
        {
            var one = Stopwatch.StartNew();
            arena.RunStep();
            worstStepMs = Math.Max(worstStepMs, one.Elapsed.TotalMilliseconds);
            for (int t = 0; t < brains.Count; t++)
                if (arena.Disqualified[t] && !disqAt.ContainsKey(t)) disqAt[t] = arena.Step;
            if (arena.Step % 900 == 0)
                Console.WriteLine("step " + arena.Step + " " + arena.PhaseName + " 總數 " + arena.AliveCount);
        }
        int[] alive = arena.AliveByTeam();
        Console.WriteLine("--- 結束於第 " + arena.Step + " 步" + (arena.EndedEarly ? "（提早結束）" : "") + "，總耗時 " + timer.Elapsed.TotalSeconds.ToString("F1") + " 秒，最慢一步 " + worstStepMs.ToString("F1") + " 毫秒");
        var order = Enumerable.Range(0, brains.Count).OrderByDescending(t => alive[t]).ThenByDescending(t => arena.TeamKills[t]).ToList();
        int rank = 1;
        foreach (int t in order)
            Console.WriteLine((rank++) + ". " + arena.TeamNames[t].PadRight(10) + " 存活 " + alive[t].ToString().PadLeft(3) + "  擊殺 " + arena.TeamKills[t].ToString().PadLeft(4)
                + "  " + arena.Status[t] + (disqAt.ContainsKey(t) ? "（第 " + disqAt[t] + " 步）" : ""));
        return 0;
    }
}
