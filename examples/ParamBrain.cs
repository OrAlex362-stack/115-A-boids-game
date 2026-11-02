using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // 範例邏輯：幾個權重決定性格。學生可以複製這個檔，改名成 boid_{名字}.cs 再修改
    public class ParamBrain : IBrain
    {
        public string Name { get; private set; }
        public Color Color { get; private set; }
        public double AlignWeight, CohesionWeight, FoodWeight, ChaseWeight, FleeWeight;

        public ParamBrain(string name, Color color, double align, double cohesion, double food, double chase, double flee)
        {
            Name = name;
            Color = color;
            AlignWeight = align;
            CohesionWeight = cohesion;
            FoodWeight = food;
            ChaseWeight = chase;
            FleeWeight = flee;
        }

        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            Vector3d heading = self.Velocity;
            heading = Steering.Unit(heading);

            Vector3d alignSum = Vector3d.Zero, positionSum = Vector3d.Zero;
            int friends = 0;
            Vector3d chase = Vector3d.Zero, flee = Vector3d.Zero;

            foreach (Neighbor n in neighbors)
            {
                Vector3d toOther = n.Position - self.Position;
                toOther = Steering.Unit(toOther);
                if (toOther.Length == 0) continue;
                if (n.IsFriend)
                {
                    alignSum += n.Velocity;
                    positionSum += new Vector3d(n.Position);
                    friends++;
                    continue;
                }
                Vector3d otherHeading = n.Velocity;
                otherHeading = Steering.Unit(otherHeading);
                // 我在對方背後（對方朝遠離我的方向跑）→ 追
                if (heading * toOther > 0.5 && otherHeading * toOther > 0.5) chase += toOther / n.Distance;
                // 對方在我背後、而且朝我來 → 逃
                if (heading * toOther < -0.5 && otherHeading * toOther < -0.5) flee -= toOther / n.Distance;
            }

            Vector3d force = Vector3d.Zero;
            if (friends > 0)
            {
                force += Steering.Seek(alignSum, self.Velocity, world) * AlignWeight;
                force += Steering.Seek(positionSum / friends - new Vector3d(self.Position), self.Velocity, world)
                    * CohesionWeight;
            }
            if (foods.Count > 0)
            {
                Point3d nearest = foods[0];
                foreach (Point3d f in foods)
                    if (self.Position.DistanceTo(f) < self.Position.DistanceTo(nearest)) nearest = f;
                force += Steering.Seek(nearest - self.Position, self.Velocity, world) * FoodWeight;
            }
            force += Steering.Seek(chase, self.Velocity, world) * ChaseWeight;
            force += Steering.Seek(flee, self.Velocity, world) * FleeWeight;

            // 靠近牆就往場地中心轉，避免一直貼牆
            double edge = world.HalfSize - 3;
            if (System.Math.Abs(self.Position.X) > edge || System.Math.Abs(self.Position.Y) > edge)
                force += Steering.Seek(world.Center - self.Position, self.Velocity, world);
            return force;
        }
    }
}
