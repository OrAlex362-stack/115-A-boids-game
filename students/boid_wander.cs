using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 隨機漫遊隊：用 self.Rng 做平滑的隨機漫步（Reynolds wander），
    // 不刻意追或逃，只在撞牆、貼障礙物、碰到食物或五芒星時修正方向。
    // 注意：同一個類別實例可能被全隊共用，所以不在欄位存每隻鳥的狀態，
    // 「平滑」靠目前速度方向當作記憶，每步只加一點點隨機轉角。
    public class Boid_wander : IBrain
    {
        public string Name { get { return "wander"; } }
        public Color Color { get { return Color.DeepPink; } }

        const double WanderJitter = 0.5;     // 每步隨機轉角的最大幅度（弧度），越小越平滑
        const double WanderWeight = 1.0;     // 漫遊力量
        const double WallMarginRatio = 0.15; // 離牆多近（佔場地半邊長的比例）開始往內轉
        const double WallWeight = 3.0;       // 避牆力量
        const double ObstacleRatio = 0.3;    // 離障礙物多近（佔視野半徑的比例）開始閃
        const double ObstacleWeight = 3.0;   // 避障礙物力量
        const double FoodRatio = 0.5;        // 食物在視野半徑這個比例以內才順手去吃
        const double FoodWeight = 1.5;       // 吃食物的力量
        const double StarWeight = 1.5;       // 碰到五芒星順手吃的力量

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            // 目前朝向：速度太小（剛出生）就用亂數給一個方向
            Vector3d heading = new Vector3d(self.Velocity.X, self.Velocity.Y, 0);
            if (heading.Length < 1e-6)
            {
                double a0 = self.Rng.NextDouble() * 2.0 * Math.PI;
                heading = new Vector3d(Math.Cos(a0), Math.Sin(a0), 0);
            }
            heading = Steering.Unit(heading);

            // 漫遊：把朝向隨機轉一個小角度（-WanderJitter ~ +WanderJitter）
            double turn = (self.Rng.NextDouble() * 2.0 - 1.0) * WanderJitter;
            double c = Math.Cos(turn), s = Math.Sin(turn);
            Vector3d wanderDir = new Vector3d(heading.X * c - heading.Y * s, heading.X * s + heading.Y * c, 0);
            Vector3d force = Steering.Seek(wanderDir, self.Velocity, world) * WanderWeight;

            // 撞牆修正：靠近場地邊緣就往中心的方向轉（越靠近越用力）
            double margin = world.HalfSize * WallMarginRatio;
            double dx = self.Position.X - world.Center.X;
            double dy = self.Position.Y - world.Center.Y;
            double over = Math.Max(Math.Abs(dx), Math.Abs(dy)) - (world.HalfSize - margin);
            if (over > 0 && margin > 1e-9)
            {
                double k = Math.Min(1.0, over / margin);
                Vector3d toCenter = new Vector3d(-dx, -dy, 0);
                force += Steering.Seek(toCenter, self.Velocity, world) * (WallWeight * (0.5 + k));
            }

            // 貼障礙物修正：離邊緣太近就往遠離最近邊的方向轉
            double obstacleRange = world.ViewRadius * ObstacleRatio;
            foreach (Obstacle ob in world.Obstacles)
            {
                double d = ob.DistanceTo(self.Position);
                if (d < obstacleRange)
                {
                    Point3d edge = ob.ClosestPoint(self.Position);
                    Vector3d away = new Vector3d(self.Position.X - edge.X, self.Position.Y - edge.Y, 0);
                    if (away.Length < 1e-6) away = new Vector3d(self.Position.X - ob.Center.X, self.Position.Y - ob.Center.Y, 0);
                    double k = 1.0 - d / obstacleRange;
                    force += Steering.Seek(away, self.Velocity, world) * (ObstacleWeight * (0.5 + k));
                }
            }

            // 碰到食物才修正：只吃離得很近的最近一個，遠的食物不理
            double foodRange = world.ViewRadius * FoodRatio;
            bool hasFood = false;
            Point3d nearestFood = Point3d.Origin;
            double bestDist = foodRange;
            foreach (Point3d f in foods)
            {
                double d = self.Position.DistanceTo(f);
                if (d < bestDist) { bestDist = d; nearestFood = f; hasFood = true; }
            }
            if (hasFood)
                force += Steering.Seek(nearestFood - self.Position, self.Velocity, world) * FoodWeight;

            // 五芒星同理：很近才順手吃
            foreach (Point3d star in self.Stars)
            {
                if (self.Position.DistanceTo(star) < foodRange)
                {
                    force += Steering.Seek(star - self.Position, self.Velocity, world) * StarWeight;
                    break;
                }
            }

            force = new Vector3d(force.X, force.Y, 0);
            if (double.IsNaN(force.X) || double.IsNaN(force.Y)) return Vector3d.Zero;
            return force;
        }
    }
}
