using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 數學派隊伍：一切都用距離倒數與單位向量來算力
    // 近的東西影響大、遠的東西影響小，方向都先單位化再乘權重
    public class Boid_bad_nan : IBrain, IDeclareRelations
    {
        public string Name { get { return "bad_nan"; } }
        public Color Color { get { return Color.Magenta; } }

        const double RepelWeight = 3.0;     // 敵軍排斥力（距離倒數）
        const double CohesionWeight = 0.7;  // 往友軍中心靠
        const double AlignWeight = 0.5;     // 跟友軍同向
        const double FoodWeight = 1.2;      // 吃食物
        const double StarWeight = 1.5;      // 搶五芒星
        const double WallWeight = 2.0;      // 障礙物排斥力
        const double WallRange = 6.0;       // 離障礙物多近才開始排斥
        const double EdgeMargin = 5.0;      // 離場地邊界多近開始往中心拉

        public void Declare(RelationTable table)
        {
            // 用預設：同隊友軍、異隊敵軍
        }

        // 自己的單位化函式：向量除以長度
        static Vector3d Unitize2D(Vector3d v)
        {
            Vector3d flat = new Vector3d(v.X, v.Y, 0);
            return flat / flat.Length;
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d me = new Vector3d(self.Position.X, self.Position.Y, 0);

            // 第一層：統計鄰居
            Vector3d allySum = Vector3d.Zero, allyVel = Vector3d.Zero;
            int allyCount = 0;
            Vector3d repel = Vector3d.Zero;
            foreach (Neighbor n in neighbors)
            {
                Vector3d p = new Vector3d(n.Position.X, n.Position.Y, 0);
                if (n.Relation == Relation.Ally)
                {
                    allySum += p;
                    allyVel += n.Velocity;
                    allyCount++;
                }
                else if (n.Relation == Relation.Enemy)
                {
                    // 越近推得越用力：力道與距離成反比
                    Vector3d away = me - p;
                    repel += Unitize2D(away) * (1.0 / n.Distance);
                }
            }

            // 第二層：搭配各種行為
            if (allyCount > 0)
            {
                Vector3d toCenter = allySum / allyCount - me;
                force += Steering.Seek(Unitize2D(toCenter), self.Velocity, world) * CohesionWeight;
                // 友軍平均速度方向，當作大家共同的前進方向
                force += Steering.Seek(Unitize2D(allyVel), self.Velocity, world) * AlignWeight;
            }
            force += repel * RepelWeight;

            // 障礙物：離邊緣越近推得越用力
            foreach (Obstacle o in world.Obstacles)
            {
                double d = o.DistanceTo(self.Position);
                if (d < WallRange)
                {
                    Point3d edge = o.ClosestPoint(self.Position);
                    Vector3d outward = self.Position - edge;
                    force += Unitize2D(outward) * (WallWeight / d);
                }
            }

            // 靠近場地邊界就往中心拉
            double fromCenter = Math.Max(Math.Abs(self.Position.X - world.Center.X), Math.Abs(self.Position.Y - world.Center.Y));
            if (fromCenter > world.HalfSize - EdgeMargin)
                force += Steering.Seek(Unitize2D(world.Center - self.Position), self.Velocity, world);

            // 五芒星：取最近的，力道與距離成反比（越近越想吃）
            if (self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                Vector3d toStar = nearestStar - self.Position;
                force += Unitize2D(toStar) * (StarWeight * 10.0 / toStar.Length);
            }

            // 食物：每一個食物都貢獻一份吸引力，越近越強
            foreach (Point3d f in foods)
            {
                Vector3d toFood = f - self.Position;
                force += Unitize2D(toFood) * (FoodWeight / toFood.Length);
            }

            return force;
        }
    }
}
