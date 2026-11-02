using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 身份：自己、友軍、敵軍、不相關。只是給你的行為模式用的標籤，不改變擊殺規則：
    // 只要是異隊，碰到時照樣依 60 度錐判定殺或被殺，不管你把它標成什麼
    public enum Relation { Self, Ally, Enemy, Neutral }

    // 學生看得到的鄰居資料（引擎複製出來的唯讀快照，改它不會影響別人）
    public struct Neighbor
    {
        public Point3d Position;
        public Vector3d Velocity;
        public int Team;
        public string Name;        // 它所屬隊伍的名字（Name 屬性的值）
        public Relation Relation;  // 對你來說它是友軍、敵軍還是不相關（預設：同隊＝友軍，異隊＝敵軍；可以在 Declare 改）
        public bool IsFriend;      // 同隊（不受 Declare 影響）
        public int InvincibleSteps;   // 它還剩幾步無敵（吃到五芒星；0 = 沒有）。無敵的對象殺不死，不用白費力氣追
        public double Distance;
    }

    public struct SelfInfo
    {
        public Point3d Position;
        public Vector3d Velocity;
        public int Team;
        public string Name;        // 自己這隊的名字
        public int InvincibleSteps;   // 我還剩幾步無敵（0 = 沒有）
        public List<Point3d> Stars;   // 視野內的五芒星（吃到就無敵 100 步），沒有就是空清單

        // 引擎給每隊的固定亂數（種子由全域種子與隊名決定），要亂數就用它，不要自己 new Random
        public Random Rng;
    }

    // 障礙物：與座標軸平行的矩形（2D），建立後不能改。Boid 碰到會被彈開；中心跑到場地外就會被引擎移除
    public sealed class Obstacle
    {
        public Point3d Center { get; private set; }
        public double HalfWidth { get; private set; }    // X 方向的半寬
        public double HalfHeight { get; private set; }   // Y 方向的半高

        public Obstacle(Point3d center, double halfWidth, double halfHeight)
        {
            Center = center;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        // 矩形上離 p 最近的點（p 在矩形裡面就是 p 自己）
        public Point3d ClosestPoint(Point3d p)
        {
            double x = Math.Max(Center.X - HalfWidth, Math.Min(Center.X + HalfWidth, p.X));
            double y = Math.Max(Center.Y - HalfHeight, Math.Min(Center.Y + HalfHeight, p.Y));
            return new Point3d(x, y, 0);
        }

        // p 到矩形邊緣的距離（在矩形裡面是 0）
        public double DistanceTo(Point3d p)
        {
            return p.DistanceTo(ClosestPoint(p));
        }
    }

    // 開賽前問你一次：你要把誰當友軍、敵軍、不相關（只要在 class 上多寫 `: IBrain, IDeclareRelations` 並實作 Declare）
    // 沒標的隊伍：同隊是友軍、異隊是敵軍；自己這隊一律是友軍
    public sealed class RelationTable
    {
        public string MyName { get; private set; }
        public IList<string> AllNames { get; private set; }   // 這一場所有參賽隊伍的名字（含自己）
        readonly Dictionary<string, Relation> map = new Dictionary<string, Relation>();

        public RelationTable(string myName, IList<string> allNames)
        {
            MyName = myName;
            AllNames = new System.Collections.ObjectModel.ReadOnlyCollection<string>(new List<string>(allNames));
            foreach (string name in allNames)
                map[name] = name == myName ? Relation.Ally : Relation.Enemy;
        }

        public void Ally(string teamName) { Set(teamName, Relation.Ally); }
        public void Enemy(string teamName) { Set(teamName, Relation.Enemy); }
        public void Neutral(string teamName) { Set(teamName, Relation.Neutral); }

        // 查某隊現在的身份（自己這隊回傳 Self，不認識的名字回傳 Neutral）
        public Relation Get(string teamName)
        {
            if (teamName == MyName) return Relation.Self;
            Relation relation;
            return map.TryGetValue(teamName, out relation) ? relation : Relation.Neutral;
        }

        void Set(string teamName, Relation relation)
        {
            if (teamName == null || teamName == MyName || !map.ContainsKey(teamName)) return;   // 自己這隊改不了；不存在的名字忽略
            map[teamName] = relation;
        }
    }

    // 選用：實作它，引擎開賽前會呼叫一次 Declare，讓你設定「友軍／敵軍／不相關」有誰
    public interface IDeclareRelations
    {
        void Declare(RelationTable table);
    }

    // 場地與物理上限：全隊相同，由引擎決定。每隊每步拿到的是一份新的唯讀複本，改不了
    public sealed class World
    {
        public Point3d Center { get; private set; }
        public double HalfSize { get; private set; }
        public int Step { get; private set; }
        public double ViewRadius { get; private set; }
        public double MaxSpeed { get; private set; }
        public double MaxForce { get; private set; }
        // 目前場上的障礙物（唯讀清單）
        public IList<Obstacle> Obstacles { get; private set; }
        // 1 穩定期（場地不縮、有障礙物）、2 縮小期、3 決戰期（場地不再縮、食物不再出現）
        public int Phase { get; private set; }

        public World(Point3d center, double halfSize, int step, double viewRadius, double maxSpeed, double maxForce,
            IList<Obstacle> obstacles = null, int phase = 1)
        {
            Obstacles = obstacles ?? new System.Collections.ObjectModel.ReadOnlyCollection<Obstacle>(new Obstacle[0]);
            Phase = phase;
            Center = center;
            HalfSize = halfSize;
            Step = step;
            ViewRadius = viewRadius;
            MaxSpeed = maxSpeed;
            MaxForce = maxForce;
        }
    }

    // 學生要實作的介面：只回答「叫什麼名字」與「想往哪轉」；顏色由老師的主程式分配，學生不能選
    public interface IBrain
    {
        string Name { get; }
        Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world);
    }

    public static class Steering
    {
        // 單位向量；太短就回零向量（不用 Vector3d.Unitize，它要呼叫原生程式庫，較慢也不能脫離 Rhino 測試）
        public static Vector3d Unit(Vector3d v)
        {
            double length = v.Length;
            if (length < 1e-12) return Vector3d.Zero;
            return v / length;
        }

        // 轉向力 ＝ 期望方向 × 最大速度 − 目前速度，長度限制在 MaxForce
        public static Vector3d Seek(Vector3d desired, Vector3d velocity, World world)
        {
            desired = Unit(desired);
            if (desired.Length == 0) return Vector3d.Zero;
            Vector3d steering = desired * world.MaxSpeed - velocity;
            if (steering.Length > world.MaxForce)
                steering *= world.MaxForce / steering.Length;
            return steering;
        }
    }
}
