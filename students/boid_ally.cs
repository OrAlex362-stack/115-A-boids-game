using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 外交家：開賽前用規則替每個隊伍標身份，行為再依身份分開處理
    //   友軍：靠攏、同向（隊名字母序排在我前面的隊伍，加上自己的隊）
    //   敵軍：名字含 bad 的隊伍；平常保持距離，自己無敵時反過來主動追殺
    //   不相關：名字排在我後面而且很短（4 個字以內）的隊伍，完全忽略
    //   其餘沒標到的異隊維持預設的敵軍
    public class Boid_ally : IBrain, IDeclareRelations
    {
        public string Name { get { return "ally"; } }
        public Color Color { get { return Color.DeepPink; } }

        const double FleeDistance = 5.0;       // 敵軍靠到這個距離以內就躲
        const double WallMargin = 6.0;         // 離場地邊界這麼近就往內推
        const double ObstacleMargin = 5.0;     // 離障礙物邊緣這麼近就推開
        const double AllyWeight = 0.7;         // 靠近友軍中心的力量
        const double AlignWeight = 0.5;        // 跟友軍同向的力量
        const double FleeWeight = 2.5;         // 躲敵軍的力量
        const double HuntWeight = 1.8;         // 無敵時追敵軍的力量
        const double FoodWeight = 1.0;         // 吃食物的力量
        const double StarWeight = 1.6;         // 搶五芒星的力量
        const double WallWeight = 2.0;         // 避開場地邊界的力量
        const double ObstacleWeight = 2.5;     // 避開障礙物的力量

        // 開賽前引擎只呼叫一次：用規則一次標好所有隊伍的身份
        public void Declare(RelationTable table)
        {
            foreach (string name in table.AllNames)
            {
                if (name == table.MyName) continue;   // 自己的隊永遠是友軍，不用標

                if (name.ToLowerInvariant().Contains("bad"))
                    table.Enemy(name);                // 名字含 bad：敵軍
                else if (string.CompareOrdinal(name, table.MyName) < 0)
                    table.Ally(name);                 // 字母序在我前面：友軍
                else if (name.Length <= 4)
                    table.Neutral(name);              // 排在我後面又很短：不相關
                // 其他維持預設（敵軍）
            }
        }

        // 每隻鳥每一步呼叫一次，回傳「想往哪轉」的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            double px = self.Position.X, py = self.Position.Y;

            // 第一層：掃鄰居，依身份分開統計
            Vector3d allyPosition = Vector3d.Zero, allyVelocity = Vector3d.Zero;
            int allyCount = 0;
            bool hasEnemy = false, hasPrey = false;
            Neighbor nearestEnemy = new Neighbor();   // 最近的敵軍（拿來躲）
            Neighbor nearestPrey = new Neighbor();    // 最近且不是無敵的敵軍（拿來追）
            if (neighbors != null)
            {
                foreach (Neighbor n in neighbors)
                {
                    if (n.Relation == Relation.Ally)
                    {
                        allyPosition += new Vector3d(n.Position.X, n.Position.Y, 0);
                        allyVelocity += new Vector3d(n.Velocity.X, n.Velocity.Y, 0);
                        allyCount++;
                    }
                    else if (n.Relation == Relation.Enemy)
                    {
                        if (!hasEnemy || n.Distance < nearestEnemy.Distance) { nearestEnemy = n; hasEnemy = true; }
                        if (n.InvincibleSteps <= 0 && (!hasPrey || n.Distance < nearestPrey.Distance)) { nearestPrey = n; hasPrey = true; }
                    }
                    // Relation.Neutral：不相關，完全忽略
                }
            }

            // 第二層：友軍靠攏、同向
            if (allyCount > 0)
            {
                Vector3d toAllies = allyPosition / allyCount - new Vector3d(px, py, 0);
                force += Steering.Seek(toAllies, self.Velocity, world) * AllyWeight;
                force += Steering.Seek(allyVelocity, self.Velocity, world) * AlignWeight;
            }

            // 敵軍：自己無敵就去追（殺得到又不會死），否則太近就躲
            if (self.InvincibleSteps > 0)
            {
                if (hasPrey)
                    force += Steering.Seek(new Vector3d(nearestPrey.Position.X - px, nearestPrey.Position.Y - py, 0), self.Velocity, world) * HuntWeight;
            }
            else if (hasEnemy && nearestEnemy.Distance < FleeDistance)
            {
                force += Steering.Seek(new Vector3d(px - nearestEnemy.Position.X, py - nearestEnemy.Position.Y, 0), self.Velocity, world) * FleeWeight;
            }

            // 五芒星：吃到無敵 100 步
            if (self.Stars != null && self.Stars.Count > 0)
            {
                Point3d star = self.Stars[0];
                foreach (Point3d s in self.Stars)
                    if (self.Position.DistanceTo(s) < self.Position.DistanceTo(star)) star = s;
                force += Steering.Seek(new Vector3d(star.X - px, star.Y - py, 0), self.Velocity, world) * StarWeight;
            }

            // 食物：往最近的去
            if (foods != null && foods.Count > 0)
            {
                Point3d food = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(food)) food = f;
                force += Steering.Seek(new Vector3d(food.X - px, food.Y - py, 0), self.Velocity, world) * FoodWeight;
            }

            // 避開場地邊界：太靠邊就往中心推（縮小期也適用，因為 HalfSize 會跟著變）
            double dx = px - world.Center.X, dy = py - world.Center.Y;
            double limit = world.HalfSize - WallMargin;
            Vector3d wall = Vector3d.Zero;
            if (dx > limit) wall.X = -(dx - limit);
            else if (dx < -limit) wall.X = -(dx + limit);
            if (dy > limit) wall.Y = -(dy - limit);
            else if (dy < -limit) wall.Y = -(dy + limit);
            if (wall.Length > 1e-9)
                force += Steering.Seek(wall, self.Velocity, world) * WallWeight;

            // 避開障礙物：靠近邊緣就從最近點的反方向推開
            if (world.Obstacles != null)
            {
                foreach (var o in world.Obstacles)
                {
                    if (o.DistanceTo(self.Position) >= ObstacleMargin) continue;
                    var c = o.ClosestPoint(self.Position);
                    Vector3d away = new Vector3d(px - c.X, py - c.Y, 0);
                    if (away.Length < 1e-9)   // 剛好壓在邊上：改從障礙物中心往外推
                        away = new Vector3d(px - o.Center.X, py - o.Center.Y, 0);
                    force += Steering.Seek(away, self.Velocity, world) * ObstacleWeight;
                }
            }

            // 保護：只給 XY，不能有 NaN／無限大，否則整隊會被淘汰
            force.Z = 0;
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) ||
                double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return force;
        }
    }
}
