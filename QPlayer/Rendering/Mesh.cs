using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace QPlayer.Rendering;

public class Mesh : SceneObject
{
    public readonly List<SubMesh> submeshes;
    public bool visible = true;
    public int VertCount => nverts;
    public int TriCount => ntris;

    protected Bounds meshBounds;
    protected int nverts = 0;
    protected int ntris = 0;
    protected Vector3[] vertices = [];
    protected Vector3D<int>[] triangles = [];

    protected static int meshNo = 0;

    public Mesh()
    {
        transform = new();
        submeshes = [];
        name = $"Mesh_{meshNo++}";
        transform.TransformChanged += _ => ComputeBounds();
    }

    public Mesh(Mesh other)
    {
        submeshes = new List<SubMesh>(other.submeshes); // TODO: This doesn't deep copy the VAOs and Materials
        transform.TransformChanged += _ => ComputeBounds();
    }

    public override SceneObject Clone()
    {
        return new Mesh(this);
    }

    public struct SubMesh
    {
        public VertexArrayObject<float, uint> vao;
        public Material mat;
        public uint vertCount;
    }

    public void ComputeBounds()
    {
        (var min, var max) = meshBounds.ToMinMax();
        if (transform.Rot == Quaternion.Identity && transform.Parent == null) // TODO: We could still do this better
        {
            min = Vector3.Transform(min, transform.Matrix);
            max = Vector3.Transform(max, transform.Matrix);
        } 
        else
        {
            // Transform each corner of mesh bounds and then find the min max
            Vector3 _000 = Vector3.Transform(new(min.X, min.Y, min.Z), transform.Matrix);
            Vector3 _001 = Vector3.Transform(new(min.X, min.Y, max.Z), transform.Matrix);
            Vector3 _010 = Vector3.Transform(new(min.X, max.Y, min.Z), transform.Matrix);
            Vector3 _011 = Vector3.Transform(new(min.X, max.Y, max.Z), transform.Matrix);
            Vector3 _100 = Vector3.Transform(new(max.X, min.Y, min.Z), transform.Matrix);
            Vector3 _101 = Vector3.Transform(new(max.X, min.Y, max.Z), transform.Matrix);
            Vector3 _110 = Vector3.Transform(new(max.X, max.Y, min.Z), transform.Matrix);
            Vector3 _111 = Vector3.Transform(new(max.X, max.Y, max.Z), transform.Matrix);
            min = Vector3.Min(Vector3.Min(Vector3.Min(_000, _001), _010), _011);
            min = Vector3.Min(Vector3.Min(Vector3.Min(Vector3.Min(min, _100), _101), _110), _111);
            max = Vector3.Max(Vector3.Max(Vector3.Max(_000, _001), _010), _011);
            max = Vector3.Max(Vector3.Max(Vector3.Max(Vector3.Max(max, _100), _101), _110), _111);
        }
        objBounds = Bounds.FromMinMax(min, max);
    }

    private const float EPSILON = 1e-8f;

    public bool Intersects(Ray ray, out float t)
    {
        Matrix4x4.Invert(transform.Matrix, out var invMat);
        //var invMat = Matrix4x4.Identity;
        var rayStart = Vector3.Transform(ray.start, invMat);
        var rayDir = Vector3.TransformNormal(ray.dir, invMat);

        foreach (var tri in triangles)
        {
            var a = vertices[tri.X];
            var b = vertices[tri.Y];
            var c = vertices[tri.Z];

            var ab = b - a;
            var ac = c - a;
            var n = Vector3.Cross(rayDir, ac);
            var det = Vector3.Dot(ab, n);
            /*var basis = new Matrix4x4(
                ab.X, ab.Y, ab.Z, 0,
                ac.X, ac.Y, ac.Z, 0,
                n.X, n.Y, n.Z, 0,
                a.X, a.Y, a.Z, 1
            );
            Matrix4x4.Invert(basis, out var invBasis);*/
            if (det > -EPSILON && det < EPSILON)
                continue;

            float iDet = MathF.ReciprocalEstimate(det);
            var s = rayStart - a;
            float u = iDet * Vector3.Dot(s, n);

            if (u < 0 || u > 1)
                continue;

            var rayStartCrossAB = Vector3.Cross(s, ab);
            float v = iDet * Vector3.Dot(rayDir, rayStartCrossAB);

            if (v < 0 || u + v > 1)
                continue;

            // At this stage we can compute t to find out where the intersection point is on the line.
            t = iDet * Vector3.Dot(ac, rayStartCrossAB);

            if (t > EPSILON)
            {
                //return new Vector3(rayStart + rayDir * t);
                return true;
            }
        }
        t = 0;
        return false;
    }
}

public readonly record struct Ray(Vector3 start, Vector3 dir)
{
    public readonly Vector3 start = start;
    public readonly Vector3 dir = dir;
    public readonly Vector3 dirInv = new(1 / dir.X, 1 / dir.Y, 1 / dir.Z);
}
