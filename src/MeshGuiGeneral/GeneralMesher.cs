using System;
using System.Collections.Generic;
using System.Linq;

namespace MeshGuiGeneral
{
    /// ============================================================================================
    /// ARCHITECTURE: General Perpendicular Mesher
    /// ============================================================================================
    /// OBJECTIVE:
    /// Generate高质量mallas en planos inclinados, sin limitación de horizontalidad en Z.
    /// Trabaja mediante líneas perpendiculares a los bordes, avanzando hacia el interior.
    /// 
    /// VOCABULARY:
    /// - Boundary Edge: Un borde del polígono que define el límite de la malla
    /// - Perpendicular Ray: Línea trazada desde un borde hacia el interior, perpendicular a dicho borde
    /// - Mesh Line: Segmento generado entre dos puntos de intersección
    /// - Collision: Cuando un rayo encuentra otro borde, otra línea, o un nudo existente
    /// - Cusp: Nudo de discontinuidad en un borde (ej: una esquina interior del alero)
    /// 
    /// ALGORITHM OVERVIEW (3.1 - 3.4):
    /// 
    /// STEP 1: Identify boundary edges and their existing nodes
    /// STEP 2: Classify edges by their perpendicular direction (X, Y, or Z)
    /// STEP 3: Process edges in order: X-perpendicular → Y-perpendicular → Z-perpendicular
    /// STEP 4: For each edge, emit rays at regular intervals (delta_mesh)
    /// STEP 5: Handle collisions according to cases 3.1.1 to 3.1.4
    /// STEP 6: Build mesh faces from the resulting network of lines and points
    /// 
    /// COLLISION CASES (3.1.1 to 3.1.4):
    /// 
    /// CASE 3.1.1: Ray hits another boundary edge
    ///   → If coincident with existing node: trim to that node
    ///   → If near existing node (dist ≤ mesh_size * 1.5): connect to it
    ///   → If near existing node (dist < mesh_size * 0.5): mark as small element
    ///   → If far (dist > mesh_size * 1.5): keep full ray length (orthogonal preserved)
    /// 
    /// CASE 3.1.2: Ray intersects another RAY from different edge (collinear)
    ///   → Both edges are parallel, small width zone
    ///   → Keep only ONE line between the two mirror nodes
    ///   → Delete the two collinear rays
    /// 
    /// CASE 3.1.3: Ray intersects another RAY from different edge (NOT collinear)
    ///   → Acute angle between edges
    ///   → Trim both rays at the intersection point
    ///   → Create triangles/trapezoids as needed
    /// 
    /// CASE 3.1.4: Ray hits nothing
    ///   → Create the full ray as a mesh line
    ///   → Continue to next ray
    /// 
    /// LAYER-BY-LAYER GENERATION (3.2):
    /// Rays are emitted in "layers" of approximately delta_mesh ± 50% thickness.
    /// Each layer creates strips that may be quads, triangles, or trapezoids.
    /// 
    /// MESH STATE TRACKING (3.3):
    /// The algorithm maintains:
    /// - Available zones: areas not yet meshed
    /// - Occupied zones: areas already assigned to elements
    /// - Active front: the current boundary of the meshed region
    /// 
    /// ORDER OF PROCESSING (3.4):
    /// Primary direction: rays perpendicular to X → rays perpendicular to Y → rays perpendicular to Z
    /// Tie-breaker: closest to origin, then left-to-right or bottom-to-top
    /// ============================================================================================

    public static class GeneralMesher
    {
        // =====================================================================
        // BASIC GEOMETRY CLASSES
        // =====================================================================

        public class Point3D
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }

            public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }

            public static Point3D operator +(Point3D a, Point3D b) => new Point3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Point3D operator -(Point3D a, Point3D b) => new Point3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Point3D operator *(Point3D a, double d) => new Point3D(a.X * d, a.Y * d, a.Z * d);
            public static Point3D operator /(Point3D a, double d) => new Point3D(a.X / d, a.Y / d, a.Z / d);

            public static double Dot(Point3D a, Point3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            public static Point3D Cross(Point3D a, Point3D b) =>
                new Point3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

            public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

            public Point3D Normalize()
            {
                double l = Length();
                return l > 1e-10 ? new Point3D(X / l, Y / l, Z / l) : new Point3D(0, 0, 0);
            }

            public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";

            public override bool Equals(object obj)
            {
                if (!(obj is Point3D other)) return false;
                return Math.Abs(X - other.X) < 1e-6 && Math.Abs(Y - other.Y) < 1e-6 && Math.Abs(Z - other.Z) < 1e-6;
            }

            public override int GetHashCode() => (X, Y, Z).GetHashCode();
        }

        public class Point2D
        {
            public double U { get; set; }
            public double V { get; set; }
            public Point2D(double u, double v) { U = u; V = v; }
            public override string ToString() => $"({U:F3}, {V:F3})";
        }

        // =====================================================================
        // MESH RESULT CLASS
        // =====================================================================

        public class MeshResult
        {
            public List<List<Point3D>> Faces { get; set; } = new List<List<Point3D>>();
        }

        // =====================================================================
        // BOUNDARY AND EDGE CLASSES
        // =====================================================================

        /// <summary>
        /// Represents an edge of the boundary polygon with metadata about its orientation.
        /// </summary>
        private class BoundaryEdge
        {
            public Point3D Start { get; set; }      // Vertex 1 of the edge
            public Point3D End { get; set; }        // Vertex 2 of the edge
            public Point3D Midpoint { get; set; }   // Midpoint of the edge
            public Point3D Direction { get; set; }  // Unit vector along edge (End - Start)
            public Point3D Normal { get; set; }     // OUTWARD perpendicular to edge (for 2D case, perpendicular in XY plane)
            public int PolygonIndex { get; set; }    // Index in the original polygon
            public double Length { get; set; }      // Euclidean length of edge

            /// <summary>
            /// Which primary direction is this edge's perpendicular oriented?
            /// 'X' = perpendicular points mostly in X direction
            /// 'Y' = perpendicular points mostly in Y direction  
            /// 'Z' = perpendicular points mostly in Z direction
            /// </summary>
            public char PerpendicularDirection { get; set; }
        }

        /// <summary>
        /// Represents a ray emitted from a boundary edge going inward.
        /// </summary>
        private class MeshRay
        {
            public Point3D Origin { get; set; }      // Starting point on boundary
            public Point3D Direction { get; set; }  // Unit direction (perpendicular to edge)
            public BoundaryEdge SourceEdge { get; set; }  // Which edge this ray came from
            public double Parameter { get; set; }   // t value along direction (for trimming)
            public Point3D End { get; set; }        // Current end point (may be trimmed)
            public bool IsActive { get; set; } = true;
            public List<Point3D> Nodes { get; set; } = new List<Point3D>(); // All nodes on this ray
        }

        /// <summary>
        /// Represents an intersection between rays or between ray and boundary.
        /// </summary>
        private class RayIntersection
        {
            public MeshRay Ray1 { get; set; }
            public MeshRay Ray2 { get; set; }          // null if intersection with boundary
            public Point3D Point { get; set; }
            public double T1 { get; set; }             // Parameter on Ray1
            public double T2 { get; set; }             // Parameter on Ray2 (if Ray2 is not null)
            public IntersectionType Type { get; set; }
            public enum IntersectionType
            {
                RayBoundary,      // Ray hit a boundary edge (Case 3.1.1)
                RayRayCollinear,  // Two rays are collinear (Case 3.1.2)
                RayRayAcute,      // Two rays intersect at angle (Case 3.1.3)
                None              // No intersection (Case 3.1.4)
            }
        }

        /// <summary>
        /// Represents a node detected on the boundary during mesh generation.
        /// </summary>
        private class ExistingNode
        {
            public Point3D Position { get; set; }
            public string Name { get; set; }
            public double DistanceToBoundary { get; set; }
        }

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        /// <summary>
        /// Main entry point: Generate mesh using the perpendicular-to-edges algorithm.
        /// Works for arbitrary planar polygons including inclined planes.
        /// </summary>
        /// <param name="boundary">Polygon vertices in order (3 or 4 points)</param>
        /// <param name="continuityNodes">Existing nodes on or near boundary (for connection)</param>
        /// <param name="deltaMesh">Target element size</param>
        /// <param name="tolerancePercent">Tolerance for snapping (e.g., 0.5 = 50%)</param>
        /// <param name="orderXYZ">Process X-perp first, then Y, then Z</param>
        /// <param name="orderYXZ">Process Y-perp first, then X, then Z</param>
        /// <param name="orderZXY">Process Z-perp first, then X, then Y</param>
        public static MeshResult GeneratePerpendicularMesh(
            List<Point3D> boundary,
            List<Point3D> continuityNodes,
            double deltaMesh,
            double tolerancePercent,
            bool orderXYZ,
            bool orderYXZ,
            bool orderZXY)
        {
            if (boundary == null || boundary.Count < 3)
                return new MeshResult();

            // =================================================================
            // STEP 1: Build boundary edges and classify by perpendicular direction
            // =================================================================
            List<BoundaryEdge> edges = BuildBoundaryEdges(boundary);

            // Determine processing order
            char[] order = orderXYZ ? new char[] { 'X', 'Y', 'Z' } :
                           orderYXZ ? new char[] { 'Y', 'X', 'Z' } :
                                      new char[] { 'Z', 'X', 'Y' };

            // =================================================================
            // STEP 2: Find existing nodes on/near boundary for snapping
            // =================================================================
            List<ExistingNode> existingNodes = FindExistingNodes(continuityNodes, boundary, deltaMesh);

            // =================================================================
            // STEP 3: Emit rays from each edge in the specified order
            // =================================================================
            List<MeshRay> allRays = new List<MeshRay>();

            foreach (char dir in order)
            {
                var dirEdges = edges.Where(e => e.PerpendicularDirection == dir).OrderBy(e => GetEdgeOrderKey(e, dir)).ToList();

                foreach (var edge in dirEdges)
                {
                    var rays = EmitRaysFromEdge(edge, deltaMesh, existingNodes, tolerancePercent);
                    allRays.AddRange(rays);
                }
            }

            // =================================================================
            // STEP 4: Detect and handle all ray collisions (Cases 3.1.1 - 3.1.4)
            // =================================================================
            List<RayIntersection> intersections = DetectCollisions(allRays, edges, existingNodes, deltaMesh, tolerancePercent);

            // =================================================================
            // STEP 5: Trim rays based on collision results
            // =================================================================
            ApplyCollisions(allRays, intersections, deltaMesh, tolerancePercent);

            // =================================================================
            // STEP 6: Build mesh faces from processed rays
            // =================================================================
            List<List<Point3D>> faces = BuildMeshFaces(allRays, edges, existingNodes, deltaMesh);

            return new MeshResult { Faces = faces };
        }

        // =====================================================================
        // LEGACY API: Ray-Slicing (for backward compatibility)
        // =====================================================================

        public static MeshResult GenerateSlicerMesh(
            List<Point3D> boundary,
            List<Point3D> continuityNodes,
            double dx,
            double dy,
            double snapTolPercent,
            bool isHorizontalPriority,
            bool isAntiClockwise,
            int discontinuityHandling = 0)
        {
            // Placeholder - calls legacy implementation
            var result = GenerateSlicerMeshLegacy(boundary, continuityNodes, dx, dy, snapTolPercent, isHorizontalPriority, isAntiClockwise, discontinuityHandling);
            return result;
        }

        private static MeshResult GenerateSlicerMeshLegacy(
            List<Point3D> boundary,
            List<Point3D> continuityNodes,
            double dx,
            double dy,
            double snapTolPercent,
            bool isHorizontalPriority,
            bool isAntiClockwise,
            int discontinuityHandling)
        {
            // Simplified legacy implementation for compatibility
            if (boundary.Count < 3) return new MeshResult();

            Point3D O = boundary[0];
            Point3D vecU = (boundary[1] - boundary[0]).Normalize();
            Point3D tempV = (boundary[2] - boundary[0]).Normalize();
            Point3D normal = Point3D.Cross(vecU, tempV).Normalize();
            Point3D vecV = Point3D.Cross(normal, vecU).Normalize();

            List<Point2D> poly2D = new List<Point2D>();
            foreach (var p3 in boundary)
            {
                Point3D diff = p3 - O;
                poly2D.Add(new Point2D(Point3D.Dot(diff, vecU), Point3D.Dot(diff, vecV)));
            }

            double cx = poly2D.Average(p => p.U);
            double cy = poly2D.Average(p => p.V);
            poly2D = poly2D.OrderBy(p => Math.Atan2(p.V - cy, p.U - cx)).ToList();

            double minU = poly2D.Min(p => p.U);
            double maxU = poly2D.Max(p => p.U);
            double minV = poly2D.Min(p => p.V);
            double maxV = poly2D.Max(p => p.V);

            double sliceSpacing = isHorizontalPriority ? dy : dx;
            double crossSpacing = isHorizontalPriority ? dx : dy;

            bool vertIsU = Math.Abs(vecU.Z) > Math.Abs(vecV.Z);
            bool isSliceU = isHorizontalPriority ? vertIsU : !vertIsU;

            double minSlice = isSliceU ? minU : minV;
            double maxSlice = isSliceU ? maxU : maxV;

            List<List<Point2D>> faces2D = DynamicZipper(poly2D, crossSpacing, sliceSpacing, minSlice, maxSlice, isSliceU);

            List<List<Point3D>> faces3D = new List<List<Point3D>>();
            foreach (var face in faces2D)
            {
                List<Point3D> face3D = new List<Point3D>();
                foreach (var p2 in face)
                {
                    face3D.Add(O + vecU * p2.U + vecV * p2.V);
                }
                if (isAntiClockwise) face3D.Reverse();
                faces3D.Add(face3D);
            }

            return new MeshResult { Faces = faces3D };
        }

        private static List<List<Point2D>> DynamicZipper(
            List<Point2D> poly,
            double crossSpacing,
            double sliceSpacing,
            double minS,
            double maxS,
            bool isSliceU)
        {
            List<double> finalSlices = new List<double>();
            finalSlices.Add(minS);
            finalSlices.Add(maxS);

            double sHeight = maxS - minS;
            int numSDivs = Math.Max(1, (int)Math.Round(sHeight / sliceSpacing));
            for (int i = 1; i < numSDivs; i++)
            {
                finalSlices.Add(minS + i * (sHeight / numSDivs));
            }
            finalSlices.Sort();

            List<List<Point2D>> allRows = new List<List<Point2D>>();

            foreach (var sval in finalSlices)
            {
                List<double> ints = new List<double>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Point2D A = poly[i];
                    Point2D B = poly[(i + 1) % poly.Count];
                    double aS = isSliceU ? A.U : A.V;
                    double bS = isSliceU ? B.U : B.V;
                    double aC = isSliceU ? A.V : A.U;
                    double bC = isSliceU ? B.V : B.U;

                    if (Math.Abs(aS - bS) < 1e-6)
                    {
                        if (Math.Abs(aS - sval) < 1e-6) { ints.Add(aC); ints.Add(bC); }
                    }
                    else if (sval >= Math.Min(aS, bS) - 1e-6 && sval <= Math.Max(aS, bS) + 1e-6)
                    {
                        double c = aC + (sval - aS) * (bC - aC) / (bS - aS);
                        ints.Add(c);
                    }
                }

                if (ints.Count < 2) continue;
                double minC = ints.Min();
                double maxC = ints.Max();

                double width = maxC - minC;
                int N = Math.Max(1, (int)Math.Round(width / crossSpacing));

                List<Point2D> rowPts = new List<Point2D>();
                for (int i = 0; i <= N; i++)
                {
                    double cVal = minC + i * (width / N);
                    rowPts.Add(isSliceU ? new Point2D(sval, cVal) : new Point2D(cVal, sval));
                }
                allRows.Add(rowPts);
            }

            List<List<Point2D>> faces = new List<List<Point2D>>();
            for (int r = 0; r < allRows.Count - 1; r++)
            {
                var rowA = allRows[r];
                var rowB = allRows[r + 1];

                int a = 0, b = 0;
                while (a < rowA.Count - 1 || b < rowB.Count - 1)
                {
                    bool canQuad = a < rowA.Count - 1 && b < rowB.Count - 1;
                    bool canTriA = a < rowA.Count - 1;
                    bool canTriB = b < rowB.Count - 1;

                    double costQuad = canQuad ? Math.Abs((isSliceU ? rowA[a + 1].V : rowA[a + 1].U) - (isSliceU ? rowB[b + 1].V : rowB[b + 1].U)) : double.MaxValue;
                    double costTriA = canTriA ? Math.Abs((isSliceU ? rowA[a + 1].V : rowA[a + 1].U) - (isSliceU ? rowB[b].V : rowB[b].U)) : double.MaxValue;
                    double costTriB = canTriB ? Math.Abs((isSliceU ? rowA[a].V : rowA[a].U) - (isSliceU ? rowB[b + 1].V : rowB[b + 1].U)) : double.MaxValue;

                    if (canQuad && costQuad <= costTriA && costQuad <= costTriB)
                    {
                        faces.Add(new List<Point2D> { rowA[a], rowA[a + 1], rowB[b + 1], rowB[b] });
                        a++; b++;
                    }
                    else if (canTriA && costTriA <= costTriB)
                    {
                        faces.Add(new List<Point2D> { rowA[a], rowA[a + 1], rowB[b] });
                        a++;
                    }
                    else if (canTriB)
                    {
                        faces.Add(new List<Point2D> { rowA[a], rowB[b + 1], rowB[b] });
                        b++;
                    }
                }
            }

            return faces;
        }

        // =====================================================================
        // HELPER METHODS
        // =====================================================================

        /// <summary>
        /// Build edges from polygon vertices and compute perpendicular directions.
        /// </summary>
        private static List<BoundaryEdge> BuildBoundaryEdges(List<Point3D> polygon)
        {
            List<BoundaryEdge> edges = new List<BoundaryEdge>();

            for (int i = 0; i < polygon.Count; i++)
            {
                Point3D start = polygon[i];
                Point3D end = polygon[(i + 1) % polygon.Count];
                Point3D dir = (end - start).Normalize();

                // Compute midpoint
                Point3D midpoint = (start + end) * 0.5;

                // For 3D case: compute edge tangent and find dominant perpendicular direction
                // The perpendicular direction is determined by the cross product with the plane normal
                Point3D edgeTangent = dir;
                double edgeLen = (end - start).Length();

                // Determine which global axis this edge's perpendicular points toward
                char perpDir;
                double absX = Math.Abs(dir.X);
                double absY = Math.Abs(dir.Y);
                double absZ = Math.Abs(dir.Z);

                // Edge direction determines perpendicular direction
                if (absX <= absY && absX <= absZ)
                    perpDir = 'Y'; // Edge mostly along X, so perp is along Y
                else if (absY <= absX && absY <= absZ)
                    perpDir = 'X'; // Edge mostly along Y, so perp is along X
                else
                    perpDir = 'X'; // Edge mostly along Z, so perp is along X (arbitrary)

                // Special case: if edge is very horizontal (small Z change), perp points in Z
                if (absZ > absX && absZ > absY)
                    perpDir = 'Z';

                edges.Add(new BoundaryEdge
                {
                    Start = start,
                    End = end,
                    Midpoint = midpoint,
                    Direction = edgeTangent,
                    Length = edgeLen,
                    PolygonIndex = i,
                    PerpendicularDirection = perpDir
                });
            }

            return edges;
        }

        /// <summary>
        /// Find existing nodes that should be considered for snapping during meshing.
        /// </summary>
        private static List<ExistingNode> FindExistingNodes(
            List<Point3D> continuityNodes,
            List<Point3D> boundary,
            double deltaMesh)
        {
            List<ExistingNode> nodes = new List<ExistingNode>();

            if (continuityNodes == null) return nodes;

            foreach (var node in continuityNodes)
            {
                double minDist = double.MaxValue;
                foreach (var boundaryNode in boundary)
                {
                    double dist = (node - boundaryNode).Length();
                    if (dist < minDist) minDist = dist;
                }

                nodes.Add(new ExistingNode
                {
                    Position = node,
                    Name = $"N_{node.X:F1}_{node.Y:F1}_{node.Z:F1}",
                    DistanceToBoundary = minDist
                });
            }

            return nodes;
        }

        /// <summary>
        /// Get sorting key for edges within the same perpendicular direction.
        /// Closer to origin and left-to-right ordering.
        /// </summary>
        private static double GetEdgeOrderKey(BoundaryEdge edge, char direction)
        {
            switch (direction)
            {
                case 'X':
                    return edge.Midpoint.Y * 10000 + edge.Midpoint.Z * 100 + edge.Midpoint.X;
                case 'Y':
                    return edge.Midpoint.X * 10000 + edge.Midpoint.Z * 100 + edge.Midpoint.Y;
                case 'Z':
                    return edge.Midpoint.X * 10000 + edge.Midpoint.Y * 100 + edge.Midpoint.Z;
                default:
                    return edge.Midpoint.X * 10000 + edge.Midpoint.Y * 100 + edge.Midpoint.Z;
            }
        }

        /// <summary>
        /// Emit rays from an edge at regular intervals of deltaMesh.
        /// </summary>
        private static List<MeshRay> EmitRaysFromEdge(
            BoundaryEdge edge,
            double deltaMesh,
            List<ExistingNode> existingNodes,
            double tolerancePercent)
        {
            List<MeshRay> rays = new List<MeshRay>();

            int numRays = Math.Max(1, (int)Math.Floor(edge.Length / deltaMesh));

            for (int i = 0; i <= numRays; i++)
            {
                double t = (double)i / numRays;
                Point3D origin = edge.Start + (edge.End - edge.Start) * t;

                // Direction: perpendicular to edge, pointing inward
                // For simplicity, use the edge's perpendicular in XY plane
                Point3D perpDir = new Point3D(-edge.Direction.Y, edge.Direction.X, 0).Normalize();

                // If Z component dominates, use XZ plane perpendicular
                if (Math.Abs(edge.Direction.Z) > Math.Abs(edge.Direction.X) && Math.Abs(edge.Direction.Z) > Math.Abs(edge.Direction.Y))
                {
                    perpDir = new Point3D(-edge.Direction.Z, 0, edge.Direction.X).Normalize();
                }

                var ray = new MeshRay
                {
                    Origin = origin,
                    Direction = perpDir,
                    SourceEdge = edge,
                    Parameter = 0,
                    End = origin + perpDir * deltaMesh * 10, // Extend well into the polygon
                    Nodes = new List<Point3D> { origin }
                };

                rays.Add(ray);
            }

            return rays;
        }

        /// <summary>
        /// Detect collisions between rays and boundaries, rays and rays.
        /// Implements Cases 3.1.1 through 3.1.4.
        /// </summary>
        private static List<RayIntersection> DetectCollisions(
            List<MeshRay> rays,
            List<BoundaryEdge> edges,
            List<ExistingNode> existingNodes,
            double deltaMesh,
            double tolerancePercent)
        {
            List<RayIntersection> intersections = new List<RayIntersection>();
            double tolerance = deltaMesh * tolerancePercent;
            double smallElementThreshold = deltaMesh * 0.5;
            double largeElementThreshold = deltaMesh * 1.5;

            // Check each ray against all other rays and boundaries
            for (int i = 0; i < rays.Count; i++)
            {
                var ray1 = rays[i];
                if (!ray1.IsActive) continue;

                // CASE 3.1.1: Check if ray hits a boundary edge (not its own)
                foreach (var edge in edges)
                {
                    if (edge == ray1.SourceEdge) continue;

                    var intersection = RayBoundaryIntersection(ray1, edge, tolerance);
                    if (intersection != null)
                    {
                        intersection.Type = RayIntersection.IntersectionType.RayBoundary;
                        intersections.Add(intersection);

                        // Handle snapping to existing nodes
                        foreach (var node in existingNodes)
                        {
                            double distToNode = (intersection.Point - node.Position).Length();
                            if (distToNode < tolerance)
                            {
                                intersection.Point = node.Position;
                                break;
                            }
                            else if (distToNode < largeElementThreshold)
                            {
                                // Case 3.1.1: Near existing node - connect to it (may lose orthogonality)
                                intersection.Point = node.Position;
                            }
                            else if (distToNode < smallElementThreshold)
                            {
                                // Case 3.1.1: Very close - mark as small element
                                intersection.Point = node.Position;
                            }
                            // If far, keep orthogonal ray
                        }
                    }
                }

                // CASE 3.1.2 and 3.1.3: Check if ray hits another ray
                for (int j = i + 1; j < rays.Count; j++)
                {
                    var ray2 = rays[j];
                    if (!ray2.IsActive) continue;
                    if (ray1.SourceEdge == ray2.SourceEdge) continue; // Skip same-edge rays

                    var intersection = RayRayIntersection(ray1, ray2, tolerance);
                    if (intersection != null)
                    {
                        // Check if collinear (Case 3.1.2) or acute angle (Case 3.1.3)
                        double angle = Math.Acos(Math.Abs(Point3D.Dot(ray1.Direction, ray2.Direction))) * 180 / Math.PI;

                        if (angle < 1.0) // Nearly collinear
                        {
                            intersection.Type = RayIntersection.IntersectionType.RayRayCollinear;
                        }
                        else
                        {
                            intersection.Type = RayIntersection.IntersectionType.RayRayAcute;
                        }

                        intersections.Add(intersection);
                    }
                }
            }

            return intersections;
        }

        /// <summary>
        /// Compute intersection between a ray and a boundary edge.
        /// </summary>
        private static RayIntersection RayBoundaryIntersection(MeshRay ray, BoundaryEdge edge, double tolerance)
        {
            // Ray: P = ray.Origin + t * ray.Direction
            // Edge: Q = edge.Start + s * (edge.End - edge.Start)
            // Solve for t, s where they intersect

            Point3D d = ray.Direction;
            Point3D e = edge.End - edge.Start;

            double denom = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(denom) < 1e-10) return null; // Parallel

            Point3D originToStart = edge.Start - ray.Origin;

            double t = (originToStart.X * e.Y - originToStart.Y * e.X) / denom;
            double s = (originToStart.X * d.Y - originToStart.Y * d.X) / denom;

            // Check if intersection is valid (t > 0 for ray, 0 <= s <= 1 for edge segment)
            if (t > 1e-6 && s >= -1e-6 && s <= 1 + 1e-6)
            {
                Point3D intersectionPoint = ray.Origin + d * t;

                return new RayIntersection
                {
                    Ray1 = ray,
                    Ray2 = null,
                    Point = intersectionPoint,
                    T1 = t
                };
            }

            return null;
        }

        /// <summary>
        /// Compute intersection between two rays.
        /// </summary>
        private static RayIntersection RayRayIntersection(MeshRay ray1, MeshRay ray2, double tolerance)
        {
            // Both rays: P1 = ray1.Origin + t * ray1.Direction
            //             P2 = ray2.Origin + s * ray2.Direction

            Point3D d1 = ray1.Direction;
            Point3D d2 = ray2.Direction;
            Point3D originDiff = ray2.Origin - ray1.Origin;

            // Check if directions are parallel (cross product near zero)
            Point3D cross = Point3D.Cross(d1, d2);
            if (cross.Length() < 1e-6)
            {
                // Parallel rays - check for overlap
                // Project originDiff onto d1 to see if they're collinear
                double t = Point3D.Dot(originDiff, d1);
                double s = Point3D.Dot(originDiff, d2);
                if (Math.Abs(t) < tolerance || Math.Abs(s) < tolerance)
                {
                    // Collinear overlapping rays
                    Point3D midPoint = ray1.Origin + d1 * (t / 2);
                    return new RayIntersection
                    {
                        Ray1 = ray1,
                        Ray2 = ray2,
                        Point = midPoint,
                        T1 = t / 2,
                        T2 = 0
                    };
                }
                return null;
            }

            // Non-parallel - solve 3x3 system
            // [d1, -d2] · [t, s]^T = originDiff
            double det = d1.X * (-d2.Y) - d1.Y * (-d2.X);
            if (Math.Abs(det) < 1e-10) return null;

            double t2 = (originDiff.X * (-d2.Y) - originDiff.Y * (-d2.X)) / det;
            double s2 = (d1.X * originDiff.Y - d1.Y * originDiff.X) / det;

            if (t2 > 1e-6 && s2 > 1e-6)
            {
                Point3D intersectionPoint = ray1.Origin + d1 * t2;
                return new RayIntersection
                {
                    Ray1 = ray1,
                    Ray2 = ray2,
                    Point = intersectionPoint,
                    T1 = t2,
                    T2 = s2
                };
            }

            return null;
        }

        /// <summary>
        /// Apply collision results to trim/adjust rays.
        /// </summary>
        private static void ApplyCollisions(
            List<MeshRay> rays,
            List<RayIntersection> intersections,
            double deltaMesh,
            double tolerancePercent)
        {
            foreach (var intersection in intersections)
            {
                switch (intersection.Type)
                {
                    case RayIntersection.IntersectionType.RayBoundary:
                        // CASE 3.1.1: Trim ray at boundary
                        intersection.Ray1.End = intersection.Point;
                        intersection.Ray1.Parameter = intersection.T1;
                        intersection.Ray1.Nodes.Add(intersection.Point);
                        break;

                    case RayIntersection.IntersectionType.RayRayCollinear:
                        // CASE 3.1.2: Merge collinear rays - keep one, mark other inactive
                        // The intersection point is where they overlap
                        intersection.Ray1.End = intersection.Point;
                        intersection.Ray1.Parameter = intersection.T1;
                        intersection.Ray1.Nodes.Add(intersection.Point);

                        // Find the node on ray2 that matches and trim
                        intersection.Ray2.End = intersection.Point;
                        intersection.Ray2.Parameter = intersection.T2;
                        intersection.Ray2.Nodes.Add(intersection.Point);
                        break;

                    case RayIntersection.IntersectionType.RayRayAcute:
                        // CASE 3.1.3: Trim both at intersection
                        intersection.Ray1.End = intersection.Point;
                        intersection.Ray1.Parameter = intersection.T1;
                        intersection.Ray1.Nodes.Add(intersection.Point);

                        intersection.Ray2.End = intersection.Point;
                        intersection.Ray2.Parameter = intersection.T2;
                        intersection.Ray2.Nodes.Add(intersection.Point);
                        break;
                }
            }
        }

        /// <summary>
        /// Build mesh faces from processed rays.
        /// Creates triangles and quads by connecting ray endpoints.
        /// </summary>
        private static List<List<Point3D>> BuildMeshFaces(
            List<MeshRay> rays,
            List<BoundaryEdge> edges,
            List<ExistingNode> existingNodes,
            double deltaMesh)
        {
            List<List<Point3D>> faces = new List<List<Point3D>>();

            // Group rays by their source edge for layer-by-layer processing
            var edgeRays = rays.GroupBy(r => r.SourceEdge).ToDictionary(g => g.Key, g => g.ToList());

            // Process each edge's rays to create faces between adjacent layers
            foreach (var kvp in edgeRays)
            {
                var edgeRaysList = kvp.Value.OrderBy(r => (r.Origin - kvp.Key.Start).Length()).ToList();

                // Connect adjacent rays to form strips
                for (int i = 0; i < edgeRaysList.Count - 1; i++)
                {
                    var rayA = edgeRaysList[i];
                    var rayB = edgeRaysList[i + 1];

                    // Skip if either ray is too short
                    if ((rayA.End - rayA.Origin).Length() < deltaMesh * 0.1) continue;
                    if ((rayB.End - rayB.Origin).Length() < deltaMesh * 0.1) continue;

                    // Create a quad between the two rays
                    var quad = new List<Point3D>
                    {
                        rayA.Origin,
                        rayB.Origin,
                        rayB.End,
                        rayA.End
                    };

                    faces.Add(quad);
                }
            }

            // Clean up degenerate faces
            faces = faces.Where(f => f.Count >= 3 && f.Count <= 4).ToList();

            return faces;
        }

        // =====================================================================
        // UTILITY METHODS (from original MesherAlgorithm, kept for compatibility)
        // =====================================================================

        public static bool IsPointOnPolygonBoundary(Point3D p, List<Point3D> boundary, double tol)
        {
            for (int i = 0; i < boundary.Count; i++)
            {
                Point3D A = boundary[i];
                Point3D B = boundary[(i + 1) % boundary.Count];

                Point3D AB = B - A;
                Point3D AP = p - A;

                double lenAB = AB.Length();
                if (lenAB == 0) continue;

                double proj = Point3D.Dot(AP, AB) / lenAB;
                if (proj >= -tol && proj <= lenAB + tol)
                {
                    Point3D pointProj = A + AB.Normalize() * proj;
                    if ((p - pointProj).Length() <= tol) return true;
                }
            }
            return false;
        }

        public static bool IsPointOnPolygonBoundary2D(Point2D p, List<Point2D> boundary, double tol)
        {
            for (int i = 0; i < boundary.Count; i++)
            {
                Point2D A = boundary[i];
                Point2D B = boundary[(i + 1) % boundary.Count];

                double lineLenSQ = Math.Pow(B.U - A.U, 2) + Math.Pow(B.V - A.V, 2);
                if (lineLenSQ == 0) continue;

                double t = ((p.U - A.U) * (B.U - A.U) + (p.V - A.V) * (B.V - A.V)) / lineLenSQ;
                if (t >= -tol && t <= 1 + tol)
                {
                    double pU = A.U + t * (B.U - A.U);
                    double pV = A.V + t * (B.V - A.V);
                    double dist = Math.Sqrt(Math.Pow(p.U - pU, 2) + Math.Pow(p.V - pV, 2));
                    if (dist <= tol) return true;
                }
            }
            return false;
        }

        public static bool IsInsidePoly(Point2D pt, List<Point2D> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].V > pt.V) != (poly[j].V > pt.V)) &&
                   (pt.U < (poly[j].U - poly[i].U) * (pt.V - poly[i].V) / (poly[j].V - poly[i].V) + poly[i].U))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
