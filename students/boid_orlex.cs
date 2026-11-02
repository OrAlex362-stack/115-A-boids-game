using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // orlex：超強攻擊性、成群獵殺
    // 重點：我的前方 60 度碰到對方的正後方 60 度才殺得到，所以不是直直衝，而是
    //   1. 同伴看到的敵人差不多，大家都挑「離群體中心最近」的同一個目標 → 自然變成一群圍著追同一個
    //   2. 離目標還遠就先靠攏（聚成一群再出手）；進到攻擊距離就各自繞到對方背後，從背後撞進去
    //   3. 被人從背後逼近時側閃（死了就不能進攻），其餘時間一律攻擊
    public class Boid_orlex : IBrain
    {
        public string Name { get { return "orlex"; } }
        public Color Color { get { return Color.Gold; } }

        const double StrikeDistance = 5.0;     // 離目標多近就不再等同伴，直接進攻
        const double BehindDistance = 3.0;     // 要繞到對方背後時，目標點離對方多遠
        const double SpreadWidth = 3.0;        // 同伴繞背的左右散開範圍，避免全部擠在同一點
        const double DangerDistance = 3.5;     // 敵人從背後進到這個距離就閃
        const double FleeWeight = 3.0;
        const double WallMargin = 3.0;
        const double ObstacleMargin = 2.0;
        const double ChaseWeight = 3.0;
        const double PackWeight = 0.6;         // 靠攏同伴的力量

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d myDirection = Steering.Unit(self.Velocity);

            // 第一層：看一遍鄰居，算同伴中心、找最好的目標、找背後的威脅
            Vector3d packSum = new Vector3d(self.Position.X, self.Position.Y, 0);
            int packCount = 1;
            foreach (Neighbor n in neighbors)
            {
                if (!n.IsFriend) continue;
                packSum += new Vector3d(n.Position.X, n.Position.Y, 0);
                packCount++;
            }
            Point3d packCenter = new Point3d(packSum.X / packCount, packSum.Y / packCount, 0);

            bool hasTarget = false;
            Neighbor target = new Neighbor();
            double bestScore = double.MaxValue;
            bool hasAttacker = false;
            Neighbor attacker = new Neighbor();

            foreach (Neighbor n in neighbors)
            {
                if (n.IsFriend) continue;
                Vector3d toOther = Steering.Unit(n.Position - self.Position);
                Vector3d otherDirection = Steering.Unit(n.Velocity);

                // 分數越小越好：離群體中心近（同伴會挑到同一個）、對方背對我（在逃）、我面向他
                double score = 0.5 * packCenter.DistanceTo(n.Position) + 0.3 * n.Distance
                    - 2.0 * (otherDirection * toOther) - 1.5 * (myDirection * toOther);
                if (score < bestScore) { bestScore = score; target = n; hasTarget = true; }

                bool theyComeAtMe = otherDirection * toOther < -0.5;
                bool imFacingAway = myDirection * toOther < -0.5;
                if (theyComeAtMe && imFacingAway && n.Distance < DangerDistance) { attacker = n; hasAttacker = true; }
            }

            // 第二層：決定這一步最想做什麼
            Vector3d force = Vector3d.Zero;
            Vector3d toPack = packCenter - self.Position;
            if (hasAttacker)
            {
                // 被人從背後逼近：往旁邊閃，把背轉開（死掉就沒辦法進攻了）
                Vector3d away = Steering.Unit(self.Position - attacker.Position);
                Vector3d side = new Vector3d(-away.Y, away.X, 0);
                force += Steering.Seek(away + side, self.Velocity, world) * FleeWeight;
            }
            else if (hasTarget)
            {
                Vector3d targetDirection = Steering.Unit(target.Velocity);
                Vector3d toTarget = target.Position - self.Position;
                bool imBehindTarget = targetDirection * Steering.Unit(toTarget) > 0.3;
                Vector3d aim;
                if (imBehindTarget)
                {
                    // 已經在背後：預測對方位置，直接追過去
                    double time = target.Distance / world.MaxSpeed;
                    aim = (target.Position + target.Velocity * time * 0.7) - self.Position;
                }
                else
                {
                    // 還在側面或正面：繞向背後的點，左右隨機散開，同伴不會全擠在同一點
                    Vector3d side = new Vector3d(-targetDirection.Y, targetDirection.X, 0);
                    double spread = (self.Rng.NextDouble() - 0.5) * SpreadWidth;
                    aim = (target.Position - targetDirection * BehindDistance + side * spread) - self.Position;
                }
                force += Steering.Seek(aim, self.Velocity, world) * ChaseWeight;
                // 離目標還遠時，先和同伴靠在一起再一起上
                if (target.Distance > StrikeDistance && packCount > 1)
                    force += Steering.Seek(toPack, self.Velocity, world) * PackWeight;
            }
            else if (self.Stars.Count > 0)
            {
                // 五芒星：吃到無敵 100 步，最適合衝進敵群
                Point3d nearestStar = self.Stars[0];
                foreach (Point3d star in self.Stars)
                    if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                force += Steering.Seek(nearestStar - self.Position, self.Velocity, world) * ChaseWeight;
            }
            else if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(nearest - self.Position, self.Velocity, world);
                if (packCount > 1) force += Steering.Seek(toPack, self.Velocity, world) * PackWeight;
            }
            else
            {
                // 沒看到敵人：成群往場地中心巡邏找人，加一點隨機搖擺（用 self.Rng）
                Vector3d wiggle = new Vector3d(self.Rng.NextDouble() - 0.5, self.Rng.NextDouble() - 0.5, 0);
                force += Steering.Seek((world.Center - self.Position) * 0.05 + wiggle, self.Velocity, world);
                if (packCount > 1) force += Steering.Seek(toPack, self.Velocity, world) * PackWeight;
            }

            // 第三層：不要撞牆、不要貼著障礙物
            double edge = world.HalfSize - WallMargin;
            if (Math.Abs(self.Position.X) > edge || Math.Abs(self.Position.Y) > edge)
                force += Steering.Seek(world.Center - self.Position, self.Velocity, world) * 2;
            foreach (Obstacle o in world.Obstacles)
            {
                if (o.DistanceTo(self.Position) >= ObstacleMargin) continue;
                Vector3d fromObstacle = self.Position - o.ClosestPoint(self.Position);
                if (fromObstacle.Length < 1e-9) fromObstacle = self.Position - o.Center;
                force += Steering.Seek(fromObstacle, self.Velocity, world) * 2;
            }
            return force;
        }
    }
}
