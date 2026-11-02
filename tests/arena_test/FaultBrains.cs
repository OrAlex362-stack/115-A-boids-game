using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena.Tests
{
    // 故意寫錯的學生 class，用來測試防錯：每一種都應該只讓自己那隊消失

    public class Boid_throwLater : IBrain
    {
        public string Name { get { return "throwLater"; } }
        public Color Color { get { return Color.Black; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            if (world.Step >= 5) throw new IndexOutOfRangeException("故意的");
            return Vector3d.Zero;
        }
    }

    public class Boid_nan : IBrain
    {
        public string Name { get { return "nan"; } }
        public Color Color { get { return Color.Black; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            return new Vector3d(0, 0, 0) / 0.0;   // NaN
        }
    }

    public class Boid_slow : IBrain
    {
        public string Name { get { return "slow"; } }
        public Color Color { get { return Color.Black; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalMilliseconds < 1.0) { }   // 每隻 1 毫秒，50 隻 = 每步 50 毫秒
            return Vector3d.Zero;
        }
    }

    public class Boid_badName : IBrain
    {
        public string Name { get { throw new NullReferenceException("故意的"); } }
        public Color Color { get { return Color.Black; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            return Vector3d.Zero;
        }
    }

    public class Boid_badCtor : IBrain
    {
        public Boid_badCtor() { throw new InvalidOperationException("建構子故意出錯"); }
        public string Name { get { return "badCtor"; } }
        public Color Color { get { return Color.Black; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            return Vector3d.Zero;
        }
    }

    // 正常隊伍，但會用 self.Rng：測試壞隊觸發重算時，好隊抽到的亂數不會被打亂（不以 Boid_ 開頭，不進名冊）
    public class JitterBrain : IBrain
    {
        public string Name { get { return "jitter"; } }
        public Color Color { get { return Color.White; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            var random = new Vector3d(self.Rng.NextDouble() - 0.5, self.Rng.NextDouble() - 0.5, self.Rng.NextDouble() - 0.5);
            return Steering.Seek(random, self.Velocity, world);
        }
    }

    // 一直往 Z 方向推：2D 遊戲裡引擎要把 Z 丟掉，所有東西還是在 Z = 0（不以 Boid_ 開頭，不進名冊）
    public class ZPushBrain : IBrain
    {
        public string Name { get { return "zpush"; } }
        public Color Color { get { return Color.White; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            return new Vector3d(0.01, 0.01, 5);
        }
    }

    public class Boid_dupName : IBrain
    {
        public string Name { get { return "nan"; } }   // 和 Boid_nan 同名
        public Color Color { get { return Color.Black; } }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            return Vector3d.Zero;
        }
    }
    // 身份測試用：Declare 設定誰是友軍／不相關，Decide 把看到的身份記下來（不以 Boid_ 開頭，不進名冊）
    public class RelationBrain : IBrain, IDeclareRelations
    {
        readonly string name;
        readonly string allyName, neutralName;
        public readonly Dictionary<string, HashSet<Relation>> Seen = new Dictionary<string, HashSet<Relation>>();
        public List<string> AllNamesAtDeclare;
        public string SelfNameSeen;
        public bool NeighborSelfSeen;
        public RelationBrain(string name, string allyName, string neutralName) { this.name = name; this.allyName = allyName; this.neutralName = neutralName; }
        public string Name { get { return name; } }
        public Color Color { get { return Color.White; } }
        public void Declare(RelationTable table)
        {
            AllNamesAtDeclare = new List<string>(table.AllNames);
            if (allyName != null) table.Ally(allyName);
            if (neutralName != null) table.Neutral(neutralName);
            table.Neutral(table.MyName);   // 自己這隊改不了
        }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            SelfNameSeen = self.Name;
            foreach (var n in neighbors)
            {
                if (n.Relation == Relation.Self) NeighborSelfSeen = true;
                HashSet<Relation> set;
                if (!Seen.TryGetValue(n.Name, out set)) Seen[n.Name] = set = new HashSet<Relation>();
                set.Add(n.Relation);
            }
            return Vector3d.Zero;
        }
    }

    // Declare 丟例外：該隊要被淘汰（不以 Boid_ 開頭，不進名冊）
    public class BadDeclareBrain : IBrain, IDeclareRelations
    {
        public string Name { get { return "badDeclare"; } }
        public Color Color { get { return Color.White; } }
        public void Declare(RelationTable table) { throw new InvalidOperationException("Declare 故意出錯"); }
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world) { return Vector3d.Zero; }
    }
}
