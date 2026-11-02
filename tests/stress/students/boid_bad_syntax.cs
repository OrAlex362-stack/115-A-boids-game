using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 追食物的普通隊伍：有食物就衝，沒有就跟著友軍走，敵人太近就閃
    public class Boid_bad_syntax : IBrain, IDeclareRelations
    {
        public string Name { get { return "bad_syntax"; } }
        public Color Color { get { return Color.Crimson; } }

        const double FleeDistance = 3.5;    // 敵軍靠到這個距離以內就躲
        const double FoodWeight = 1.6;      // 追食物的力量
        const double StarWeight = 1.2;      // 搶五芒星的力量
        const double FleeWeight = 1.8;      // 躲敵軍的力量
        const double GroupWeight = 0.3;     // 靠近友軍中心的力量

        // 開賽前呼叫一次：這隊用預設關係（同隊友軍、異隊敵軍），不做特別設定
        public void Declare(RelationTable table)
        {
        }

        // 每隻鳥每一步呼叫一次，回傳想轉向的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;

            // 統計鄰居：友軍的平均位置、最近的敵軍
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
                else if (n.Relation == Relation.Enemy)
                {
                    if (!hasEnemy || n.Distance < nearestEnemy.Distance) { nearestEnemy = n; hasEnemy = true; }
                }
            }

            // 沒食物時，輕輕靠向友軍中心，避免落單
            if (allyCount > 0 && foods.Count == 0)
            {
                Vector3d toAllies = allyPosition / allyCount - new Vector3d(self.Position.X, self.Position.Y, 0);
                force += Steering.Seek(toAllies, self.Velocity, world) * GroupWeight;
            }

            // 敵軍太近就往反方向躲
            if (hasEnemy && nearestEnemy.Distance < FleeDistance)
                force += Steering.Seek(self.Position - nearestEnemy.Position, self.Velocity, world) * FleeWeight;

            // 看到五芒星就搶
            if (self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                force += Steering.Seek(nearestStar - self.Position, self.Velocity, world) * StarWeight;
            }

            // 有食物就往最近的那個去
            if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(neerest - self.Position, self.Velocity, world) * FoodWeight;
            }
            return force;
        }
    }
}
