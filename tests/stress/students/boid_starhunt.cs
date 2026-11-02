using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 五芒星獵人（starhunt）
    // 性格：
    //   1. 一看到五芒星，全隊（不論是否有敵人在附近）都往最近的星衝。
    //   2. 自己無敵（self.InvincibleSteps > 0）時變成殺手：衝進敵群，從背後追殺沒有無敵的敵人。
    //   3. 平常（沒無敵）保守：敵人靠近就躲，特別是被敵人從背後追的時候；同時跟友軍抱團、順路吃食物。
    public class Boid_starhunt : IBrain, IDeclareRelations
    {
        public string Name { get { return "starhunt"; } }
        public Color Color { get { return Color.Magenta; } }   // 洋紅色，在場上很好辨認

        // ---- 平常（保守）的參數 ----
        const double FleeDistance = 5.0;        // 敵軍靠到這個距離以內就開始躲
        const double DangerDistance = 8.0;      // 被追殺中的敵人，在這個距離內就要躲
        const double FleeWeight = 2.0;          // 一般躲敵人的力量
        const double DangerFleeWeight = 3.5;    // 被敵人從背後追時，躲得更用力
        const double AllyWeight = 0.5;          // 靠近友軍中心的力量
        const double AlignWeight = 0.3;         // 跟友軍同向的力量
        const double FoodWeight = 0.9;          // 吃食物的力量
        const double StarWeight = 4.0;          // 搶五芒星的力量（比躲敵人還大，一心搶星）
        const double OpportunityWeight = 1.2;   // 剛好繞到無敵以外的敵人背後時，順手咬一口的力量

        // ---- 無敵（衝鋒）的參數 ----
        const double HuntWeight = 3.0;          // 追殺敵人的力量
        const double LeadTime = 3.0;            // 預測敵人位置時，往前抓幾步的距離
        const double SeekEnemyWeight = 1.0;     // 視野內沒敵人時，往場地中心找敵人的力量
        const double WanderWeight = 0.4;        // 沒目標時的隨機晃動

        // 角度判定：前方／後方 60 度的錐，半角 30 度，cos(30) 約 0.866
        const double ConeCos = 0.866;

        // 開賽前引擎只呼叫一次：使用預設（同隊友軍、異隊敵軍），不需要額外設定
        public void Declare(RelationTable table)
        {
        }

        // 把向量壓平到 XY 平面（這是 2D 遊戲）
        static Vector3d Flat(Vector3d v)
        {
            return new Vector3d(v.X, v.Y, 0);
        }

        static Vector3d Flat(Point3d p)
        {
            return new Vector3d(p.X, p.Y, 0);
        }

        // 檢查數字有沒有壞掉（NaN／無限大）
        static bool IsBad(Vector3d v)
        {
            return double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z)
                || double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z);
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d myPos = Flat(self.Position);
            Vector3d myDir = Steering.Unit(Flat(self.Velocity));   // 我現在的朝向（停住時是零向量）
            bool invincible = self.InvincibleSteps > 0;

            // ===== 第一層：掃一遍鄰居，分開統計 =====
            Vector3d allyPosition = Vector3d.Zero, allyVelocity = Vector3d.Zero;
            int allyCount = 0;
            bool hasTarget = false;            // 有沒有可以獵殺的敵人（沒無敵的）
            Neighbor target = new Neighbor();
            double targetScore = double.MaxValue;
            bool hasNearEnemy = false;         // 有沒有太近的敵人
            Neighbor nearEnemy = new Neighbor();
            Vector3d dangerPush = Vector3d.Zero;   // 被追殺的那種敵人，所有逃離方向的加總
            Vector3d fleePush = Vector3d.Zero;     // 一般靠太近的敵人，逃離方向的加總
            Vector3d bitePush = Vector3d.Zero;     // 我剛好在對方背後的機會

            foreach (Neighbor n in neighbors)
            {
                if (n.Relation == Relation.Ally)
                {
                    allyPosition += Flat(n.Position);
                    allyVelocity += Flat(n.Velocity);
                    allyCount++;
                    continue;
                }
                if (n.Relation != Relation.Enemy) continue;   // 不相關的不理

                Vector3d toEnemy = Flat(n.Position) - myPos;           // 我指向敵人
                Vector3d toEnemyDir = Steering.Unit(toEnemy);
                Vector3d enemyDir = Steering.Unit(Flat(n.Velocity));   // 敵人的朝向
                bool enemyInvincible = n.InvincibleSteps > 0;

                if (!hasNearEnemy || n.Distance < nearEnemy.Distance) { nearEnemy = n; hasNearEnemy = true; }

                // --- 我的無敵衝鋒：挑最好咬的敵人 ---
                // 分數越小越好：距離近優先；我在對方背後（對方朝向與我指向它的方向一致）再大幅加分
                if (!enemyInvincible)
                {
                    double score = n.Distance;
                    if (enemyDir * toEnemyDir > ConeCos) score -= 3.0;   // 它背對我：容易咬
                    if (score < targetScore) { targetScore = score; target = n; hasTarget = true; }
                }

                // --- 平常的保守：判斷這隻敵人對我是不是威脅 ---
                // 敵人的朝向對著我（它的前方錐包含我），且我是背對它逃（我在它前方、它在我後方）＝危險
                Vector3d fromEnemyDir = -toEnemyDir;                   // 敵人指向我的反方向 = 我相對於敵人的方向
                bool enemyFacesMe = enemyDir * fromEnemyDir > ConeCos;   // 敵人正對著我衝
                bool iAmFacingAway = myDir.Length > 0 && myDir * toEnemyDir < -0.3;   // 我背對它
                if (n.Distance < DangerDistance && enemyFacesMe && iAmFacingAway)
                {
                    dangerPush += Steering.Unit(-toEnemy) * (1.0 + (DangerDistance - n.Distance) / DangerDistance);
                }
                else if (n.Distance < FleeDistance)
                {
                    fleePush += Steering.Unit(-toEnemy) * (1.0 + (FleeDistance - n.Distance) / FleeDistance);
                }

                // --- 機會：我在對方背後且面對它（我的前方錐對上它的後方錐），對方沒無敵就順手咬 ---
                if (!enemyInvincible && myDir.Length > 0 && myDir * toEnemyDir > ConeCos && enemyDir * toEnemyDir > ConeCos
                    && n.Distance < FleeDistance * 1.5)
                {
                    bitePush += toEnemyDir;
                }
            }

            // ===== 第二層：依狀態決定行為 =====
            bool starSeen = self.Stars != null && self.Stars.Count > 0;

            if (invincible)
            {
                // ---- 無敵：衝鋒模式，不躲任何人 ----
                if (hasTarget)
                {
                    // 預測位置：目標往它自己的速度方向再走幾步；距離越近預測越少，避免過衝
                    double lead = Math.Min(LeadTime, target.Distance / Math.Max(world.MaxSpeed, 1e-6));
                    Vector3d aim = Flat(target.Position) + Flat(target.Velocity) * lead - myPos;
                    force += Steering.Seek(aim, self.Velocity, world) * HuntWeight;
                }
                else if (starSeen && self.InvincibleSteps < 30)
                {
                    // 沒獵物、無敵快用完：去補一顆星續命
                    force += Steering.Seek(NearestStar(self) - self.Position, self.Velocity, world) * StarWeight;
                }
                else
                {
                    // 視野裡沒有可咬的敵人：往場地中心找（敵群多半在中間），加一點晃動避免全隊黏成一條線
                    Vector3d toCenter = Flat(world.Center) - myPos;
                    force += Steering.Seek(toCenter, self.Velocity, world) * SeekEnemyWeight;
                    force += Wander(self, world) * WanderWeight;
                }
                if (allyCount > 0)
                {
                    // 衝鋒時還是稍微跟著同伴，但力量很小，不拖慢追殺
                    Vector3d toAllies = allyPosition / allyCount - myPos;
                    force += Steering.Seek(toAllies, self.Velocity, world) * (AllyWeight * 0.3);
                }
            }
            else
            {
                // ---- 平常：保守模式 ----
                // 躲敵人：被追殺的優先，一般靠太近的其次
                if (dangerPush.Length > 0)
                    force += Steering.Seek(dangerPush, self.Velocity, world) * DangerFleeWeight;
                else if (fleePush.Length > 0)
                    force += Steering.Seek(fleePush, self.Velocity, world) * FleeWeight;

                // 友軍：靠攏與同向（有危險時力量自然被躲避蓋過）
                if (allyCount > 0)
                {
                    Vector3d toAllies = allyPosition / allyCount - myPos;
                    force += Steering.Seek(toAllies, self.Velocity, world) * AllyWeight;
                    force += Steering.Seek(allyVelocity, self.Velocity, world) * AlignWeight;
                }

                // 機會咬一口（只有在不危險時才做）
                if (dangerPush.Length == 0 && bitePush.Length > 0)
                    force += Steering.Seek(bitePush, self.Velocity, world) * OpportunityWeight;

                // 五芒星：一心搶星，全隊一起；被追殺時仍搶，因為吃到就反過來變殺手
                if (starSeen)
                    force += Steering.Seek(NearestStar(self) - self.Position, self.Velocity, world) * StarWeight;

                // 食物：沒有星、沒有危險時才順路去吃
                if (foods != null && foods.Count > 0 && !starSeen && dangerPush.Length == 0)
                {
                    Point3d nearest = foods[0];
                    foreach (Point3d f in foods)
                        if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                    force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
                }

                // 什麼都沒看到：輕輕晃動，不要呆站
                if (force.Length < 1e-9)
                    force += Wander(self, world) * WanderWeight;
            }

            // 壓平 Z 並確保不回傳壞掉的數字
            force = Flat(force);
            if (IsBad(force)) return Vector3d.Zero;
            return force;
        }

        // 找視野內最近的五芒星（呼叫前要確認 Stars 不是空的）
        static Point3d NearestStar(SelfInfo self)
        {
            Point3d nearest = self.Stars[0];
            foreach (Point3d star in self.Stars)
                if (self.Position.DistanceTo(star) < self.Position.DistanceTo(nearest)) nearest = star;
            return nearest;
        }

        // 沒目標時的隨機晃動：在目前朝向附近隨機偏一點（只用 self.Rng，才能重播）
        static Vector3d Wander(SelfInfo self, World world)
        {
            double angle = (self.Rng.NextDouble() - 0.5) * 1.2;   // 左右各約 35 度
            Vector3d dir = Steering.Unit(Flat(self.Velocity));
            if (dir.Length == 0)
            {
                double a = self.Rng.NextDouble() * 2 * Math.PI;
                dir = new Vector3d(Math.Cos(a), Math.Sin(a), 0);
            }
            double c = Math.Cos(angle), s = Math.Sin(angle);
            Vector3d turned = new Vector3d(dir.X * c - dir.Y * s, dir.X * s + dir.Y * c, 0);
            return Steering.Seek(turned, self.Velocity, world);
        }
    }
}
