using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 埋伏者：躲在矩形障礙物的背風面（敵人看不到的一側）等人經過，
    // 敵人貼近時從障礙物後面衝出去，繞到對方正後方咬下去；
    // 障礙物消失（場地縮小）或進入決戰期後，改成往中心集結。
    public class Boid_camper : IBrain, IDeclareRelations
    {
        public string Name { get { return "camper"; } }
        public Color Color { get { return Color.Magenta; } }

        const double HideMargin = 2.0;      // 躲藏點離障礙物邊緣多遠
        const double HideSpread = 1.5;      // 躲藏點的隨機散開幅度（避免全部疊在同一點）
        const double HideWeight = 1.2;      // 衝向躲藏點的力量
        const double StrikeRange = 9.0;     // 敵人進到這個距離以內就衝出去
        const double StrikeWeight = 2.5;    // 衝出去咬人的力量
        const double DodgeRange = 3.5;      // 敵人貼在背後這個距離以內就躲
        const double DodgeWeight = 2.0;     // 躲背後敵人的力量
        const double FoodRange = 6.0;       // 食物在這個距離以內才去搶（埋伏時不亂跑）
        const double FoodWeight = 0.9;      // 吃食物的力量
        const double StarWeight = 1.5;      // 搶五芒星的力量
        const double CenterWeight = 1.0;    // 往中心集結的力量
        const double AllyWeight = 0.4;      // 靠攏友軍的力量

        // 用預設身份：同隊友軍、異隊敵軍
        public void Declare(RelationTable table)
        {
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d me = new Vector3d(self.Position.X, self.Position.Y, 0);

            // 第一層：掃一遍鄰居，統計敵軍重心、最近的可咬敵人、友軍重心
            Vector3d enemySum = Vector3d.Zero, allySum = Vector3d.Zero;
            int enemyCount = 0, allyCount = 0;
            bool hasTarget = false;
            Neighbor target = new Neighbor();
            bool hasThreat = false;
            Neighbor threat = new Neighbor();
            double speed = new Vector3d(self.Velocity.X, self.Velocity.Y, 0).Length;
            Vector3d heading = Steering.Unit(new Vector3d(self.Velocity.X, self.Velocity.Y, 0));

            foreach (Neighbor n in neighbors)
            {
                Vector3d p = new Vector3d(n.Position.X, n.Position.Y, 0);
                if (n.Relation == Relation.Ally)
                {
                    allySum += p;
                    allyCount++;
                }
                else if (n.Relation == Relation.Enemy)
                {
                    enemySum += p;
                    enemyCount++;
                    // 無敵的殺不死，不用追
                    if (n.InvincibleSteps <= 0 && (!hasTarget || n.Distance < target.Distance))
                    {
                        target = n;
                        hasTarget = true;
                    }
                    // 在我背後貼近的敵人：可能正要咬我
                    if (speed > 0.1 && n.Distance < DodgeRange)
                    {
                        Vector3d toEnemy = p - me;
                        if (Vector3d.Multiply(Steering.Unit(toEnemy), heading) < -0.5 && (!hasThreat || n.Distance < threat.Distance))
                        {
                            threat = n;
                            hasThreat = true;
                        }
                    }
                }
                // Relation.Neutral：不相關，不理它
            }

            // 第二層：依階段與障礙物決定主要行為
            bool hasCover = world.Phase != 3 && world.Obstacles != null && world.Obstacles.Count > 0;
            bool striking = false;

            if (hasTarget && target.Distance < StrikeRange)
            {
                // 衝出去咬：預判敵人位置，瞄準對方正後方（前方 60 度碰到正後方 60 度才殺得死）
                Vector3d tv = new Vector3d(target.Velocity.X, target.Velocity.Y, 0);
                double lead = Math.Min(target.Distance / Math.Max(world.MaxSpeed, 0.01), 6.0);
                Vector3d aim = new Vector3d(target.Position.X, target.Position.Y, 0) + tv * lead;
                if (tv.Length > 0.05)
                    aim -= Steering.Unit(tv) * 1.0;     // 再往對方背後偏一點
                force += Steering.Seek(aim - me, self.Velocity, world) * StrikeWeight;
                striking = true;
            }

            if (!striking)
            {
                if (hasCover)
                {
                    // 找最近的障礙物，躲到它背風面
                    Obstacle best = null;
                    double bestDist = double.MaxValue;
                    foreach (Obstacle o in world.Obstacles)
                    {
                        double d = o.DistanceTo(self.Position);
                        if (d < bestDist) { bestDist = d; best = o; }
                    }

                    // 背風面 = 背對敵人視線的一側；看不到敵人就以場地中心當作敵人來的方向
                    Vector3d center = new Vector3d(best.Center.X, best.Center.Y, 0);
                    Vector3d threatFrom = enemyCount > 0
                        ? enemySum / enemyCount
                        : new Vector3d(world.Center.X, world.Center.Y, 0);
                    Vector3d away = Steering.Unit(center - threatFrom);
                    if (away.Length < 0.5)      // 敵人剛好在障礙物正上方，隨便挑一邊
                        away = new Vector3d(1, 0, 0);

                    // 沿著「敵人 → 障礙物中心」的方向找到矩形邊界，再向外退一點
                    double ax = Math.Abs(away.X), ay = Math.Abs(away.Y);
                    double tx = ax > 1e-6 ? best.HalfWidth / ax : double.MaxValue;
                    double ty = ay > 1e-6 ? best.HalfHeight / ay : double.MaxValue;
                    double reach = Math.Min(tx, ty) + HideMargin;
                    Vector3d hidePoint = center + away * reach;
                    // 加一點隨機散開，同隊不會全擠在同一點
                    hidePoint += new Vector3d((self.Rng.NextDouble() - 0.5) * 2 * HideSpread,
                                              (self.Rng.NextDouble() - 0.5) * 2 * HideSpread, 0);

                    Vector3d toHide = hidePoint - me;
                    if (toHide.Length > 1.5)
                        force += Steering.Seek(toHide, self.Velocity, world) * HideWeight;     // 趕去躲
                    else
                        force += Steering.Seek(-heading, self.Velocity, world) * 0.5 * HideWeight;   // 到位後減速潛伏
                }
                else
                {
                    // 障礙物沒了或決戰期：往中心集結
                    Vector3d toCenter = new Vector3d(world.Center.X, world.Center.Y, 0) - me;
                    force += Steering.Seek(toCenter, self.Velocity, world) * CenterWeight;
                    if (allyCount > 0)
                        force += Steering.Seek(allySum / allyCount - me, self.Velocity, world) * AllyWeight;
                }

                // 近距離的食物順手吃（埋伏時不跑遠）
                if (foods.Count > 0)
                {
                    Point3d nearest = foods[0];
                    foreach (Point3d f in foods)
                        if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                    if (self.Position.DistanceTo(nearest) < FoodRange)
                        force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
                }
            }

            // 五芒星：吃到無敵 100 步，不管埋伏或衝鋒都值得搶
            if (self.Stars.Count > 0)
            {
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                force += Steering.Seek(nearestStar - self.Position, self.Velocity, world) * StarWeight;
            }

            // 背後有敵人貼近（而且我沒有在衝鋒、自己也不是無敵）就閃開
            if (hasThreat && !striking && self.InvincibleSteps <= 0)
            {
                Vector3d fromEnemy = me - new Vector3d(threat.Position.X, threat.Position.Y, 0);
                force += Steering.Seek(fromEnemy, self.Velocity, world) * DodgeWeight;
            }

            // 保險：避免回傳 NaN 讓整隊被淘汰
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) || double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return new Vector3d(force.X, force.Y, 0);
        }
    }
}
