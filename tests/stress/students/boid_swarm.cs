using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 蜂群：用很強的聚合力讓全隊黏成一團，整團慢慢朝最近的敵人推進，用人數壓制。
    // 食物只讓「視野內離食物最近的友軍」去吃，其他隻不離團。
    public class Boid_swarm : IBrain, IDeclareRelations
    {
        public string Name { get { return "swarm"; } }
        public Color Color { get { return Color.Magenta; } }

        const double CohesionWeight = 2.6;    // 聚合：往友軍中心靠（最強）
        const double AlignWeight = 0.6;       // 與友軍同向，團才不會散
        const double AdvanceWeight = 0.45;    // 整團推進敵人的力量（刻意偏小，慢慢推）
        const double FoodWeight = 1.2;        // 吃食物（只有群體中最靠近的個體會用）
        const double StarWeight = 1.0;        // 搶五芒星（同樣只給最靠近的個體）
        const double WallWeight = 1.2;        // 離牆太近時往場中央回
        const double ObstacleWeight = 1.5;    // 離障礙物太近時遠離
        const double WallMargin = 6.0;        // 離牆多近開始回頭
        const double ObstacleMargin = 4.0;    // 離障礙物多近開始閃
        const double FleeDistance = 3.0;      // 落單時敵軍靠到這個距離就往團裡逃
        const double Tie = 0.05;              // 距離比較的容差，避免同時出手造成抖動

        // 開賽前不做特別設定：同隊友軍、異隊全是敵軍
        public void Declare(RelationTable table)
        {
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d myPos = new Vector3d(self.Position.X, self.Position.Y, 0);

            // 第一層：掃鄰居，統計友軍中心與速度、最近的敵人
            Vector3d allyCenter = Vector3d.Zero, allyVelocity = Vector3d.Zero;
            int allyCount = 0;
            bool hasEnemy = false;
            Neighbor nearestEnemy = new Neighbor();
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation == Relation.Ally)
                {
                    allyCenter += new Vector3d(n.Position.X, n.Position.Y, 0);
                    allyVelocity += new Vector3d(n.Velocity.X, n.Velocity.Y, 0);
                    allyCount++;
                }
                else if (n.Relation == Relation.Enemy)
                {
                    if (!hasEnemy || n.Distance < nearestEnemy.Distance) { nearestEnemy = n; hasEnemy = true; }
                }
            }

            // 第二層：聚合與同向
            if (allyCount > 0)
            {
                Vector3d toCenter = allyCenter / allyCount - myPos;
                force += Steering.Seek(toCenter, self.Velocity, world) * CohesionWeight;
                force += Steering.Seek(allyVelocity / allyCount, self.Velocity, world) * AlignWeight;
            }

            // 推進：朝最近的敵人慢慢走，友軍越多越敢推；看不到敵人就往場中央，讓團聚在中間
            if (hasEnemy)
            {
                double boldness = Math.Min(1.0, (allyCount + 1) / 8.0);
                if (!(nearestEnemy.InvincibleSteps > 0))
                    force += Steering.Seek(nearestEnemy.Position - self.Position, self.Velocity, world) * (AdvanceWeight * boldness);
                // 落單（身邊沒幾隻友軍）又被敵人貼臉：往團的方向逃，不去硬碰
                if (allyCount < 3 && nearestEnemy.Distance < FleeDistance && allyCount > 0)
                    force += Steering.Seek(allyCenter / allyCount - myPos, self.Velocity, world) * 1.5;
            }
            else
            {
                force += Steering.Seek(world.Center - self.Position, self.Velocity, world) * (AdvanceWeight * 0.5);
            }

            // 食物：只有視野內離它最近的友軍才去搶（其他友軍離它更遠就不動）
            if (foods != null && foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                if (IsClosestInGroup(self, neighbors, nearest))
                    force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
            }

            // 五芒星：同樣由群體中最近的個體去吃
            if (self.Stars != null && self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d s in self.Stars)
                    if (self.Position.DistanceTo(s) < self.Position.DistanceTo(nearestStar)) nearestStar = s;
                if (IsClosestInGroup(self, neighbors, nearestStar))
                    force += Steering.Seek(nearestStar - self.Position, self.Velocity, world) * StarWeight;
            }

            // 離牆太近：往場中央回
            double toWall = world.HalfSize - Math.Max(Math.Abs(self.Position.X - world.Center.X), Math.Abs(self.Position.Y - world.Center.Y));
            if (toWall < WallMargin)
                force += Steering.Seek(world.Center - self.Position, self.Velocity, world) * WallWeight;

            // 離障礙物太近：從最近的邊緣往外閃
            if (world.Obstacles != null)
            {
                foreach (Obstacle o in world.Obstacles)
                {
                    if (o.DistanceTo(self.Position) < ObstacleMargin)
                        force += Steering.Seek(self.Position - o.ClosestPoint(self.Position), self.Velocity, world) * ObstacleWeight;
                }
            }

            // 保險：避免 NaN 或無限大讓整隊被淘汰；Z 一律當 0
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) || double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return new Vector3d(force.X, force.Y, 0);
        }

        // 目標點在我視野內的友軍中，我是不是離它最近的（較近者去吃，平手時不都去搶）
        static bool IsClosestInGroup(SelfInfo self, List<Neighbor> neighbors, Point3d target)
        {
            double mine = self.Position.DistanceTo(target);
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation != Relation.Ally) continue;
                if (n.Position.DistanceTo(target) < mine - Tie) return false;
            }
            return true;
        }
    }
}
