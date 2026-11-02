using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 衝刺隊：只管吃食物、搶五芒星，跑得快；遇到敵人只閃躲，不主動攻擊
    public class Boid_sprint : IBrain, IDeclareRelations
    {
        public string Name { get { return "sprint"; } }
        public Color Color { get { return Color.Magenta; } }   // 洋紅色，場上很好辨認

        const double DodgeDistance = 5.0;      // 敵軍靠到這個距離以內就閃
        const double DodgeWeight = 3.0;        // 閃敵軍的力量
        const double ChaserBonus = 2.0;        // 敵軍正朝我衝來時，閃躲力量再乘上這個倍數
        const double FoodWeight = 1.5;         // 吃食物的力量
        const double StarWeight = 2.5;         // 搶五芒星的力量
        const double ObstacleMargin = 3.0;     // 離障礙物邊緣這麼近就開始繞開
        const double ObstacleWeight = 2.5;     // 繞開障礙物的力量
        const double WallMargin = 4.0;         // 離場地邊界這麼近就往中心拉
        const double WallWeight = 2.0;         // 避開邊界的力量
        const double WanderWeight = 0.3;       // 沒目標時隨機亂晃的力量

        // 開賽前呼叫一次：用預設身份（同隊友軍、異隊敵軍），不需要改
        public void Declare(RelationTable table)
        {
        }

        // 把任意向量壓成 Z=0 的 2D 向量
        static Vector3d Flat(Vector3d v)
        {
            return new Vector3d(v.X, v.Y, 0);
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Point3d me = new Point3d(self.Position.X, self.Position.Y, 0);
            bool invincible = self.InvincibleSteps > 0;

            // 第一層：閃躲敵軍（無敵時不用躲；友軍、不相關的都不理）
            if (!invincible && neighbors != null)
            {
                foreach (Neighbor n in neighbors)
                {
                    if (n.Relation != Relation.Enemy) continue;
                    if (n.Distance >= DodgeDistance) continue;

                    Vector3d away = Flat(self.Position - n.Position);
                    double closeness = 1.0 - n.Distance / DodgeDistance;   // 越近越大，0 到 1
                    double weight = DodgeWeight * closeness;

                    // 敵軍的速度朝向我，代表它正在追我，閃得更用力
                    Vector3d toMe = Flat(me - n.Position);
                    if (Flat(n.Velocity) * Steering.Unit(toMe) > 0.0) weight *= ChaserBonus;

                    // 往側邊閃比直線後退更能甩開追兵：把逃離方向加上一點垂直分量
                    Vector3d side = new Vector3d(-away.Y, away.X, 0);
                    if (Flat(self.Velocity) * side < 0.0) side = -side;
                    Vector3d dodge = Steering.Unit(away) + Steering.Unit(side) * 0.6;
                    force += Steering.Seek(dodge, self.Velocity, world) * weight;
                }
            }

            // 第二層：搶五芒星（最優先的獎勵）
            bool hasTarget = false;
            if (self.Stars != null && self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (me.DistanceTo(star) < me.DistanceTo(nearestStar)) nearestStar = star;
                force += Steering.Seek(Flat(nearestStar - me), self.Velocity, world) * StarWeight;
                hasTarget = true;
            }

            // 第三層：衝向最近的食物
            if (foods != null && foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (me.DistanceTo(f) < me.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(Flat(nearest - me), self.Velocity, world) * FoodWeight;
                hasTarget = true;
            }

            // 第四層：繞開障礙物（太靠近邊緣就從最近點往外推）
            if (world.Obstacles != null)
            {
                foreach (Obstacle o in world.Obstacles)
                {
                    double d = o.DistanceTo(me);
                    if (d >= ObstacleMargin) continue;
                    Vector3d push = Flat(me - o.ClosestPoint(me));
                    if (push.Length < 1e-6) push = Flat(me - o.Center);   // 剛好在邊上或裡面，從中心往外推
                    force += Steering.Seek(push, self.Velocity, world) * (ObstacleWeight * (1.0 - d / ObstacleMargin));
                }
            }

            // 第五層：離邊界太近就往場地中心拉（場地會縮小，要隨時看 HalfSize）
            double dx = me.X - world.Center.X, dy = me.Y - world.Center.Y;
            double edge = world.HalfSize - Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (edge < WallMargin)
            {
                double pull = WallWeight * (1.0 - Math.Max(edge, 0.0) / WallMargin);
                force += Steering.Seek(new Vector3d(-dx, -dy, 0), self.Velocity, world) * pull;
            }

            // 沒有目標時：保持衝刺，順著現在的方向再加一點隨機偏轉，到處巡邏找食物
            if (!hasTarget)
            {
                Vector3d heading = Flat(self.Velocity);
                if (heading.Length < 1e-6) heading = new Vector3d(self.Rng.NextDouble() - 0.5, self.Rng.NextDouble() - 0.5, 0);
                Vector3d jitter = new Vector3d(self.Rng.NextDouble() - 0.5, self.Rng.NextDouble() - 0.5, 0);
                force += Steering.Seek(Steering.Unit(heading) + jitter, self.Velocity, world) * WanderWeight;
            }

            // 保險：任何一項算出 NaN 就當作沒事，避免整隊被淘汰
            force = Flat(force);
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) || double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return force;
        }
    }
}
