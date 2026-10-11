namespace DellGamingControlCenter.App.Controls;

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

/// <summary>
/// Lightweight helper to construct 3D meshes (boxes, beveled slabs, cylinders)
/// for native WPF Viewport3D hardware visualizations.
/// Produces frozen MeshGeometry3D for zero runtime allocation and maximum DirectX efficiency.
/// </summary>
public sealed class MeshBuilder3D
{
    private readonly Point3DCollection _positions = new();
    private readonly Vector3DCollection _normals = new();
    private readonly Int32Collection _indices = new();

    public void AddBox(Point3D center, double sizeX, double sizeY, double sizeZ)
    {
        double hx = sizeX / 2.0;
        double hy = sizeY / 2.0;
        double hz = sizeZ / 2.0;

        // Front Face (Z+)
        AddQuad(
            new Point3D(center.X - hx, center.Y - hy, center.Z + hz),
            new Point3D(center.X + hx, center.Y - hy, center.Z + hz),
            new Point3D(center.X + hx, center.Y + hy, center.Z + hz),
            new Point3D(center.X - hx, center.Y + hy, center.Z + hz),
            new Vector3D(0, 0, 1));

        // Back Face (Z-)
        AddQuad(
            new Point3D(center.X + hx, center.Y - hy, center.Z - hz),
            new Point3D(center.X - hx, center.Y - hy, center.Z - hz),
            new Point3D(center.X - hx, center.Y + hy, center.Z - hz),
            new Point3D(center.X + hx, center.Y + hy, center.Z - hz),
            new Vector3D(0, 0, -1));

        // Top Face (Y+)
        AddQuad(
            new Point3D(center.X - hx, center.Y + hy, center.Z + hz),
            new Point3D(center.X + hx, center.Y + hy, center.Z + hz),
            new Point3D(center.X + hx, center.Y + hy, center.Z - hz),
            new Point3D(center.X - hx, center.Y + hy, center.Z - hz),
            new Vector3D(0, 1, 0));

        // Bottom Face (Y-)
        AddQuad(
            new Point3D(center.X - hx, center.Y - hy, center.Z - hz),
            new Point3D(center.X + hx, center.Y - hy, center.Z - hz),
            new Point3D(center.X + hx, center.Y - hy, center.Z + hz),
            new Point3D(center.X - hx, center.Y - hy, center.Z + hz),
            new Vector3D(0, -1, 0));

        // Right Face (X+)
        AddQuad(
            new Point3D(center.X + hx, center.Y - hy, center.Z + hz),
            new Point3D(center.X + hx, center.Y - hy, center.Z - hz),
            new Point3D(center.X + hx, center.Y + hy, center.Z - hz),
            new Point3D(center.X + hx, center.Y + hy, center.Z + hz),
            new Vector3D(1, 0, 0));

        // Left Face (X-)
        AddQuad(
            new Point3D(center.X - hx, center.Y - hy, center.Z - hz),
            new Point3D(center.X - hx, center.Y - hy, center.Z + hz),
            new Point3D(center.X - hx, center.Y + hy, center.Z + hz),
            new Point3D(center.X - hx, center.Y + hy, center.Z - hz),
            new Vector3D(-1, 0, 0));
    }

    public void AddCylinder(Point3D bottomCenter, double radius, double height, int slices = 12)
    {
        int baseIndex = _positions.Count;
        Point3D topCenter = new(bottomCenter.X, bottomCenter.Y + height, bottomCenter.Z);

        for (int i = 0; i < slices; i++)
        {
            double angle1 = (2.0 * Math.PI * i) / slices;
            double angle2 = (2.0 * Math.PI * (i + 1)) / slices;

            double x1 = radius * Math.Cos(angle1);
            double z1 = radius * Math.Sin(angle1);
            double x2 = radius * Math.Cos(angle2);
            double z2 = radius * Math.Sin(angle2);

            Point3D b1 = new(bottomCenter.X + x1, bottomCenter.Y, bottomCenter.Z + z1);
            Point3D b2 = new(bottomCenter.X + x2, bottomCenter.Y, bottomCenter.Z + z2);
            Point3D t1 = new(topCenter.X + x1, topCenter.Y, topCenter.Z + z1);
            Point3D t2 = new(topCenter.X + x2, topCenter.Y, topCenter.Z + z2);

            Vector3D n1 = new(x1, 0, z1);
            n1.Normalize();
            Vector3D n2 = new(x2, 0, z2);
            n2.Normalize();

            // Side quad
            int idx = _positions.Count;
            _positions.Add(b1); _normals.Add(n1);
            _positions.Add(b2); _normals.Add(n2);
            _positions.Add(t2); _normals.Add(n2);
            _positions.Add(t1); _normals.Add(n1);

            _indices.Add(idx);
            _indices.Add(idx + 1);
            _indices.Add(idx + 2);
            _indices.Add(idx);
            _indices.Add(idx + 2);
            _indices.Add(idx + 3);

            // Top cap triangle
            int topIdx = _positions.Count;
            _positions.Add(topCenter); _normals.Add(new Vector3D(0, 1, 0));
            _positions.Add(t1);        _normals.Add(new Vector3D(0, 1, 0));
            _positions.Add(t2);        _normals.Add(new Vector3D(0, 1, 0));

            _indices.Add(topIdx);
            _indices.Add(topIdx + 1);
            _indices.Add(topIdx + 2);
        }
    }

    private void AddQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
    {
        int startIndex = _positions.Count;

        _positions.Add(p0);
        _positions.Add(p1);
        _positions.Add(p2);
        _positions.Add(p3);

        _normals.Add(normal);
        _normals.Add(normal);
        _normals.Add(normal);
        _normals.Add(normal);

        // First triangle
        _indices.Add(startIndex);
        _indices.Add(startIndex + 1);
        _indices.Add(startIndex + 2);

        // Second triangle
        _indices.Add(startIndex);
        _indices.Add(startIndex + 2);
        _indices.Add(startIndex + 3);
    }

    public MeshGeometry3D ToMesh()
    {
        var mesh = new MeshGeometry3D
        {
            Positions = _positions,
            Normals = _normals,
            TriangleIndices = _indices
        };
        mesh.Freeze();
        return mesh;
    }
}
