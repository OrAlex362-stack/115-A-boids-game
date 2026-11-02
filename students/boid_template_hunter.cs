using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 獵殺型範本：專門追蹤別隊、繞到對方背後攻擊（不參賽，只是範本）
    // 用法：複製這個檔，改檔名成 boid_{你的名字}.cs，class 改成 Boid_{你的名字}，Name 與 Color 改成自己的
    // 重點：我的前方 60 度碰到對方的正後方 60 度才殺得到，所以要「繞到背後再追」，不是直直衝過去
    public class Boid_template_hunter : IBrain
    {
        public string Name { get { return "hunter_template"; } }
        public Color Color { get { return Color.Crimson; } }

        const double BehindDistance = 3.0;     // 要繞到對方背後時，目標點離對方多遠
        const double DangerDistance = 4.0;     // 敵人從背後追我，進到這個距離就逃
        const double WallMargin = 3.0;         // 離牆多近開始轉回場地中心
        const double ObstacleMargin = 2.0;     // 離障礙物表面多近開始閃開
        const double ChaseWeight = 2.0;        // 追擊的力量比其他行為大，才夠積極
        const double FleeWeight = 3.0;         // 被追時逃跑的力量

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d myDirection = Steering.Unit(self.Velocity);
            Vector3d force = Vector3d.Zero;

            // 第一層：掃一遍鄰居，找最好打的敵人，同時找有沒有人從背後追我
            bool hasTarget = false;
            Neighbor target = new Neighbor();
            double bestScore = double.MinValue;
            bool hasAttacker = false;
            Neighbor attacker = new Neighbor();

            foreach (Neighbor n in neighbors)
            {
                if (n.IsFriend) continue;
                Vector3d toOther = Steering.Unit(n.Position - self.Position);
                Vector3d otherDirection = Steering.Unit(n.Velocity);

                // 分數：對方越是背對我（正在逃）、離我越近、我越是面向他，越好打
                double score = 2 * (otherDirection * toOther) + (myDirection * toOther) - 0.2 * n.Distance;
                if (score > bestScore) { bestScore = score; target = n; hasTarget = true; }

                // 對方朝我來、而我正背對他（他在我背後）→ 危險
                bool theyComeAtMe = otherDirection * toOther < -0.5;
                bool imFacingAway = myDirection * toOther < -0.5;
                if (theyComeAtMe && imFacingAway && n.Distance < DangerDistance) { attacker = n; hasAttacker = true; }
            }

            // 第二層：決定這一步最想做什麼
            if (hasAttacker)
            {
                // 有人從背後追我：往遠離他的方向跑
                force += Steering.Seek(self.Position - attacker.Position, self.Velocity, world) * FleeWeight;
            }
            else if (hasTarget)
            {
                Vector3d toTarget = target.Position - self.Position;
                Vector3d targetDirection = Steering.Unit(target.Velocity);
                bool imBehindTarget = targetDirection * Steering.Unit(toTarget) > 0.3;
                Vector3d aim;
                if (imBehindTarget)
                {
                    // 已經在對方背後：預測對方一小段時間後的位置，直接追過去
                    double time = target.Distance / world.MaxSpeed;
                    aim = (target.Position + target.Velocity * time * 0.5) - self.Position;
                }
                else
                {
                    // 在對方側面或正面：先繞向對方背後的一個點
                    aim = (target.Position - targetDirection * BehindDistance) - self.Position;
                }
                force += Steering.Seek(aim, self.Velocity, world) * ChaseWeight;
            }
            else if (foods.Count > 0)
            {
                // 沒看到敵人：先吃食物長大
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(nearest - self.Position, self.Velocity, world);
            }
            else
            {
                // 都沒有：往場地中心找人，加一點隨機搖擺（用引擎給的 self.Rng，不要自己 new Random）
                Vector3d wiggle = new Vector3d(self.Rng.NextDouble() - 0.5, self.Rng.NextDouble() - 0.5, 0);
                force += Steering.Seek((world.Center - self.Position) * 0.05 + wiggle, self.Velocity, world);
            }

            // 第三層：不要撞牆、不要貼著障礙物（引擎會彈開，但彈開浪費轉向）
            double edge = world.HalfSize - WallMargin;
            if (System.Math.Abs(self.Position.X) > edge || System.Math.Abs(self.Position.Y) > edge)
                force += Steering.Seek(world.Center - self.Position, self.Velocity, world) * 2;
            foreach (Obstacle o in world.Obstacles)
            {
                if (o.DistanceTo(self.Position) < ObstacleMargin)
                {
                    // 從矩形上最近的點往外推開
                    Vector3d fromObstacle = self.Position - o.ClosestPoint(self.Position);
                    if (fromObstacle.Length < 1e-9) fromObstacle = self.Position - o.Center;
                    force += Steering.Seek(fromObstacle, self.Velocity, world) * 2;
                }
            }
            return force;
        }
    }
}
