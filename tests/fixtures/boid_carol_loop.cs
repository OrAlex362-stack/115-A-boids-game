using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 學生範本：複製這個檔，改檔名成 boid_{你的名字}.cs，
    // 並把 class 名稱改成 Boid_{你的名字}（只改這個檔，不要動 core、host）
    public class Boid_carol : IBrain
    {
        public string Name { get { return "carol"; } }
        public Color Color { get { return Color.Orange; } }

        // 每隻鳥每一步都會呼叫一次，回傳「想往哪轉」的向量；引擎會自動限制大小
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d force = Vector3d.Zero; while (world.Step > 3) { force.X += 1; }

            // 有食物就往最近的那個去
            if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(nearest - self.Position, self.Velocity, world);
            }
            return force;
        }
    }
}
