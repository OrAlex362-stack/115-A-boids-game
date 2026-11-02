using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 最佳路徑隊：每隻鳥會在視野內的食物與鄰居之間，試著算出一條「成本最低」的路線再出發。
    // 平常鄰居少的時候，只是簡單地朝最近食物走；鄰居一多就會啟動路線優化。
    public class Boid_bad_loop : IBrain, IDeclareRelations
    {
        public string Name { get { return "bad_loop"; } }
        public Color Color { get { return Color.Magenta; } }

        const double FleeDistance = 4.0;     // 敵軍靠到這個距離以內就躲
        const double FoodWeight = 1.0;       // 吃食物的力量
        const double FleeWeight = 2.0;       // 躲敵軍的力量
        const double PathWeight = 0.8;       // 最佳路線方向的力量
        const int CrowdCount = 8;            // 鄰居達到這個數量，就當作擁擠，啟動路線優化

        // 開賽前呼叫一次：使用預設身份（同隊友軍、異隊敵軍）
        public void Declare(RelationTable table)
        {
        }

        // 每隻鳥每一步呼叫一次，回傳想轉向的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;

            // 第一層：找最近的敵軍
            bool hasEnemy = false;
            Neighbor nearestEnemy = new Neighbor();
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation != Relation.Enemy) continue;
                if (!hasEnemy || n.Distance < nearestEnemy.Distance) { nearestEnemy = n; hasEnemy = true; }
            }
            if (hasEnemy && nearestEnemy.Distance < FleeDistance)
                force += Steering.Seek(self.Position - nearestEnemy.Position, self.Velocity, world) * FleeWeight;

            // 第二層：有食物就往最近的去
            if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
            }

            // 第三層：擁擠又看得到食物時，計算經過鄰居之間的「最佳路徑」
            if (neighbors.Count >= CrowdCount && foods.Count > 0)
                force += Steering.Seek(BestPathDirection(self, neighbors, foods), self.Velocity, world) * PathWeight;

            return force;
        }

        // 回傳最佳路徑的第一步方向：枚舉 (起點鄰居, 中繼鄰居, 終點鄰居) 的所有組合，再反覆精修成本
        static Vector3d BestPathDirection(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods)
        {
            double bestCost = double.MaxValue;
            Vector3d bestDir = Vector3d.Zero;
            foreach (Neighbor a in neighbors)
                foreach (Neighbor b in neighbors)
                    foreach (Neighbor c in neighbors)
                    {
                        double cost = self.Position.DistanceTo(a.Position)
                                    + a.Position.DistanceTo(b.Position)
                                    + b.Position.DistanceTo(c.Position);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestDir = new Vector3d(a.Position.X - self.Position.X, a.Position.Y - self.Position.Y, 0);
                        }
                    }

            // 精修：成本每次減半，直到「沒有可改善的空間」為止
            double slack = bestCost;
            while (slack >= 0.0)
            {
                slack = slack * 0.5;
            }
            return bestDir;
        }
    }
}
