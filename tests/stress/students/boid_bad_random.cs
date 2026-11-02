using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 愛亂跑的隊伍：平常東晃西晃，看到食物就衝過去吃，遇到近距離的敵人就閃開
    public class Boid_bad_random : IBrain, IDeclareRelations
    {
        public string Name { get { return "bad_random"; } }
        public Color Color { get { return Color.Magenta; } }

        const double FleeDistance = 4.0;    // 敵軍靠到這個距離以內就躲
        const double WanderWeight = 0.8;    // 亂晃的力量
        const double FoodWeight = 1.6;      // 吃食物的力量
        const double FleeWeight = 1.5;      // 躲敵軍的力量
        const double StarWeight = 1.2;      // 搶五芒星的力量

        // 亂晃用的亂數產生器，每個隊伍實例共用一個，讓每步的轉向都不太一樣
        Random wanderRandom = new Random();
        double wanderAngle = 0.0;           // 目前亂晃的方向（弧度）

        // 開賽前呼叫一次：這隊用預設關係（同隊友軍、異隊敵軍），不另外設定
        public void Declare(RelationTable table)
        {
        }

        // 每隻鳥每一步都會呼叫一次，回傳「想往哪轉」的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;

            // 第一層：亂晃。方向每一步隨機偏轉一點，種子混了時間和 Guid，讓每隻鳥都不一樣
            int seed = Guid.NewGuid().GetHashCode() ^ (int)(DateTime.Now.Ticks & 0x7FFFFFFF);
            Random jitter = new Random(seed);
            double turn = (wanderRandom.NextDouble() - 0.5) * 1.2
                        + (jitter.NextDouble() - 0.5) * 0.8
                        + (self.Rng.NextDouble() - 0.5) * 0.4;
            wanderAngle += turn;
            Vector3d wander = new Vector3d(Math.Cos(wanderAngle), Math.Sin(wanderAngle), 0);
            force += Steering.Seek(wander, self.Velocity, world) * WanderWeight;

            // 第二層：找最近的敵人，太近就閃開
            bool hasEnemy = false;
            Neighbor nearestEnemy = new Neighbor();
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation != Relation.Enemy) continue;
                if (!hasEnemy || n.Distance < nearestEnemy.Distance) { nearestEnemy = n; hasEnemy = true; }
            }
            if (hasEnemy && nearestEnemy.Distance < FleeDistance && nearestEnemy.InvincibleSteps <= 0)
            {
                Vector3d away = self.Position - nearestEnemy.Position;
                away.Z = 0;
                if (away.Length > 1e-9)
                    force += Steering.Seek(away, self.Velocity, world) * FleeWeight;
            }

            // 看到五芒星就搶
            if (self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                Vector3d toStar = nearestStar - self.Position;
                toStar.Z = 0;
                if (toStar.Length > 1e-9)
                    force += Steering.Seek(toStar, self.Velocity, world) * StarWeight;
            }

            // 有食物就往最近的那個衝
            if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                Vector3d toFood = nearest - self.Position;
                toFood.Z = 0;
                if (toFood.Length > 1e-9)
                    force += Steering.Seek(toFood, self.Velocity, world) * FoodWeight;
            }

            force.Z = 0;
            return force;
        }
    }
}
