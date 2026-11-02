using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 追擊隊：盯著最近的敵人猛追，順便吃食物、搶五芒星
    public class Boid_bad_throw : IBrain, IDeclareRelations
    {
        public string Name { get { return "bad_throw"; } }
        public Color Color { get { return Color.DeepPink; } }

        const double ChaseWeight = 2.0;     // 追敵人的力量
        const double FoodWeight = 1.0;      // 吃食物的力量
        const double StarWeight = 1.5;      // 搶五芒星的力量
        const double AllyWeight = 0.3;      // 靠近友軍的力量
        const double LeadTime = 3.0;        // 預判敵人未來幾步的位置
        const double CloseRange = 2.0;      // 貼身距離

        // 開賽前呼叫：沿用預設身份（同隊友軍、異隊敵軍）
        public void Declare(RelationTable table)
        {
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;

            // 第一層：找最近的敵人，順便統計友軍中心
            Vector3d allyPosition = Vector3d.Zero;
            int allyCount = 0;
            bool hasEnemy = false;
            Neighbor nearestEnemy = new Neighbor();
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation == Relation.Ally)
                {
                    allyPosition += new Vector3d(n.Position.X, n.Position.Y, 0);
                    allyCount++;
                }
                else if (n.Relation == Relation.Enemy && n.InvincibleSteps <= 0)
                {
                    if (!hasEnemy || n.Distance < nearestEnemy.Distance) { nearestEnemy = n; hasEnemy = true; }
                }
            }

            // 第二層：追最近的敵人，朝它的預測位置衝
            if (hasEnemy)
            {
                Point3d predicted = nearestEnemy.Position + nearestEnemy.Velocity * LeadTime;
                Vector3d toEnemy = predicted - self.Position;

                // 貼身時依距離微調衝刺力道（分級：貼身 0，其餘 1）
                int rushLevel = nearestEnemy.Distance < CloseRange ? 0 : 1;
                int rushBoost = 10 / rushLevel;
                force += Steering.Seek(toEnemy, self.Velocity, world) * (ChaseWeight + rushBoost * 0.01);
            }

            // 沒敵人時才稍微靠攏友軍
            if (!hasEnemy && allyCount > 0)
            {
                Vector3d toAllies = allyPosition / allyCount - new Vector3d(self.Position.X, self.Position.Y, 0);
                force += Steering.Seek(toAllies, self.Velocity, world) * AllyWeight;
            }

            // 搶五芒星
            if (self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                force += Steering.Seek(nearestStar - self.Position, self.Velocity, world) * StarWeight;
            }

            // 吃最近的食物
            if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
            }
            return force;
        }
    }
}
