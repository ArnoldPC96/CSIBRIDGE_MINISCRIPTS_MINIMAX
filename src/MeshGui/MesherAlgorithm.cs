using System;
using System.Collections.Generic;
using System.Linq;

namespace MeshGui
{
    public static class MesherAlgorithm
    {
        public class Point3D
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Point3D operator +(Point3D a, Point3D b) => new Point3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Point3D operator -(Point3D a, Point3D b) => new Point3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Point3D operator *(Point3D a, double d) => new Point3D(a.X * d, a.Y * d, a.Z * d);
            public static double Dot(Point3D a, Point3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static Point3D Cross(Point3D a, Point3D b) =>
                new Point3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
            public Point3D Normalize() { double l = Length(); return l > 0 ? new Point3D(X / l, Y / l, Z / l) : this; }
        }

        public class Point2D : IComparable<Point2D>
        {
            public double U { get; set; }
            public double V { get; set; }
            public Point2D(double u, double v) { U = u; V = v; }
            public int CompareTo(Point2D other) => U.CompareTo(other.U); // Por defecto ordena por U
        }

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


        public static List<List<Point3D>> GenerateSlicerMesh(List<Point3D> boundary, List<Point3D> continuityNodes, double dx, double dy, double snapTolPercent, bool isHorizontalPriority, bool isAntiClockwise, int discontinuityHandling = 0)
        {
            if (boundary.Count < 3) return new List<List<Point3D>>();

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

            List<Point2D> cont2D = new List<Point2D>();
            foreach (var p3 in continuityNodes)
            {
                Point3D diff = p3 - O;
                cont2D.Add(new Point2D(Point3D.Dot(diff, vecU), Point3D.Dot(diff, vecV)));
            }

            bool vertIsU = Math.Abs(vecU.Z) > Math.Abs(vecV.Z);
            bool isSliceU = isHorizontalPriority ? vertIsU : !vertIsU;

            double minSlice = isSliceU ? minU : minV;
            double maxSlice = isSliceU ? maxU : maxV;
            double minCrossGlobal = isSliceU ? minV : minU;
            double maxCrossGlobal = isSliceU ? maxV : maxU;

            double sliceSpacing = isHorizontalPriority ? dy : dx;
            double crossSpacing = isHorizontalPriority ? dx : dy;

            List<List<Point2D>> mappedFaces2D = DynamicZipper(poly2D, cont2D, crossSpacing, sliceSpacing, minSlice, maxSlice, isSliceU, discontinuityHandling);

            if (discontinuityHandling == 1)
            {
                RestoreSkippedBoundaries(mappedFaces2D, poly2D);
            }

            List<List<Point3D>> mesh3D = new List<List<Point3D>>();
            foreach (var face2D in mappedFaces2D)
            {
                List<Point3D> face3D = new List<Point3D>();
                foreach (var p2 in face2D)
                {
                    face3D.Add(O + (vecU * p2.U) + (vecV * p2.V));
                }

                if (isAntiClockwise)
                {
                    face3D.Reverse();
                }

                mesh3D.Add(face3D);
            }

            return mesh3D;
        }

        private static List<List<Point2D>> DynamicZipper(List<Point2D> poly, List<Point2D> cont, double crossSpacing, double sliceSpacing, double minS, double maxS, bool isSliceU, int discontinuityHandling)
        {
            List<double> rawSlices = new List<double>();
            foreach (var p in poly) rawSlices.Add(isSliceU ? p.U : p.V);
            foreach (var c in cont) rawSlices.Add(isSliceU ? c.U : c.V);
            
            rawSlices.Sort();
            List<double> hardSlices = new List<double>();
            foreach (var s in rawSlices) if (hardSlices.Count == 0 || Math.Abs(hardSlices.Last() - s) > 1e-4) hardSlices.Add(s);

            List<double> finalSlices = new List<double>();
            double sTol = sliceSpacing * 0.15;
            double sHeight = maxS - minS;
            int numSDivs = Math.Max(1, (int)Math.Round(sHeight / sliceSpacing));

            if (discontinuityHandling == 1)
            {
                finalSlices.Add(minS);
                finalSlices.Add(maxS);
                for (int i = 1; i < numSDivs; i++)
                {
                    finalSlices.Add(minS + i * (sHeight / numSDivs));
                }
            }
            else
            {
                finalSlices.AddRange(hardSlices);
                for (int i = 1; i < numSDivs; i++)
                {
                    double uS = minS + i * (sHeight / numSDivs);
                    if (!hardSlices.Exists(hs => Math.Abs(hs - uS) < sTol))
                    {
                        finalSlices.Add(uS);
                    }
                }
            }

            finalSlices.Sort();
            List<double> unqSlices = finalSlices;

            List<List<Point2D>> allRows = new List<List<Point2D>>();

            foreach (var sval in unqSlices)
            {
                List<double> ints = new List<double>();
                for (int i = 0; i < poly.Count; i++) {
                    Point2D A = poly[i]; Point2D B = poly[(i + 1) % poly.Count];
                    double aS = isSliceU ? A.U : A.V, bS = isSliceU ? B.U : B.V;
                    double aC = isSliceU ? A.V : A.U, bC = isSliceU ? B.V : B.U;

                    if (Math.Abs(aS - bS) < 1e-6) {
                        if (Math.Abs(aS - sval) < 1e-6) { ints.Add(aC); ints.Add(bC); }
                    } else if (sval >= Math.Min(aS, bS) - 1e-6 && sval <= Math.Max(aS, bS) + 1e-6) {
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
                for (int i = 0; i <= N; i++) {
                    double cVal = minC + i * (width / N);
                    rowPts.Add(isSliceU ? new Point2D(sval, cVal) : new Point2D(cVal, sval));
                }
                allRows.Add(rowPts);
            }

            List<List<Point2D>> faces = new List<List<Point2D>>();
            for (int r = 0; r < allRows.Count - 1; r++) {
                var rowA = allRows[r];
                var rowB = allRows[r + 1];
                
                int a = 0;
                int b = 0;
                while (a < rowA.Count - 1 || b < rowB.Count - 1)
                {
                    bool canQuad = (a < rowA.Count - 1 && b < rowB.Count - 1);
                    bool canTriA = (a < rowA.Count - 1);
                    bool canTriB = (b < rowB.Count - 1);

                    double costQuad = double.MaxValue;
                    double costTriA = double.MaxValue;
                    double costTriB = double.MaxValue;

                    double tA_quad = (double)(a + 1) / (rowA.Count - 1);
                    double tB_quad = (double)(b + 1) / (rowB.Count - 1);
                    double tA_triA = (double)(a + 1) / (rowA.Count - 1);
                    double tB_triA = (double)(b) / (rowB.Count - 1);
                    double tA_triB = (double)(a) / (rowA.Count - 1);
                    double tB_triB = (double)(b + 1) / (rowB.Count - 1);

                    double wA = Math.Abs((isSliceU ? rowA.Last().V : rowA.Last().U) - (isSliceU ? rowA.First().V : rowA.First().U));
                    double wB = Math.Abs((isSliceU ? rowB.Last().V : rowB.Last().U) - (isSliceU ? rowB.First().V : rowB.First().U));
                    double topoWeight = Math.Max(wA, wB);
                    if (topoWeight < 1e-4) topoWeight = crossSpacing * Math.Max(rowA.Count, rowB.Count);

                    if (canQuad) {
                        double cA = isSliceU ? rowA[a + 1].V : rowA[a + 1].U;
                        double cB = isSliceU ? rowB[b + 1].V : rowB[b + 1].U;
                        costQuad = Math.Abs(cA - cB) + topoWeight * Math.Abs(tA_quad - tB_quad) - 0.001;
                    }
                    if (canTriA) {
                        double cA = isSliceU ? rowA[a + 1].V : rowA[a + 1].U;
                        double cB = isSliceU ? rowB[b].V : rowB[b].U;
                        costTriA = Math.Abs(cA - cB) + topoWeight * Math.Abs(tA_triA - tB_triA);
                    }
                    if (canTriB) {
                        double cA = isSliceU ? rowA[a].V : rowA[a].U;
                        double cB = isSliceU ? rowB[b + 1].V : rowB[b + 1].U;
                        costTriB = Math.Abs(cA - cB) + topoWeight * Math.Abs(tA_triB - tB_triB);
                    }

                    if (canQuad && costQuad <= costTriA && costQuad <= costTriB) {
                        faces.Add(new List<Point2D> { rowA[a], rowA[a + 1], rowB[b + 1], rowB[b] });
                        a++; b++;
                    }
                    else if (canTriA && costTriA <= costTriB) {
                        faces.Add(new List<Point2D> { rowA[a], rowA[a + 1], rowB[b] });
                        a++;
                    }
                    else if (canTriB) {
                         faces.Add(new List<Point2D> { rowA[a], rowB[b + 1], rowB[b] });
                         b++;
                    }
                }
            }
            
            List<List<Point2D>> validFaces = new List<List<Point2D>>();
            foreach (var face in faces) {
                var unique = new List<Point2D>();
                foreach(var pt in face) {
                    bool dup = false;
                    foreach(var unq in unique) {
                        if (Math.Abs(pt.U - unq.U) < 1e-4 && Math.Abs(pt.V - unq.V) < 1e-4) dup = true;
                    }
                    if (!dup) unique.Add(pt);
                }
                if (unique.Count >= 3) {
                    if (!isSliceU) unique.Reverse(); // Para V7 el DynamicZipper avanza por N independientemente, control de ciclo CW/CCW
                    validFaces.Add(unique);
                }
            }

            return validFaces;
        }
        public static void RestoreSkippedBoundaries(List<List<Point2D>> mappedFaces2D, List<Point2D> poly2D)
        {
            Dictionary<string, int> edgeCounts = new Dictionary<string, int>();
            foreach (var face in mappedFaces2D) {
                for (int i=0; i<face.Count; i++) {
                    Point2D p1 = face[i];
                    Point2D p2 = face[(i+1)%face.Count];
                    string key1 = $"{Math.Round(p1.U, 4)}_{Math.Round(p1.V, 4)}|{Math.Round(p2.U, 4)}_{Math.Round(p2.V, 4)}";
                    string key2 = $"{Math.Round(p2.U, 4)}_{Math.Round(p2.V, 4)}|{Math.Round(p1.U, 4)}_{Math.Round(p1.V, 4)}";
                    if (edgeCounts.ContainsKey(key2)) edgeCounts[key2]++;
                    else if (edgeCounts.ContainsKey(key1)) edgeCounts[key1]++;
                    else edgeCounts[key1] = 1;
                }
            }

            List<List<Point2D>> newFaces = new List<List<Point2D>>();

            foreach (var face in mappedFaces2D) {
                for (int i=0; i<face.Count; i++) {
                    Point2D p1 = face[i];
                    Point2D p2 = face[(i+1)%face.Count];
                    string key1 = $"{Math.Round(p1.U, 4)}_{Math.Round(p1.V, 4)}|{Math.Round(p2.U, 4)}_{Math.Round(p2.V, 4)}";
                    string key2 = $"{Math.Round(p2.U, 4)}_{Math.Round(p2.V, 4)}|{Math.Round(p1.U, 4)}_{Math.Round(p1.V, 4)}";
                    
                    if ((edgeCounts.ContainsKey(key1) && edgeCounts[key1] == 1) || (edgeCounts.ContainsKey(key2) && edgeCounts[key2] == 1)) {
                        int e1 = GetPolyEdgeIndex(p1, poly2D);
                        int e2 = GetPolyEdgeIndex(p2, poly2D);

                        if (e1 != -1 && e2 != -1 && e1 != e2) {
                            int diff = (e2 - e1 + poly2D.Count) % poly2D.Count;
                            
                            // Valid cut-off corner with 1 missing node
                            if (diff == 1) {
                                Point2D missing = poly2D[(e1 + 1) % poly2D.Count];
                                newFaces.Add(new List<Point2D>() { p2, p1, missing });
                            }
                            // Valid cut-off corner with 2 missing nodes
                            else if (diff == 2) {
                                Point2D missing1 = poly2D[(e1 + 1) % poly2D.Count];
                                Point2D missing2 = poly2D[(e1 + 2) % poly2D.Count];
                                newFaces.Add(new List<Point2D>() { p2, p1, missing1, missing2 });
                            }
                            else if (diff > 2 && diff < poly2D.Count - 2) {
                                // Si cortó una figura extraña de más de 2 nudos, se generaría un polígono > 4 lados
                                // CSiBridge solo acepta tris o quads, así que se fracciona a la fuerza
                                Point2D missing1 = poly2D[(e1 + 1) % poly2D.Count];
                                newFaces.Add(new List<Point2D>() { p2, p1, missing1 });
                            }
                        }
                    }
                }
            }
            mappedFaces2D.AddRange(newFaces);
        }

        private static int GetPolyEdgeIndex(Point2D p, List<Point2D> poly)
        {
            for (int i = 0; i < poly.Count; i++) {
                if (IsPointOnSegment(p, poly[i], poly[(i + 1) % poly.Count])) return i;
            }
            return -1;
        }

        private static bool IsPointOnSegment(Point2D p, Point2D a, Point2D b)
        {
            double cross = (p.U - a.U) * (b.V - a.V) - (p.V - a.V) * (b.U - a.U);
            if (Math.Abs(cross) > 1e-3) return false;
            double dot = (p.U - a.U) * (b.U - a.U) + (p.V - a.V) * (b.V - a.V);
            if (dot < -1e-3) return false;
            double lenSq = (b.U - a.U) * (b.U - a.U) + (b.V - a.V) * (b.V - a.V);
            if (dot > lenSq + 1e-3) return false;
            return true;
        }
    }
}
