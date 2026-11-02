using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 膽小鬼隊：專門保命
    //   1. 看到敵人就逃，離得越近逃得越用力
    //   2. 特別怕「從背後靠近」的敵人（那是被殺的角度），而且逃跑時會往側邊閃，不是直線跑
    //   3. 沒有威脅時躲在障礙物旁邊（貼著邊緣、敵人的反方向那一側）
    //   4. 只在安全時才去吃食物、搶五芒星（附近沒敵人，且食物離我比離敵人近）
    public class Boid_coward : IBrain, IDeclareRelations
    {
        public string Name { get { return "coward"; } }
        public Color Color { get { return Color.DeepPink; } }   // 粉紅色，好辨認

        const double SafeDistance = 7.0;        // 敵人在這個距離內就算有威脅（視野半徑約 10）
        const double BackDangerBonus = 2.5;     // 敵人在背後時，威脅放大的倍數
        const double FleeWeight = 3.0;          // 逃離敵人的力量
        const double SideStepWeight = 1.2;      // 往側邊閃躲的力量
        const double HideDistance = 1.2;        // 離障礙物邊緣多近算「躲好了」
        const double HideWeight = 1.0;          // 衝向障礙物的力量
        const double ObstacleAvoidWeight = 2.0; // 貼太近時推開，避免被彈開或卡住
        const double WallMargin = 3.0;          // 離場地邊界多近開始往回拉
        const double WallWeight = 2.0;          // 離邊界太近時往中心拉的力量
        const double FoodWeight = 1.0;          // 安全時吃食物的力量
        const double StarWeight = 1.5;          // 安全時搶五芒星的力量
        const double AllyWeight = 0.3;          // 沒有障礙物可躲時，微弱地靠向友軍

        // 不特別設定身份：預設同隊友軍、其他隊都是敵軍，全部都怕
        public void Declare(RelationTable table)
        {
        }

        // 把任何向量壓成 XY 平面，並避免 NaN
        static Vector3d Flat(Vector3d v)
        {
            double x = double.IsNaN(v.X) || double.IsInfinity(v.X) ? 0 : v.X;
            double y = double.IsNaN(v.Y) || double.IsInfinity(v.Y) ? 0 : v.Y;
            return new Vector3d(x, y, 0);
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero;
            Vector3d pos = new Vector3d(self.Position.X, self.Position.Y, 0);
            Vector3d heading = Steering.Unit(Flat(self.Velocity));   // 我現在面向的方向

            // 第一層：掃描鄰居，累積威脅
            Vector3d fleeSum = Vector3d.Zero;     // 所有敵人的「逃離方向」加總（越近越重）
            Vector3d sideSum = Vector3d.Zero;     // 閃躲用的側向
            Vector3d enemyCenter = Vector3d.Zero; // 敵人位置平均，用來決定躲在障礙物的哪一側
            Vector3d allyCenter = Vector3d.Zero;
            int enemyCount = 0, allyCount = 0;
            double nearestEnemy = double.MaxValue;
            foreach (Neighbor n in neighbors)
            {
                Vector3d np = new Vector3d(n.Position.X, n.Position.Y, 0);
                if (n.Relation == Relation.Ally)
                {
                    allyCenter += np;
                    allyCount++;
                    continue;
                }
                if (n.Relation != Relation.Enemy) continue;

                enemyCount++;
                enemyCenter += np;
                if (n.Distance < nearestEnemy) nearestEnemy = n.Distance;
                if (n.Distance > SafeDistance) continue;

                Vector3d away = pos - np;                      // 從敵人指向我
                Vector3d awayDir = Steering.Unit(away);
                double closeness = 1.0 - n.Distance / SafeDistance;   // 越近越接近 1
                double danger = closeness * closeness + 0.1;

                // 敵人在我背後嗎？（敵人相對我的方向，和我的前進方向點積為負）
                Vector3d toEnemy = Steering.Unit(np - pos);
                bool behind = heading.X * toEnemy.X + heading.Y * toEnemy.Y < -0.3;
                // 敵人是不是正朝我衝過來？
                Vector3d enemyHeading = Steering.Unit(Flat(n.Velocity));
                bool chasing = enemyHeading.X * awayDir.X + enemyHeading.Y * awayDir.Y > 0.4;   // awayDir 是敵人指向我
                if (behind) danger *= BackDangerBonus;
                if (behind && chasing) danger *= 1.5;

                fleeSum += awayDir * danger;

                // 側閃：垂直於敵人方向，挑和我目前速度同一邊的那側，不要原地打轉
                Vector3d perp = new Vector3d(-awayDir.Y, awayDir.X, 0);
                if (perp.X * heading.X + perp.Y * heading.Y < 0) perp = -perp;
                if (chasing || behind) sideSum += perp * danger;
            }

            bool threatened = fleeSum.Length > 1e-6;

            if (threatened)
            {
                double scale = self.InvincibleSteps > 0 ? 0.4 : 1.0;   // 無敵時就比較不怕，但仍然是膽小鬼
                force += Steering.Seek(fleeSum, self.Velocity, world) * FleeWeight * scale;
                if (sideSum.Length > 1e-6)
                    force += Steering.Seek(sideSum, self.Velocity, world) * SideStepWeight * scale;
            }

            // 第二層：障礙物。找最近的一個
            Obstacle nearestObs = null;
            double nearestObsDist = double.MaxValue;
            foreach (Obstacle o in world.Obstacles)
            {
                double d = o.DistanceTo(self.Position);
                if (d < nearestObsDist) { nearestObsDist = d; nearestObs = o; }
            }

            bool hasCover = nearestObs != null;
            if (hasCover)
            {
                Vector3d edge = new Vector3d(nearestObs.ClosestPoint(self.Position).X, nearestObs.ClosestPoint(self.Position).Y, 0);
                Vector3d fromObs = Steering.Unit(pos - edge);   // 從障礙物邊緣指向我

                if (nearestObsDist < 0.6)
                {
                    // 貼太近：往外推一點，避免撞上被彈開
                    force += Steering.Seek(fromObs, self.Velocity, world) * ObstacleAvoidWeight;
                }
                else if (nearestObsDist > HideDistance)
                {
                    // 想躲：目標點選在障礙物「背對敵人」那一側
                    Vector3d target = edge;
                    if (enemyCount > 0)
                    {
                        Vector3d enemyDir = Steering.Unit(enemyCenter / enemyCount - new Vector3d(nearestObs.Center.X, nearestObs.Center.Y, 0));
                        // 障礙物中心往敵人反方向延伸半個身位，當作藏身處
                        double reach = Math.Max(nearestObs.HalfWidth, nearestObs.HalfHeight) + 0.8;
                        target = new Vector3d(nearestObs.Center.X, nearestObs.Center.Y, 0) - enemyDir * reach;
                    }
                    double weight = threatened ? HideWeight * 0.5 : HideWeight;   // 被追時以逃為主
                    force += Steering.Seek(target - pos, self.Velocity, world) * weight;
                }
                else if (!threatened)
                {
                    // 已經躲好了：把速度煞掉，原地待著
                    force += -self.Velocity * 0.5;
                }
            }
            else if (allyCount > 0 && !threatened)
            {
                // 沒有障礙物（決戰期）：微弱地靠向友軍，至少人多比較安心
                force += Steering.Seek(allyCenter / allyCount - pos, self.Velocity, world) * AllyWeight;
            }

            // 第三層：只在安全時吃東西（附近沒敵人，食物離我比離敵人近）
            bool safe = !threatened && nearestEnemy > SafeDistance * 1.2;
            if (safe)
            {
                if (self.Stars.Count > 0)
                {
                    Point3d star = self.Stars[0];
                    foreach (Point3d s in self.Stars)
                        if (self.Position.DistanceTo(s) < self.Position.DistanceTo(star)) star = s;
                    force += Steering.Seek(star - self.Position, self.Velocity, world) * StarWeight;
                }
                if (foods.Count > 0)
                {
                    Point3d best = foods[0];
                    foreach (Point3d f in foods)
                        if (self.Position.DistanceTo(f) < self.Position.DistanceTo(best)) best = f;
                    // 食物若離障礙物太遠、而我已經躲好，就不冒險；離得近或沒有掩體才去吃
                    bool worthIt = !hasCover || nearestObsDist > HideDistance || self.Position.DistanceTo(best) < 5.0;
                    if (worthIt)
                        force += Steering.Seek(best - self.Position, self.Velocity, world) * FoodWeight;
                }
            }

            // 第四層：別撞牆，離邊界太近就往中心拉
            double fromCenterX = self.Position.X - world.Center.X;
            double fromCenterY = self.Position.Y - world.Center.Y;
            double limit = world.HalfSize - WallMargin;
            Vector3d wall = Vector3d.Zero;
            if (fromCenterX > limit) wall.X = -(fromCenterX - limit);
            else if (fromCenterX < -limit) wall.X = -(fromCenterX + limit);
            if (fromCenterY > limit) wall.Y = -(fromCenterY - limit);
            else if (fromCenterY < -limit) wall.Y = -(fromCenterY + limit);
            if (wall.Length > 1e-6)
                force += Steering.Seek(wall, self.Velocity, world) * WallWeight;

            force = Flat(force);
            return force;
        }
    }
}
