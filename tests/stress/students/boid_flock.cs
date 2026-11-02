using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 經典 Boids 成群：分離、對齊、聚合三力 + 群體吃食物 + 敵人靠近整群轉向避開
    public class Boid_flock : IBrain, IDeclareRelations
    {
        public string Name { get { return "flock"; } }
        public Color Color { get { return Color.DeepSkyBlue; } }

        const double SeparationDistance = 1.5;   // 友軍靠到這個距離以內就稍微拉開（引擎已處理，這裡只輕輕補強）
        const double FleeDistance = 6.0;         // 敵軍進到這個距離以內，整群轉向避開
        const double EdgeMargin = 4.0;           // 離場地邊緣這個距離以內就往內轉
        const double ObstacleMargin = 3.0;       // 離障礙物邊緣這個距離以內就繞開

        const double SeparationWeight = 0.8;     // 分離的力量
        const double AlignWeight = 0.6;          // 對齊的力量
        const double CohesionWeight = 0.5;       // 聚合的力量
        const double FleeWeight = 3.0;           // 躲敵軍的力量
        const double FoodWeight = 1.2;           // 吃食物的力量
        const double StarWeight = 1.5;           // 搶五芒星的力量
        const double EdgeWeight = 2.0;           // 離開場地邊緣的力量
        const double ObstacleWeight = 2.5;       // 繞開障礙物的力量

        // 使用預設身份：同隊友軍、異隊敵軍，不需要額外宣告
        public void Declare(RelationTable table)
        {
        }

        // 每隻鳥每一步呼叫一次，回傳「想往哪轉」的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d me = new Vector3d(self.Position.X, self.Position.Y, 0);

            // 第一層：掃一遍鄰居，統計友軍中心、平均速度、擁擠程度與敵軍威脅
            Vector3d allyCenter = Vector3d.Zero, allyVelocity = Vector3d.Zero;
            Vector3d separation = Vector3d.Zero, danger = Vector3d.Zero;
            int allyCount = 0;
            foreach (Neighbor n in neighbors)
            {
                Vector3d p = new Vector3d(n.Position.X, n.Position.Y, 0);
                if (n.Relation == Relation.Ally)
                {
                    allyCenter += p;
                    allyVelocity += new Vector3d(n.Velocity.X, n.Velocity.Y, 0);
                    allyCount++;
                    // 分離：越近推得越用力
                    if (n.Distance < SeparationDistance && n.Distance > 1e-6)
                        separation += (me - p) / (n.Distance * n.Distance);
                }
                else if (n.Relation == Relation.Enemy && n.Distance < FleeDistance && n.Distance > 1e-6)
                {
                    // 敵人越近越危險；無敵的敵人殺不死但能殺我，一樣要躲
                    danger += (me - p) / (n.Distance * n.Distance);
                }
            }

            // 第二層：三力
            if (allyCount > 0)
            {
                force += Toward(allyCenter / allyCount - me, self, world) * CohesionWeight;   // 聚合
                force += Toward(allyVelocity, self, world) * AlignWeight;                      // 對齊
                force += Toward(separation, self, world) * SeparationWeight;                   // 分離
            }

            // 敵人靠近：整群往同一個遠離方向轉（自己無敵時不必躲）
            if (self.InvincibleSteps <= 0 && danger.Length > 1e-9)
                force += Toward(danger, self, world) * FleeWeight;

            // 五芒星與食物：往最近的去，大家看到同一個目標就會一起吃
            if (self.Stars.Count > 0)
                force += Toward(Nearest(self.Stars, self.Position) - self.Position, self, world) * StarWeight;
            if (foods.Count > 0)
                force += Toward(Nearest(foods, self.Position) - self.Position, self, world) * FoodWeight;

            // 場地邊緣：快出界就往中心轉
            double dx = self.Position.X - world.Center.X, dy = self.Position.Y - world.Center.Y;
            double limit = world.HalfSize - EdgeMargin;
            Vector3d edge = Vector3d.Zero;
            if (dx > limit) edge.X = -(dx - limit);
            else if (dx < -limit) edge.X = -(dx + limit);
            if (dy > limit) edge.Y = -(dy - limit);
            else if (dy < -limit) edge.Y = -(dy + limit);
            force += Toward(edge, self, world) * EdgeWeight;

            // 障礙物：靠近邊緣就往外推（不處理已消失的障礙物，引擎會把它從清單拿掉）
            foreach (var o in world.Obstacles)
            {
                if (o.DistanceTo(self.Position) >= ObstacleMargin) continue;
                var c = o.ClosestPoint(self.Position);
                Vector3d away = new Vector3d(self.Position.X - c.X, self.Position.Y - c.Y, 0);
                if (away.Length < 1e-6) away = me - new Vector3d(o.Center.X, o.Center.Y, 0);   // 跑進障礙物內：朝中心外推
                force += Toward(away, self, world) * ObstacleWeight;
            }

            // 防呆：保證回傳的是有限數字、Z 為 0
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) || double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return new Vector3d(force.X, force.Y, 0);
        }

        // 把方向變成轉向力；零向量直接回傳零，避免除以零
        static Vector3d Toward(Vector3d direction, SelfInfo self, World world)
        {
            Vector3d flat = new Vector3d(direction.X, direction.Y, 0);
            if (flat.Length < 1e-9) return Vector3d.Zero;
            return Steering.Seek(Steering.Unit(flat), self.Velocity, world);
        }

        // 找出清單中離某點最近的點（清單不可為空）
        static Point3d Nearest(List<Point3d> points, Point3d from)
        {
            Point3d best = points[0];
            foreach (Point3d p in points)
                if (from.DistanceTo(p) < from.DistanceTo(best)) best = p;
            return best;
        }
    }
}
