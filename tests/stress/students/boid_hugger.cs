using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 貼牆巡邏隊：沿著場地邊緣（距牆一小段距離的方形軌道）逆時針繞圈。
    // 軌道半徑每一步都用 world.HalfSize 重算，所以場地縮小時會自動跟著縮。
    // 背後和側面貼牆，敵人只能從前方和內側來；有食物或五芒星在附近才衝進去吃，吃完回軌道。
    public class Boid_hugger : IBrain, IDeclareRelations
    {
        public string Name { get { return "hugger"; } }
        public Color Color { get { return Color.DeepPink; } }   // 桃紅色，在綠／藍／橘之間很好認

        const double WallGapRatio = 0.10;     // 軌道離牆的距離，佔場地半邊長的比例
        const double WallGapMin = 1.5;        // 軌道離牆距離的下限（場地很小時用）
        const double RingGain = 0.8;          // 偏離軌道時拉回軌道的力道
        const double TangentWeight = 1.0;     // 沿軌道繞圈的力量
        const double RingWeight = 1.2;        // 回到軌道的力量
        const double FleeWeight = 2.5;        // 躲敵軍的力量
        const double ObstacleWeight = 2.0;    // 避開障礙物的力量
        const double FoodWeight = 1.6;        // 衝進去吃食物的力量
        const double StarWeight = 2.0;        // 搶五芒星的力量
        const double ChaseWeight = 1.0;       // 從背後追敵軍的力量
        const double FoodDashDistance = 7.0;  // 食物近到這個距離才衝進去吃
        const double StarDashDistance = 10.0; // 五芒星近到這個距離才去搶
        const double ObstacleMargin = 3.0;    // 離障礙物邊緣這個距離內就推開
        const double FleeDistance = 4.5;      // 敵軍靠到這個距離以內就躲
        const double ChaseDistance = 3.5;     // 敵軍在前方這個距離內且背對我才追

        // 預設同隊友軍、異隊敵軍，不需要特別宣告
        public void Declare(RelationTable table)
        {
        }

        // 每隻鳥每一步都會呼叫一次，回傳「想往哪轉」的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;

            // 相對場地中心的位置（Z 一律當 0）
            double px = self.Position.X - world.Center.X;
            double py = self.Position.Y - world.Center.Y;

            // 第一層：貼牆巡邏——沿方形軌道逆時針繞，並被拉回軌道
            double gap = Math.Max(WallGapMin, world.HalfSize * WallGapRatio);
            double ringHalf = Math.Max(0.5, world.HalfSize - gap);

            // 用高次方當權重，邊的中段只看單一方向，角落平滑混合
            double ax = Math.Pow(Math.Abs(px) / ringHalf + 1e-6, 4);
            double ay = Math.Pow(Math.Abs(py) / ringHalf + 1e-6, 4);
            Vector3d outward = Steering.Unit(new Vector3d(Math.Sign(px) * ax, Math.Sign(py) * ay, 0));   // 指向牆的方向
            Vector3d tangent = new Vector3d(-outward.Y, outward.X, 0);                                    // 逆時針切線

            double offRing = Math.Max(Math.Abs(px), Math.Abs(py)) - ringHalf;   // 正：比軌道更靠牆；負：太靠內
            Vector3d toRing = outward * (-offRing * RingGain);
            if (toRing.Length > 1.0) toRing = Steering.Unit(toRing);

            force += Steering.Seek(tangent, self.Velocity, world) * TangentWeight;
            if (Math.Abs(offRing) > 0.2)
                force += Steering.Seek(toRing, self.Velocity, world) * RingWeight;

            // 第二層：掃鄰居——躲背後與近身的敵軍，從背後追落單的敵軍
            Vector3d heading = Steering.Unit(new Vector3d(self.Velocity.X, self.Velocity.Y, 0));
            bool enemyNear = false;
            foreach (Neighbor n in neighbors)
            {
                if (n.Relation != Relation.Enemy) continue;
                Vector3d toEnemy = new Vector3d(n.Position.X - self.Position.X, n.Position.Y - self.Position.Y, 0);
                double dist = n.Distance;
                if (dist < 1e-6) continue;
                Vector3d dir = toEnemy / dist;
                double front = heading * dir;   // 正：敵人在我前方；負：在我背後

                if (dist < FleeDistance && self.InvincibleSteps <= 0)
                {
                    enemyNear = true;
                    // 越近越用力躲；在背後的敵人最危險，額外加重
                    double urgency = (FleeDistance - dist) / FleeDistance;
                    if (front < 0) urgency *= 1.5;
                    force += Steering.Seek(dir * -1.0, self.Velocity, world) * FleeWeight * urgency;
                }
                else if (dist < ChaseDistance && front > 0.8 && n.InvincibleSteps <= 0)
                {
                    // 敵人在我正前方，而且正在遠離我（我在它背後）：追上去殺
                    Vector3d enemyHeading = Steering.Unit(new Vector3d(n.Velocity.X, n.Velocity.Y, 0));
                    if (enemyHeading * dir > 0.5)
                        force += Steering.Seek(dir, self.Velocity, world) * ChaseWeight;
                }
            }

            // 第三層：避開障礙物
            foreach (Obstacle o in world.Obstacles)
            {
                double d = o.DistanceTo(self.Position);
                if (d < ObstacleMargin)
                {
                    Point3d edge = o.ClosestPoint(self.Position);
                    Vector3d away = new Vector3d(self.Position.X - edge.X, self.Position.Y - edge.Y, 0);
                    if (away.Length < 1e-6)   // 剛好在邊上：改從障礙物中心往外推
                        away = new Vector3d(self.Position.X - o.Center.X, self.Position.Y - o.Center.Y, 0);
                    force += Steering.Seek(away, self.Velocity, world) * ObstacleWeight * ((ObstacleMargin - d) / ObstacleMargin);
                }
            }

            // 第四層：偶爾衝進去吃——只有東西夠近、附近沒有威脅才離開軌道
            if (self.Stars.Count > 0)
            {
                Point3d star = self.Stars[0];
                foreach (Point3d s in self.Stars)
                    if (self.Position.DistanceTo(s) < self.Position.DistanceTo(star)) star = s;
                if (self.Position.DistanceTo(star) < StarDashDistance)
                    force += Steering.Seek(star - self.Position, self.Velocity, world) * StarWeight;
            }

            if (!enemyNear && foods.Count > 0)
            {
                Point3d food = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(food)) food = f;
                double fd = self.Position.DistanceTo(food);
                if (fd < FoodDashDistance)
                {
                    // 越近越想吃，吃到就複製成兩隻
                    double eager = 1.0 + (FoodDashDistance - fd) / FoodDashDistance;
                    force += Steering.Seek(food - self.Position, self.Velocity, world) * FoodWeight * eager;
                }
            }

            // 防呆：任何分量不是有限數字就回傳零向量，避免整隊被淘汰
            if (double.IsNaN(force.X) || double.IsNaN(force.Y) || double.IsInfinity(force.X) || double.IsInfinity(force.Y))
                return Vector3d.Zero;
            return new Vector3d(force.X, force.Y, 0);
        }
    }
}
