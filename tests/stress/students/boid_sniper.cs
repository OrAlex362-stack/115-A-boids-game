using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 狙擊手：專挑落單、背對自己的敵人，先繞到它背後，再以預測位置一擊必殺。
    // 沒有可打的目標時，才去搶五芒星與食物。
    public class Boid_sniper : IBrain, IDeclareRelations
    {
        public string Name { get { return "sniper"; } }
        public Color Color { get { return Color.Crimson; } }

        const double IsolationRadius = 6.0;    // 目標周圍這個範圍內的其他敵人算「同伴」
        const double CompanionPenalty = 8.0;   // 每多一個同伴，目標的評分加上這個懲罰（越低越好）
        const double FacingAwayBonus = 5.0;    // 目標背對我時，評分減掉這個獎勵
        const double BehindOffset = 3.0;       // 繞背時，瞄準目標背後這麼遠的位置
        const double StrikeRange = 5.0;        // 已在背後且距離小於此值就直接衝刺
        const double FleeDistance = 3.5;       // 非目標的敵人從背後靠近到這個距離就閃
        const double WallMargin = 3.0;         // 離牆或障礙物這麼近就開始往外推
        const double MaxLead = 4.0;            // 預測位置最多往前抓幾步
        const double ChaseWeight = 1.6;        // 追擊的力量
        const double FleeWeight = 2.0;         // 閃躲的力量
        const double AvoidWeight = 2.5;        // 避牆與障礙物的力量
        const double StarWeight = 1.5;         // 搶五芒星的力量
        const double FoodWeight = 1.0;         // 吃食物的力量

        // 狙擊手不需要特別設定身份：預設同隊友軍、異隊敵軍
        public void Declare(RelationTable table)
        {
        }

        // 把向量壓平到 2D（Z 一律當 0）
        static Vector3d Flat(Vector3d v)
        {
            return new Vector3d(v.X, v.Y, 0);
        }

        static Vector3d Flat(Point3d p)
        {
            return new Vector3d(p.X, p.Y, 0);
        }

        // 每隻鳥每一步呼叫一次
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d myPos = Flat(self.Position);
            Vector3d myDir = Steering.Unit(Flat(self.Velocity));

            // 第一層：在敵人當中挑出最好的狙擊目標
            bool hasTarget = false;
            Neighbor target = new Neighbor();
            double bestScore = double.MaxValue;
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation != Relation.Enemy) continue;
                if (n.InvincibleSteps > 0) continue;   // 無敵的殺不死，不用追

                // 數目標周圍有幾個其他敵人（同伴越多越危險）
                int companions = 0;
                foreach (Neighbor m in neighbors)
                {
                    if (m.Relation == Relation.Ally) continue;
                    if (m.Position.DistanceTo(n.Position) < 0.0001) continue;   // 自己本人
                    if (m.Position.DistanceTo(n.Position) < IsolationRadius) companions++;
                }

                // 目標是否背對我：目標前進方向與「從我指向它」的方向同向
                Vector3d toTarget = Steering.Unit(Flat(n.Position) - myPos);
                Vector3d targetDir = Steering.Unit(Flat(n.Velocity));
                double facingAway = targetDir * toTarget;   // 越接近 1 越背對我

                double score = n.Distance + companions * CompanionPenalty - facingAway * FacingAwayBonus;
                if (score < bestScore) { bestScore = score; target = n; hasTarget = true; }
            }

            // 第二層：有目標就繞背、預測、衝刺；沒目標才吃東西
            if (hasTarget)
            {
                Vector3d tPos = Flat(target.Position);
                Vector3d tVel = Flat(target.Velocity);
                Vector3d tDir = Steering.Unit(tVel);
                double speed = Math.Max(world.MaxSpeed, 0.0001);

                // 預測位置：目標在我抵達的時間內會走到哪
                double lead = Math.Min(target.Distance / speed, MaxLead);
                Vector3d predicted = tPos + tVel * lead;

                // 我相對目標的位置：dot < 0 代表我在它背後
                Vector3d fromTarget = myPos - tPos;
                double behind = fromTarget * tDir;
                bool inBack = behind < 0;

                Vector3d aim;
                if (inBack && target.Distance < StrikeRange)
                {
                    aim = predicted - myPos;   // 已繞到背後：直接撞預測位置
                }
                else if (inBack)
                {
                    aim = (predicted - tDir * BehindOffset * 0.5) - myPos;   // 在背後但還遠：貼近
                }
                else
                {
                    // 在前方或側邊：先往側邊繞到背後的位置，避免從正面撞上
                    Vector3d side = new Vector3d(-tDir.Y, tDir.X, 0);
                    double sign = (fromTarget * side) >= 0 ? 1.0 : -1.0;
                    Vector3d waypoint = predicted - tDir * (BehindOffset + 2.0) + side * sign * 3.0;
                    aim = waypoint - myPos;
                }
                force += Steering.Seek(aim, self.Velocity, world) * ChaseWeight;
            }
            else
            {
                if (self.Stars.Count > 0)
                {
                    Point3d nearestStar = self.Stars[0];
                    foreach (Point3d star in self.Stars)
                        if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearestStar)) nearestStar = star;
                    force += Steering.Seek(nearestStar - self.Position, self.Velocity, world) * StarWeight;
                }
                if (foods.Count > 0)
                {
                    Point3d nearest = foods[0];
                    foreach (Point3d f in foods)
                        if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                    force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
                }
                // 完全沒東西：維持原方向前進，不亂轉
                if (foods.Count == 0 && self.Stars.Count == 0 && myDir.Length > 0)
                    force += myDir * 0.1;
            }

            // 第三層：閃躲非目標、且從我背後靠近的敵人（它的正面會碰到我的背）
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation != Relation.Enemy) continue;
                if (hasTarget && n.Position.DistanceTo(target.Position) < 0.0001) continue;
                if (n.Distance >= FleeDistance) continue;
                Vector3d toEnemy = Flat(n.Position) - myPos;
                if (toEnemy * myDir < 0)   // 敵人在我背後半邊
                    force += Steering.Seek(-toEnemy, self.Velocity, world) * FleeWeight;
            }

            // 第四層：避開場地邊界與障礙物
            force += AvoidWalls(self, world) * AvoidWeight;

            // 保險：不能回傳 NaN 或無限大
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) || double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return new Vector3d(force.X, force.Y, 0);
        }

        // 靠近牆或障礙物時，給一個往外的轉向力
        Vector3d AvoidWalls(SelfInfo self, World world)
        {
            Vector3d push = Vector3d.Zero;
            Vector3d p = Flat(self.Position);
            Vector3d c = Flat(world.Center);
            double dx = p.X - c.X;
            double dy = p.Y - c.Y;
            double limit = world.HalfSize - WallMargin;

            if (dx > limit) push += new Vector3d(-1, 0, 0) * ((dx - limit) / WallMargin);
            else if (dx < -limit) push += new Vector3d(1, 0, 0) * ((-limit - dx) / WallMargin);
            if (dy > limit) push += new Vector3d(0, -1, 0) * ((dy - limit) / WallMargin);
            else if (dy < -limit) push += new Vector3d(0, 1, 0) * ((-limit - dy) / WallMargin);

            if (world.Obstacles != null)
            {
                foreach (var ob in world.Obstacles)
                {
                    double d = ob.DistanceTo(self.Position);
                    if (d >= WallMargin) continue;
                    Vector3d away = Flat(self.Position) - Flat(ob.ClosestPoint(self.Position));
                    if (away.Length < 0.0001) away = Flat(self.Position) - Flat(ob.Center);   // 在障礙物裡面：從中心推出
                    push += Steering.Unit(away) * ((WallMargin - d) / WallMargin + 0.3);
                }
            }
            return push;
        }
    }
}
