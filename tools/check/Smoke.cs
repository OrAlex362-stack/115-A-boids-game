using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BoidArena;

// 試跑學生的 class：和兩隊範例對戰 300 步，學生隊被淘汰或建立失敗就回傳 1
class Smoke
{
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        List<string> rejected;
        var students = Roster.Find(typeof(Smoke).Assembly, out rejected);
        foreach (string r in rejected) Console.WriteLine("REJECT " + r);
        if (students.Count == 0)
        {
            Console.WriteLine("REJECT 沒有找到 Boid_ 開頭的 class");
            return 1;
        }
        var brains = new List<IBrain>(students);
        brains.Add(new ParamBrain("smoke_a", Color.Gray, 1, 1, 1, 0.5, 0.5));
        brains.Add(new ParamBrain("smoke_b", Color.DarkGray, 0.5, 0.5, 1, 1, 0));
        var arena = new Arena(brains, Arena.GlobalSeed);
        for (int s = 0; s < 300; s++) arena.RunStep();

        int failures = rejected.Count;
        for (int t = 0; t < students.Count; t++)
        {
            Console.WriteLine((arena.Disqualified[t] ? "FAIL " : "OK ") + students[t].GetType().Name + " "
                + arena.Status[t]);
            if (arena.Disqualified[t]) failures++;
        }
        return failures == 0 ? 0 : 1;
    }
}
