#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdRuntime;
using static GdRuntime.G;
using Environment = Godot.Environment;
using Range = Godot.Range;
using LastCamp;
using LastCamp.Tests;
using LastCamp.Construction;

namespace LastCamp.Tests;

public partial class TestWoodpile : TestCase
{
    public void test_split_firewood_is_closed_at_bark_cleft_and_ends()
    {
        foreach (Variant seed_value in new Godot.Collections.Array { 821, 843, 871 })
        {
            List<Vector3> faces = new List<Vector3>(PropMeshes.split_log_mesh(0.48, 0.096, seed_value.AsInt64()).GetFaces());
            Godot.Collections.Dictionary welded = new Godot.Collections.Dictionary();
            List<int> ids = new List<int>();
            foreach (Vector3 point in faces)
            {
                Vector3I key = new Vector3I((int)roundi(point.X * 1000000.0), (int)roundi(point.Y * 1000000.0), (int)roundi(point.Z * 1000000.0));
                if (!welded.ContainsKey(key))
                {
                    welded[key] = (long)welded.Count;
                }
                ids.Add(welded[key].AsInt32());
            }
            Godot.Collections.Dictionary edges = new Godot.Collections.Dictionary();
            for (long i = 0, i_end = (long)faces.Count; i < i_end; i += 3)
            {
                assert_gt((faces[(int)(i + 1)] - faces[(int)i]).Cross(faces[(int)(i + 2)] - faces[(int)i]).LengthSquared(), 1e-16, "split piece has no collapsed triangles");
                for (long j = 0; j < 3; j++)
                {
                    long a = ids[(int)(i + j)];
                    long b = ids[(int)(i + (j + 1) % 3)];
                    Vector2I edge = new Vector2I((int)mini(a, b), (int)maxi(a, b));
                    edges[edge] = G.to_int(G.get(edges, edge, 0)) + 1;
                }
            }
            foreach (Variant edge_key in edges.Keys)
            {
                Vector2I edge2 = edge_key.AsVector2I();
                assert_eq(edges[edge2], 2, "bark, split faces and end caps meet without openings");
            }
        }
    }

    public void test_rotated_firewood_contacts_the_ground_without_burial()
    {
        ArrayMesh mesh = PropMeshes.split_log_mesh(0.47, 0.104, 829);
        Callable ground = Callable.From((Vector2 at) => 0.5 + at.X * 0.12 - at.Y * 0.07);
        foreach (Variant roll_item in new Godot.Collections.Array { 0.0, 0.66, PI })
        {
            double roll = roll_item.AsDouble();
            Basis basis = new Basis(Vector3.Up, 0.4f) * new Basis(Vector3.Right, (float)roll);
            Transform3D pose = Campsite._settle_firewood(mesh, new Transform3D(basis, new Vector3(0.2f, 4.0f, -0.1f)), new Godot.Collections.Array<Godot.Collections.Dictionary>(), ground);
            double contact = INF;
            foreach (Vector3 vertex in mesh.GetFaces())
            {
                Vector3 point = pose * vertex;
                contact = minf(contact, point.Y - G.to_float(ground.Call(new Vector2(point.X, point.Z))));
            }
            assert_near(contact, -0.0015, 0.00001, "rotated uneven log rests on the real ground plane");
        }
    }

    public void test_stack_contacts_the_overlapping_slope_not_its_highest_corner()
    {
        MeshBuilder support = new MeshBuilder();
        // Known plane y = 0.4 + 0.2x. Its high edge at x=1 is outside the
        // upper piece's footprint, so a bounding-box contact gives a false gap.
        support.add_quad(new Vector3(-1.0f, 0.2f, -1.0f), new Vector3(1.0f, 0.6f, -1.0f), new Vector3(1.0f, 0.6f, 1.0f), new Vector3(-1.0f, 0.2f, 1.0f));
        Godot.Collections.Array<Godot.Collections.Dictionary> supports = Campsite._firewood_faces(support.commit(), Transform3D.Identity);
        BoxMesh upper = new BoxMesh();
        upper.Size = new Vector3(0.2f, 0.1f, 0.3f);
        Callable ground = Callable.From((Vector2 _at) => -2.0);
        foreach (Variant x_item in new Godot.Collections.Array { 0.0, -0.6 })
        {
            double x = x_item.AsDouble();
            Transform3D pose = Campsite._settle_firewood(upper, new Transform3D(Basis.Identity, new Vector3((float)x, 3.0f, 0.0f)), supports, ground);
            double expected_contact = 0.4 + 0.2 * (x + 0.1);
            assert_near(pose.Origin.Y - 0.05, expected_contact - 0.0015, 0.00001, "upper piece touches the lower surface inside its own footprint");
        }
    }
}
