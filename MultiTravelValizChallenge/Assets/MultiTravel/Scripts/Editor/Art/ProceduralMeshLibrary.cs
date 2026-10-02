using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiTravel.EditorTools.Art
{
    /// <summary>
    /// Procedural mesh primitives for the generated environment and product props.
    /// <para>
    /// Conventions: metres; Y up; every closed solid is centred on the origin unless stated otherwise; normals point
    /// outward (or toward the viewer for panels). Unity renders clockwise-wound triangles (seen from the front) as front faces
    /// in its left-handed space, i.e. the face normal is <c>Vector3.Cross(b - a, c - a)</c>. Every builder emits analytic
    /// vertex normals and <see cref="MeshBuilder.ToMesh"/> orients each triangle to agree with them, so winding is correct by construction.
    /// UV0 is always written, tangents are computed with <see cref="Mesh.RecalculateTangents()"/>.
    /// </para>
    /// The <c>Create*</c> functions only build in-memory meshes; <see cref="Save"/> / <see cref="GenerateStandardMeshes"/> persist them
    /// under <see cref="GeneratedAssetUtil.MeshesFolder"/>.
    /// </summary>
    public static class ProceduralMeshLibrary
    {
        // ------------------------------------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------------------------------------

        /// <summary>Asset path used for a generated mesh name.</summary>
        public static string PathFor(string meshName)
        {
            return $"{GeneratedAssetUtil.MeshesFolder}/{meshName}.asset";
        }

        /// <summary>Saves (or updates in place) a mesh as <c>Assets/MultiTravel/Generated/Meshes/&lt;meshName&gt;.asset</c>.</summary>
        public static Mesh Save(Mesh mesh, string meshName)
        {
            return GeneratedAssetUtil.UpsertMesh(mesh, PathFor(meshName));
        }

        /// <summary>Loads a previously generated mesh by name, or null.</summary>
        public static Mesh Load(string meshName)
        {
            return AssetDatabase.LoadAssetAtPath<Mesh>(PathFor(meshName));
        }

        /// <summary>Builds <paramref name="factory"/> and saves it under <paramref name="meshName"/> (regenerated every call, GUID kept).</summary>
        public static Mesh Ensure(string meshName, Func<Mesh> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return Save(factory(), meshName);
        }

        /// <summary>
        /// Generates the shared base mesh set (unit-ish primitives other generators scale/reuse). Idempotent.
        /// </summary>
        public static GeneratorReport GenerateStandardMeshes()
        {
            var report = new GeneratorReport("Generate standard meshes");
            var specs = new List<KeyValuePair<string, Func<Mesh>>>
            {
                Spec("MT_RoundedBox_1m_r05", () => CreateRoundedBox(Vector3.one, 0.05f, 4)),
                Spec("MT_RoundedBox_1m_r15", () => CreateRoundedBox(Vector3.one, 0.15f, 5)),
                Spec("MT_Cylinder_d1_h1", () => CreateCylinder(0.5f, 1f, 48, true)),
                Spec("MT_Cylinder_d1_h1_LowPoly", () => CreateCylinder(0.5f, 1f, 16, true)),
                Spec("MT_Tube_d1_h1_t05", () => CreateTube(0.5f, 0.45f, 1f, 48)),
                Spec("MT_Torus_R04_r01", () => CreateTorus(0.4f, 0.1f, 48, 24)),
                Spec("MT_Capsule_d1_h2", () => CreateCapsule(0.5f, 2f, 32, 8)),
                Spec("MT_TruncatedCone_b05_t03_h1", () => CreateTruncatedCone(0.5f, 0.3f, 1f, 48, true)),
                Spec("MT_Dome_r05", () => CreateDome(0.5f, 48, 16, 1f, true)),
                Spec("MT_Plane_1m", () => CreatePlane(1f, 1f, 1, 1)),
                Spec("MT_Plane_10m_10x10", () => CreatePlane(10f, 10f, 10, 10)),
                Spec("MT_CurvedPanel_R3_A100_H2_5", () => CreateCurvedPanel(3f, 100f, 2.5f, 48, 1, true)),
                Spec("MT_Hat_Straw", () => CreateHat(0.17f, 0.11f, 0.1f, 0.012f, 48)),
            };

            foreach (var spec in specs)
            {
                try
                {
                    bool existed = Load(spec.Key) != null;
                    Ensure(spec.Key, spec.Value);
                    (existed ? report.Updated : report.Created).Add(PathFor(spec.Key));
                }
                catch (Exception ex)
                {
                    report.AddError($"{spec.Key}: {ex.Message}");
                }
            }

            return report;
        }

        private static KeyValuePair<string, Func<Mesh>> Spec(string name, Func<Mesh> f)
        {
            return new KeyValuePair<string, Func<Mesh>>(name, f);
        }

        // ------------------------------------------------------------------------------------------------
        // Primitives
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Box of <paramref name="size"/> with rounded edges/corners of <paramref name="radius"/> (clamped to half the smallest extent),
        /// centred on the origin. <paramref name="segments"/> = subdivisions per 45° of each rounded band. Smooth normals, per-face UVs (0..1).
        /// </summary>
        public static Mesh CreateRoundedBox(Vector3 size, float radius, int segments = 4)
        {
            size = new Vector3(Mathf.Max(size.x, 1e-4f), Mathf.Max(size.y, 1e-4f), Mathf.Max(size.z, 1e-4f));
            Vector3 half = size * 0.5f;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            segments = Mathf.Max(1, segments);
            Vector3 inner = half - new Vector3(r, r, r);

            var axisX = AxisPositions(half.x, inner.x, r, segments);
            var axisY = AxisPositions(half.y, inner.y, r, segments);
            var axisZ = AxisPositions(half.z, inner.z, r, segments);

            var b = new MeshBuilder("RoundedBox");
            // Each face: normal axis n, two in-plane axes (u, v). Positions on the outer box face are projected onto the rounded surface.
            AddRoundedFace(b, Vector3.right, 0, 2, 1, axisZ, axisY, half, inner, r);
            AddRoundedFace(b, Vector3.left, 0, 2, 1, axisZ, axisY, half, inner, r);
            AddRoundedFace(b, Vector3.up, 1, 0, 2, axisX, axisZ, half, inner, r);
            AddRoundedFace(b, Vector3.down, 1, 0, 2, axisX, axisZ, half, inner, r);
            AddRoundedFace(b, Vector3.forward, 2, 0, 1, axisX, axisY, half, inner, r);
            AddRoundedFace(b, Vector3.back, 2, 0, 1, axisX, axisY, half, inner, r);
            return b.ToMesh();
        }

        /// <summary>Cylinder of <paramref name="radius"/> and <paramref name="height"/> along Y, centred on the origin. Hard cap edges.</summary>
        public static Mesh CreateCylinder(float radius, float height, int radialSegments = 32, bool capped = true)
        {
            return CreateTruncatedCone(radius, radius, height, radialSegments, capped);
        }

        /// <summary>
        /// Truncated cone (frustum) along Y, centred on the origin: <paramref name="bottomRadius"/> at y = -h/2, <paramref name="topRadius"/> at +h/2.
        /// Either radius may be 0 (cone). Hard edges between side and caps.
        /// </summary>
        public static Mesh CreateTruncatedCone(float bottomRadius, float topRadius, float height, int radialSegments = 32, bool capped = true)
        {
            float h = height * 0.5f;
            var profile = new List<Vector2>();
            if (capped && bottomRadius > 0f)
            {
                profile.Add(new Vector2(0f, -h));
                profile.Add(new Vector2(bottomRadius, -h));
            }

            profile.Add(new Vector2(bottomRadius, -h));
            profile.Add(new Vector2(topRadius, h));
            if (capped && topRadius > 0f)
            {
                profile.Add(new Vector2(topRadius, h));
                profile.Add(new Vector2(0f, h));
            }

            return CreateLathe(profile, radialSegments, null, "TruncatedCone");
        }

        /// <summary>Hollow tube (pipe/ring) along Y, centred on the origin, with flat annular top and bottom.</summary>
        public static Mesh CreateTube(float outerRadius, float innerRadius, float height, int radialSegments = 32)
        {
            float h = height * 0.5f;
            innerRadius = Mathf.Clamp(innerRadius, 0f, outerRadius - 1e-4f);
            // Closed loop, counter-clockwise in (r, y): bottom (inner->outer), outer wall (up), top (outer->inner), inner wall (down).
            // Duplicated points create hard creases.
            var profile = new List<Vector2>
            {
                new Vector2(innerRadius, -h), new Vector2(outerRadius, -h),
                new Vector2(outerRadius, -h), new Vector2(outerRadius, h),
                new Vector2(outerRadius, h), new Vector2(innerRadius, h),
                new Vector2(innerRadius, h), new Vector2(innerRadius, -h),
            };
            return CreateLathe(profile, radialSegments, null, "Tube");
        }

        /// <summary>Torus lying in the XZ plane, centred on the origin.</summary>
        public static Mesh CreateTorus(float majorRadius, float minorRadius, int majorSegments = 48, int minorSegments = 24)
        {
            minorSegments = Mathf.Max(3, minorSegments);
            var profile = new List<Vector2>(minorSegments + 1);
            var normals = new List<Vector2>(minorSegments + 1);
            for (int i = 0; i <= minorSegments; i++)
            {
                // Counter-clockwise around the tube cross-section, starting at the outer equator.
                float a = (float)i / minorSegments * Mathf.PI * 2f;
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                profile.Add(new Vector2(majorRadius, 0f) + n * minorRadius);
                normals.Add(n);
            }

            return CreateLathe(profile, majorSegments, normals, "Torus");
        }

        /// <summary>Capsule along Y, centred on the origin. <paramref name="height"/> is the total height including both hemispheres.</summary>
        public static Mesh CreateCapsule(float radius, float height, int radialSegments = 32, int hemisphereRings = 8)
        {
            hemisphereRings = Mathf.Max(2, hemisphereRings);
            float cyl = Mathf.Max(0f, height * 0.5f - radius);
            var profile = new List<Vector2>();
            var normals = new List<Vector2>();
            // Bottom pole -> bottom equator.
            for (int i = 0; i <= hemisphereRings; i++)
            {
                float a = -Mathf.PI * 0.5f + (float)i / hemisphereRings * Mathf.PI * 0.5f;
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                profile.Add(new Vector2(0f, -cyl) + n * radius);
                normals.Add(n);
            }

            // Top equator -> top pole (the straight section is the quad strip between the two equators).
            for (int i = 0; i <= hemisphereRings; i++)
            {
                float a = (float)i / hemisphereRings * Mathf.PI * 0.5f;
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                profile.Add(new Vector2(0f, cyl) + n * radius);
                normals.Add(n);
            }

            FixPoleX(profile);
            return CreateLathe(profile, radialSegments, normals, "Capsule");
        }

        /// <summary>
        /// Dome (spherical cap) with its base circle at y = 0 and apex up. <paramref name="coverage"/> 1 = hemisphere, smaller = flatter cap
        /// (fraction of the 90° arc from the apex). <paramref name="closedBase"/> adds a flat bottom disc.
        /// </summary>
        public static Mesh CreateDome(float radius, int radialSegments = 32, int rings = 12, float coverage = 1f, bool closedBase = false)
        {
            rings = Mathf.Max(2, rings);
            coverage = Mathf.Clamp(coverage, 0.05f, 1f);
            float startAngle = Mathf.PI * 0.5f * (1f - coverage); // elevation of the rim
            float baseY = Mathf.Sin(startAngle) * radius;
            var profile = new List<Vector2>();
            var normals = new List<Vector2>();
            float rimR = Mathf.Cos(startAngle) * radius;
            if (closedBase)
            {
                profile.Add(new Vector2(0f, 0f));
                normals.Add(Vector2.down);
                profile.Add(new Vector2(rimR, 0f));
                normals.Add(Vector2.down);
            }

            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.Lerp(startAngle, Mathf.PI * 0.5f, (float)i / rings);
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                profile.Add(new Vector2(n.x * radius, n.y * radius - baseY));
                normals.Add(n);
            }

            FixPoleX(profile);
            return CreateLathe(profile, radialSegments, normals, "Dome");
        }

        /// <summary>Flat plane in XZ, normal +Y, centred on the origin, UV 0..1 across the whole plane.</summary>
        public static Mesh CreatePlane(float width, float depth, int segmentsX = 1, int segmentsZ = 1)
        {
            segmentsX = Mathf.Max(1, segmentsX);
            segmentsZ = Mathf.Max(1, segmentsZ);
            var b = new MeshBuilder("Plane");
            int start = b.VertexCount;
            for (int z = 0; z <= segmentsZ; z++)
            {
                for (int x = 0; x <= segmentsX; x++)
                {
                    float u = (float)x / segmentsX;
                    float v = (float)z / segmentsZ;
                    b.AddVertex(new Vector3((u - 0.5f) * width, 0f, (v - 0.5f) * depth), Vector3.up, new Vector2(u, v));
                }
            }

            b.AddGrid(start, segmentsX + 1, segmentsZ + 1, false);
            return b.ToMesh();
        }

        /// <summary>
        /// Curved backdrop panel: a cylindrical arc of <paramref name="radius"/> around the origin, spanning <paramref name="arcDegrees"/>
        /// centred on +Z, from y = 0 to y = <paramref name="height"/>. The front (concave) side faces the origin; U runs left→right as seen
        /// from the origin, V bottom→top. <paramref name="backFace"/> adds a mirrored back side (separate vertices, outward normals, same UVs).
        /// </summary>
        public static Mesh CreateCurvedPanel(float radius, float arcDegrees, float height, int arcSegments = 48, int heightSegments = 1, bool backFace = true)
        {
            arcSegments = Mathf.Max(1, arcSegments);
            heightSegments = Mathf.Max(1, heightSegments);
            float arc = Mathf.Clamp(arcDegrees, 1f, 359f) * Mathf.Deg2Rad;
            var b = new MeshBuilder("CurvedPanel");
            for (int side = 0; side < (backFace ? 2 : 1); side++)
            {
                int start = b.VertexCount;
                for (int y = 0; y <= heightSegments; y++)
                {
                    float v = (float)y / heightSegments;
                    for (int i = 0; i <= arcSegments; i++)
                    {
                        float u = (float)i / arcSegments;
                        float a = (u - 0.5f) * arc;
                        var radial = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        var normal = side == 0 ? -radial : radial;
                        b.AddVertex(radial * radius + Vector3.up * (v * height), normal, new Vector2(side == 0 ? u : 1f - u, v));
                    }
                }

                b.AddGrid(start, arcSegments + 1, heightSegments + 1, false);
            }

            return b.ToMesh();
        }

        /// <summary>
        /// Surface of revolution around the Y axis. <paramref name="profile"/> holds (radius, y) points ordered so that the outward side is on the
        /// right of the travel direction (e.g. bottom→top for an outside wall, centre→rim for a bottom cap). Two consecutive identical points form
        /// a hard crease. Optional <paramref name="profileNormals"/> (same count, 2D (radial, y)) override the computed normals.
        /// U = angle (0..1, seam duplicated), V = normalised arc length along the profile.
        /// </summary>
        public static Mesh CreateLathe(IList<Vector2> profile, int radialSegments = 32, IList<Vector2> profileNormals = null, string name = "Lathe")
        {
            if (profile == null || profile.Count < 2)
            {
                throw new ArgumentException("Lathe profile needs at least two points.", nameof(profile));
            }

            if (profileNormals != null && profileNormals.Count != profile.Count)
            {
                throw new ArgumentException("profileNormals must match the profile length.", nameof(profileNormals));
            }

            radialSegments = Mathf.Max(3, radialSegments);
            int count = profile.Count;
            var normals2D = profileNormals != null ? new List<Vector2>(profileNormals) : ComputeProfileNormals(profile);

            // Arc length for V.
            var arcLength = new float[count];
            for (int i = 1; i < count; i++)
            {
                arcLength[i] = arcLength[i - 1] + Vector2.Distance(profile[i - 1], profile[i]);
            }

            float total = Mathf.Max(arcLength[count - 1], 1e-6f);
            var b = new MeshBuilder(name);
            int start = b.VertexCount;
            for (int i = 0; i < count; i++)
            {
                var p = profile[i];
                var n = normals2D[i];
                for (int j = 0; j <= radialSegments; j++)
                {
                    float u = (float)j / radialSegments;
                    float a = u * Mathf.PI * 2f;
                    float c = Mathf.Cos(a);
                    float s = Mathf.Sin(a);
                    var pos = new Vector3(p.x * c, p.y, p.x * s);
                    var nrm = new Vector3(n.x * c, n.y, n.x * s);
                    b.AddVertex(pos, nrm, new Vector2(u, arcLength[i] / total));
                }
            }

            // Strips between consecutive profile points; skip zero-length (crease) segments.
            int ring = radialSegments + 1;
            for (int i = 0; i < count - 1; i++)
            {
                if ((profile[i + 1] - profile[i]).sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                for (int j = 0; j < radialSegments; j++)
                {
                    int a0 = start + i * ring + j;
                    int a1 = a0 + 1;
                    int b0 = a0 + ring;
                    int b1 = b0 + 1;
                    b.AddTriangle(a0, b0, b1);
                    b.AddTriangle(a0, b1, a1);
                }
            }

            return b.ToMesh();
        }

        /// <summary>
        /// Simple brimmed hat (e.g. the straw hat prop): a flat annular brim of <paramref name="brimRadius"/> and a rounded crown of
        /// <paramref name="crownRadius"/> × <paramref name="crownHeight"/>; base at y = 0.
        /// </summary>
        public static Mesh CreateHat(float brimRadius, float crownRadius, float crownHeight, float brimThickness, int radialSegments = 48)
        {
            float t = Mathf.Max(0.002f, brimThickness);
            var profile = new List<Vector2>
            {
                // Underside of brim (centre -> rim), facing down.
                new Vector2(0f, 0f), new Vector2(brimRadius, 0f),
                new Vector2(brimRadius, 0f), new Vector2(brimRadius, t),
                new Vector2(brimRadius, t), new Vector2(crownRadius, t),
                new Vector2(crownRadius, t),
            };

            const int crownSteps = 8;
            float wall = crownHeight * 0.65f;
            profile.Add(new Vector2(crownRadius, t + wall));
            for (int i = 1; i <= crownSteps; i++)
            {
                float a = (float)i / crownSteps * Mathf.PI * 0.5f;
                profile.Add(new Vector2(Mathf.Cos(a) * crownRadius, t + wall + Mathf.Sin(a) * (crownHeight - wall)));
            }

            FixPoleX(profile);
            return CreateLathe(profile, radialSegments, null, "Hat");
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        private static void FixPoleX(List<Vector2> profile)
        {
            for (int i = 0; i < profile.Count; i++)
            {
                if (Mathf.Abs(profile[i].x) < 1e-6f)
                {
                    profile[i] = new Vector2(0f, profile[i].y);
                }
            }
        }

        /// <summary>
        /// 2D normals for a lathe profile: the right-hand perpendicular of the travel direction (dy, -dx), averaged across smooth joints.
        /// At a crease (duplicate point) each copy only uses its own segment.
        /// </summary>
        private static List<Vector2> ComputeProfileNormals(IList<Vector2> profile)
        {
            int count = profile.Count;
            var result = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                Vector2 sum = Vector2.zero;
                bool creaseBefore = i > 0 && (profile[i] - profile[i - 1]).sqrMagnitude < 1e-12f;
                bool creaseAfter = i < count - 1 && (profile[i + 1] - profile[i]).sqrMagnitude < 1e-12f;

                if (i > 0 && !creaseBefore)
                {
                    sum += Perp(profile[i] - profile[i - 1]);
                }

                if (i < count - 1 && !creaseAfter)
                {
                    sum += Perp(profile[i + 1] - profile[i]);
                }

                if (sum.sqrMagnitude < 1e-12f)
                {
                    // Isolated point: fall back to any neighbouring segment direction.
                    if (i > 0 && !creaseBefore)
                    {
                        sum = Perp(profile[i] - profile[i - 1]);
                    }
                    else if (i < count - 1 && !creaseAfter)
                    {
                        sum = Perp(profile[i + 1] - profile[i]);
                    }
                    else
                    {
                        sum = Vector2.right;
                    }
                }

                result.Add(sum.normalized);
            }

            return result;
        }

        private static Vector2 Perp(Vector2 direction)
        {
            var d = direction.normalized;
            return new Vector2(d.y, -d.x);
        }

        /// <summary>Per-axis vertex coordinates for the rounded box: corner bands with tan-spaced angles and the flat middle span.</summary>
        private static List<float> AxisPositions(float half, float inner, float radius, int segments)
        {
            var list = new List<float>();
            if (radius <= 1e-6f)
            {
                list.Add(-half);
                list.Add(half);
                return list;
            }

            for (int k = segments; k >= 1; k--)
            {
                float phi = (float)k / segments * Mathf.PI * 0.25f;
                list.Add(-(inner + radius * Mathf.Tan(phi)));
            }

            list.Add(-inner);
            if (inner > 1e-6f)
            {
                list.Add(inner);
            }

            for (int k = 1; k <= segments; k++)
            {
                float phi = (float)k / segments * Mathf.PI * 0.25f;
                list.Add(inner + radius * Mathf.Tan(phi));
            }

            return list;
        }

        private static void AddRoundedFace(MeshBuilder b, Vector3 faceNormal, int nAxis, int uAxis, int vAxis,
            List<float> uPositions, List<float> vPositions, Vector3 half, Vector3 inner, float radius)
        {
            int start = b.VertexCount;
            float sign = faceNormal[nAxis];
            float uMin = uPositions[0], uMax = uPositions[uPositions.Count - 1];
            float vMin = vPositions[0], vMax = vPositions[vPositions.Count - 1];

            for (int vi = 0; vi < vPositions.Count; vi++)
            {
                for (int ui = 0; ui < uPositions.Count; ui++)
                {
                    var p = Vector3.zero;
                    p[nAxis] = sign * half[nAxis];
                    p[uAxis] = uPositions[ui];
                    p[vAxis] = vPositions[vi];

                    var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    var dir = p - core;
                    Vector3 normal = dir.sqrMagnitude > 1e-12f ? dir.normalized : faceNormal;
                    Vector3 position = radius > 1e-6f ? core + normal * radius : p;

                    // Face UVs: u mirrored on negative faces so textures are never reversed when seen from outside.
                    float u = Mathf.InverseLerp(uMin, uMax, p[uAxis]);
                    float v = Mathf.InverseLerp(vMin, vMax, p[vAxis]);
                    if (sign < 0f)
                    {
                        u = 1f - u;
                    }

                    b.AddVertex(position, normal, new Vector2(u, v));
                }
            }

            b.AddGrid(start, uPositions.Count, vPositions.Count, false);
        }

        /// <summary>Accumulates vertices/triangles and orients every triangle to match its vertex normals.</summary>
        public sealed class MeshBuilder
        {
            private readonly string name;
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            public MeshBuilder(string name)
            {
                this.name = name;
            }

            public int VertexCount => vertices.Count;

            public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                vertices.Add(position);
                normals.Add(normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up);
                uvs.Add(uv);
                return vertices.Count - 1;
            }

            /// <summary>Adds a triangle; its winding is corrected in <see cref="ToMesh"/>.</summary>
            public void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            /// <summary>Adds two triangles per cell of a row-major vertex grid starting at <paramref name="start"/>.</summary>
            public void AddGrid(int start, int columns, int rows, bool wrapColumns)
            {
                int cellColumns = wrapColumns ? columns : columns - 1;
                for (int y = 0; y < rows - 1; y++)
                {
                    for (int x = 0; x < cellColumns; x++)
                    {
                        int x1 = (x + 1) % columns;
                        int a = start + y * columns + x;
                        int b = start + y * columns + x1;
                        int c = start + (y + 1) * columns + x1;
                        int d = start + (y + 1) * columns + x;
                        AddTriangle(a, b, c);
                        AddTriangle(a, c, d);
                    }
                }
            }

            /// <summary>
            /// Builds the mesh: drops degenerate triangles, orients each remaining triangle so that
            /// <c>Cross(b - a, c - a)</c> (Unity's clockwise front face) agrees with the summed vertex normals,
            /// writes UV0, recalculates bounds and tangents.
            /// </summary>
            public Mesh ToMesh()
            {
                var oriented = new List<int>(triangles.Count);
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    var pa = vertices[a];
                    var face = Vector3.Cross(vertices[b] - pa, vertices[c] - pa);
                    if (face.sqrMagnitude < 1e-16f)
                    {
                        continue; // degenerate (e.g. at a lathe pole)
                    }

                    var reference = normals[a] + normals[b] + normals[c];
                    if (Vector3.Dot(face, reference) < 0f)
                    {
                        (b, c) = (c, b);
                    }

                    oriented.Add(a);
                    oriented.Add(b);
                    oriented.Add(c);
                }

                var mesh = new Mesh { name = name };
                mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(oriented, 0, true);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
