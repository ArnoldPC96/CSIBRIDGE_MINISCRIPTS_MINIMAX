using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using CSiAPIv1;

namespace MeshGui
{
    public class CapturedPoint
    {
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public partial class MainWindow : Window
    {
        private cOAPI mySapObject = null;
        private cSapModel mySapModel = null;
        
        private ObservableCollection<CapturedPoint> capturedPoints = new ObservableCollection<CapturedPoint>();
        private string activeCaptureMode = "NONE";
        private string activeSourceAreaName = "";

        public MainWindow()
        {
            InitializeComponent();
            DgPoints.ItemsSource = capturedPoints;
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mySapObject = (cOAPI)Marshal.GetActiveObject("CSI.CSiBridge.API.SapObject");
                mySapModel = mySapObject.SapModel;
                
                string ver = mySapObject.GetOAPIVersionNumber().ToString();
                
                string modelPath = "";
                modelPath = mySapModel.GetModelFilename(true); // Obteniendo el nombre del archivo
                
                if (string.IsNullOrEmpty(modelPath)) modelPath = mySapModel.GetModelFilepath(); // Fallback

                string modelName = string.IsNullOrEmpty(modelPath) ? "Sin Guardar (Untitled)" : System.IO.Path.GetFileName(modelPath);

                // Imprimir nombre de archivo en lugar de "Modelo:" a secas
                TxtLog.Text = $"Conectado. CSIBridge OAPI: {ver} | BDB: {modelName}";
                
                BtnCaptureArea.IsEnabled = true;
                BtnCapturePoints.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Abre CSiBridge primero.\nError: " + ex.Message, "Error COM", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtLog.Text = "Fallo de conexión.";
            }
        }

        private void ClearCapture()
        {
            capturedPoints.Clear();
            activeSourceAreaName = "";
            activeCaptureMode = "NONE";
            TxtCaptureMode.Text = "Modo: Ninguno";
            BtnRun.IsEnabled = false;
            BtnDeleteInside.IsEnabled = false;
        }

        private void BtnCaptureArea_Click(object sender, RoutedEventArgs e)
        {
            ClearCapture();
            
            try
            {
                int ObjCount = 0;
                int[] ObjTypes = null;
                string[] ObjNames = null;
                mySapModel.SelectObj.GetSelected(ref ObjCount, ref ObjTypes, ref ObjNames);

                string aName = "";
                for (int i = 0; i < ObjCount; i++)
                {
                    if (ObjTypes[i] == 5) { aName = ObjNames[i]; break; }
                }

                if (string.IsNullOrEmpty(aName))
                {
                    MessageBox.Show("Debes seleccionar al menos un Objeto Área en CSiBridge.", "Captura Vacía", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int numPoints = 0;
                string[] ptNames = null;
                mySapModel.AreaObj.GetPoints(aName, ref numPoints, ref ptNames);

                for (int i = 0; i < numPoints; i++)
                {
                    double x = 0, y = 0, z = 0;
                    mySapModel.PointObj.GetCoordCartesian(ptNames[i], ref x, ref y, ref z);
                    capturedPoints.Add(new CapturedPoint { Name = ptNames[i] ?? $"P{i+1}", X = x, Y = y, Z = z });
                }

                activeSourceAreaName = aName;
                activeCaptureMode = "AREA";
                TxtCaptureMode.Text = $"Modo: Área '{aName}' ({numPoints} ptos)";
                BtnRun.IsEnabled = true;
                BtnDeleteInside.IsEnabled = true;
            }
            catch(Exception ex)
            {
                MessageBox.Show("Error capturando Área: " + ex.Message);
            }
        }

        private void BtnCapturePoints_Click(object sender, RoutedEventArgs e)
        {
            ClearCapture();

            try
            {
                int ObjCount = 0;
                int[] ObjTypes = null;
                string[] ObjNames = null;
                mySapModel.SelectObj.GetSelected(ref ObjCount, ref ObjTypes, ref ObjNames);

                List<string> ptSel = new List<string>();
                for (int i = 0; i < ObjCount; i++)
                {
                    if (ObjTypes[i] == 1) { ptSel.Add(ObjNames[i]); }
                }

                if (ptSel.Count < 3 || ptSel.Count > 4)
                {
                    MessageBox.Show($"Seleccionaste {ptSel.Count} nudos. Deben ser 3 o 4 nudos exactamente.", "Captura Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var ptName in ptSel)
                {
                    double x = 0, y = 0, z = 0;
                    mySapModel.PointObj.GetCoordCartesian(ptName, ref x, ref y, ref z);
                    capturedPoints.Add(new CapturedPoint { Name = ptName, X = x, Y = y, Z = z });
                }

                activeCaptureMode = "POINTS";
                TxtCaptureMode.Text = $"Modo: Puntos Libres ({ptSel.Count} ptos)";
                BtnRun.IsEnabled = true;
                BtnDeleteInside.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error capturando Puntos: " + ex.Message);
            }
        }

        private void BtnDeleteInside_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (capturedPoints.Count < 3) return;
                List<MesherAlgorithm.Point3D> boundaryNodes = new List<MesherAlgorithm.Point3D>();
                foreach (var pt in capturedPoints)
                    boundaryNodes.Add(new MesherAlgorithm.Point3D(pt.X, pt.Y, pt.Z));

                int deleted = DeleteMatchingAreas(boundaryNodes);
                MessageBox.Show($"Se eliminaron {deleted} elementos de área contenidos dentro del polígono seleccionado.", "Limpieza Completada", MessageBoxButton.OK, MessageBoxImage.Information);
                mySapModel.View.RefreshWindow(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error eliminando áreas internas: " + ex.Message);
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (capturedPoints.Count < 3) return;

                double dx = double.Parse(TxtDx.Text);
                double dy = double.Parse(TxtDy.Text);
                double tolPercent = double.Parse(TxtTol.Text) / 100.0;
                
                bool isHorizontalPriority = RbHoriz.IsChecked == true;
                bool isAntiClockwise = RbAntiHorario.IsChecked == true;
                int discontinuityHandling = RbDefault.IsChecked == true ? 0 : (RbNoHorizon.IsChecked == true ? 1 : (RbLocalSnowplow.IsChecked == true ? 2 : 3));

                List<MesherAlgorithm.Point3D> boundaryNodes = new List<MesherAlgorithm.Point3D>();
                foreach (var pt in capturedPoints)
                    boundaryNodes.Add(new MesherAlgorithm.Point3D(pt.X, pt.Y, pt.Z));

                string areaProperty = "Default";
                if (activeCaptureMode == "AREA" && !string.IsNullOrEmpty(activeSourceAreaName))
                {
                    mySapModel.AreaObj.GetProperty(activeSourceAreaName, ref areaProperty);
                }

                List<MesherAlgorithm.Point3D> continuityNodes = GetGlobalContinuityNodes(boundaryNodes, Math.Min(dx, dy) * 0.05);

                mySapModel.SetModelIsLocked(false);

                int deletedInternals = DeleteMatchingAreas(boundaryNodes);

                // Ejecutamos Ray-Slicing Boundary-Fitted Mesher
                var meshResult = MesherAlgorithm.GenerateSlicerMesh(boundaryNodes, continuityNodes, dx, dy, tolPercent, isHorizontalPriority, isAntiClockwise, discontinuityHandling);

                int successCt = 0;
                foreach (var face in meshResult.Faces)
                {
                    int fn = face.Count;
                    if (fn >= 3 && fn <= 4)
                    {
                        double[] x = new double[fn];
                        double[] y = new double[fn];
                        double[] z = new double[fn];
                        for (int i = 0; i < fn; i++)
                        {
                            x[i] = face[i].X; y[i] = face[i].Y; z[i] = face[i].Z;
                        }

                        string newName = "";
                        int ret = mySapModel.AreaObj.AddByCoord(fn, ref x, ref y, ref z, ref newName, areaProperty);
                        if (ret == 0 && !string.IsNullOrEmpty(newName))
                            successCt++;
                    }
                }

                // Para 2.2: Subdividir elementos adyacentes
                if (discontinuityHandling == 2 && meshResult.OrphanEdges.Count > 0)
                {
                    int adjSubdivided = SubdivideAdjacentAreas(boundaryNodes, meshResult.OrphanEdges, areaProperty);
                    successCt += adjSubdivided;
                }

                // Borramos area original si se generaron bien
                if (successCt > 0)
                {
                    if (activeCaptureMode == "AREA" && !string.IsNullOrEmpty(activeSourceAreaName))
                        mySapModel.AreaObj.Delete(activeSourceAreaName);
                }

                mySapModel.View.RefreshWindow(0);
                TxtLog.Text += $"\nMalla Inyectada: {successCt} Trapecios/Triángulos por barrido.";
                MessageBox.Show($"Malla generada con éxito ({successCt} elementos).", "Terminado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error en proceso: {ex.Message}");
                TxtLog.Text = "Error Ejecución: " + ex.Message;
            }
        }

        private List<MesherAlgorithm.Point3D> GetGlobalContinuityNodes(List<MesherAlgorithm.Point3D> boundary, double tolerance)
        {
            List<MesherAlgorithm.Point3D> cont = new List<MesherAlgorithm.Point3D>();
            try
            {
                int numPts = 0;
                string[] ptNames = null;
                mySapModel.PointObj.GetNameList(ref numPts, ref ptNames);
                if (numPts == 0) return cont;

                for (int i = 0; i < numPts; i++)
                {
                    double x = 0, y = 0, z = 0;
                    mySapModel.PointObj.GetCoordCartesian(ptNames[i], ref x, ref y, ref z);
                    MesherAlgorithm.Point3D p = new MesherAlgorithm.Point3D(x, y, z);

                    if (MesherAlgorithm.IsPointOnPolygonBoundary(p, boundary, tolerance))
                    {
                        cont.Add(p);
                    }
                }
            }
            catch { }
            return cont;
        }

        private int DeleteMatchingAreas(List<MesherAlgorithm.Point3D> boundaryNodes)
        {
            int deletedCount = 0;
            int numAreas = 0;
            string[] areaNames = null;
            mySapModel.AreaObj.GetNameList(ref numAreas, ref areaNames);
            if (numAreas == 0 || areaNames == null) return 0;

            // Definition of Local Plane to check IsInsidePoly for Centers
            MesherAlgorithm.Point3D O = boundaryNodes[0];
            MesherAlgorithm.Point3D vecU = (boundaryNodes[1] - boundaryNodes[0]).Normalize();
            MesherAlgorithm.Point3D tempV = (boundaryNodes[2] - boundaryNodes[0]).Normalize();
            MesherAlgorithm.Point3D normal = MesherAlgorithm.Point3D.Cross(vecU, tempV).Normalize();
            MesherAlgorithm.Point3D vecV = MesherAlgorithm.Point3D.Cross(normal, vecU).Normalize();

            List<MesherAlgorithm.Point2D> poly2D = new List<MesherAlgorithm.Point2D>();
            foreach (var p3 in boundaryNodes)
            {
                MesherAlgorithm.Point3D diff = p3 - O;
                poly2D.Add(new MesherAlgorithm.Point2D(MesherAlgorithm.Point3D.Dot(diff, vecU), MesherAlgorithm.Point3D.Dot(diff, vecV)));
            }

            double cx = 0; double cy = 0;
            foreach (var p2 in poly2D) { cx += p2.U; cy += p2.V; }
            cx /= poly2D.Count; cy /= poly2D.Count;

            poly2D.Sort((a, b) => Math.Atan2(a.V - cy, a.U - cx).CompareTo(Math.Atan2(b.V - cy, b.U - cx)));

            foreach (string aName in areaNames)
            {
                // Obviamos area capturada
                if (activeCaptureMode == "AREA" && aName == activeSourceAreaName) continue;

                int nPts = 0;
                string[] ptNames = null;
                mySapModel.AreaObj.GetPoints(aName, ref nPts, ref ptNames);

                bool isCompletelyInside = true;

                // Validate if ALL nodes of this area are internally bounded by our Polygon
                for (int i = 0; i < nPts; i++)
                {
                    double x = 0, y = 0, z = 0;
                    mySapModel.PointObj.GetCoordCartesian(ptNames[i], ref x, ref y, ref z);
                    
                    MesherAlgorithm.Point3D node3D = new MesherAlgorithm.Point3D(x, y, z);

                    // Recheck that it belongs to the same mathematical plane first!
                    double distToPlane = Math.Abs(MesherAlgorithm.Point3D.Dot(node3D - O, normal));
                    if (distToPlane > 0.05) { isCompletelyInside = false; break; }

                    MesherAlgorithm.Point3D diff = node3D - O;
                    MesherAlgorithm.Point2D p2D = new MesherAlgorithm.Point2D(MesherAlgorithm.Point3D.Dot(diff, vecU), MesherAlgorithm.Point3D.Dot(diff, vecV));
                    
                    if (!MesherAlgorithm.IsInsidePoly(p2D, poly2D) && !MesherAlgorithm.IsPointOnPolygonBoundary2D(p2D, poly2D, 0.05))
                    {
                        isCompletelyInside = false; break;
                    }
                }

                if (isCompletelyInside)
                {
                    mySapModel.AreaObj.Delete(aName);
                    deletedCount++;
                }
            }
            return deletedCount;
        }

        private int SubdivideAdjacentAreas(List<MesherAlgorithm.Point3D> boundaryNodes, List<MesherAlgorithm.OrphanEdgeInfo> orphanEdges, string areaProperty)
        {
            int subdividedCount = 0;

            MesherAlgorithm.Point3D O = boundaryNodes[0];
            MesherAlgorithm.Point3D vecU = (boundaryNodes[1] - boundaryNodes[0]).Normalize();
            MesherAlgorithm.Point3D tempV = (boundaryNodes[2] - boundaryNodes[0]).Normalize();
            MesherAlgorithm.Point3D normal = MesherAlgorithm.Point3D.Cross(vecU, tempV).Normalize();
            MesherAlgorithm.Point3D vecV = MesherAlgorithm.Point3D.Cross(normal, vecU).Normalize();

            foreach (var orphan in orphanEdges)
            {
                MesherAlgorithm.Point3D edgeStart3D = O + vecU * orphan.EdgeStart.U + vecV * orphan.EdgeStart.V;
                MesherAlgorithm.Point3D edgeEnd3D = O + vecU * orphan.EdgeEnd.U + vecV * orphan.EdgeEnd.V;
                MesherAlgorithm.Point3D missingNode3D = O + vecU * orphan.MissingNode.U + vecV * orphan.MissingNode.V;

                int numAreas = 0;
                string[] areaNames = null;
                mySapModel.AreaObj.GetNameList(ref numAreas, ref areaNames);
                if (numAreas == 0 || areaNames == null) continue;

                string adjacentAreaName = null;
                List<MesherAlgorithm.Point3D> adjacentNodes = null;

                foreach (string aName in areaNames)
                {
                    if (activeCaptureMode == "AREA" && aName == activeSourceAreaName) continue;

                    int nPts = 0;
                    string[] ptNames = null;
                    mySapModel.AreaObj.GetPoints(aName, ref nPts, ref ptNames);
                    if (nPts < 3) continue;

                    List<MesherAlgorithm.Point3D> areaNodes = new List<MesherAlgorithm.Point3D>();
                    for (int i = 0; i < nPts; i++)
                    {
                        double x = 0, y = 0, z = 0;
                        mySapModel.PointObj.GetCoordCartesian(ptNames[i], ref x, ref y, ref z);
                        areaNodes.Add(new MesherAlgorithm.Point3D(x, y, z));
                    }

                    bool sharesEdge = false;
                    for (int i = 0; i < areaNodes.Count; i++)
                    {
                        MesherAlgorithm.Point3D a1 = areaNodes[i];
                        MesherAlgorithm.Point3D a2 = areaNodes[(i + 1) % areaNodes.Count];

                        double dist1 = (a1 - edgeEnd3D).Length();
                        double dist2 = (a2 - edgeEnd3D).Length();
                        if (dist1 < 0.1 && dist2 < 0.1)
                        {
                            sharesEdge = true;
                            break;
                        }
                    }

                    if (sharesEdge)
                    {
                        adjacentAreaName = aName;
                        adjacentNodes = areaNodes;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(adjacentAreaName) && adjacentNodes != null)
                {
                    mySapModel.AreaObj.Delete(adjacentAreaName);
                    subdividedCount++;

                    MesherAlgorithm.Point3D n1 = adjacentNodes[0];
                    MesherAlgorithm.Point3D n2 = adjacentNodes[1];
                    MesherAlgorithm.Point3D n3 = adjacentNodes[2];
                    MesherAlgorithm.Point3D n4 = adjacentNodes.Count > 3 ? adjacentNodes[3] : n3;

                    double[] x = new double[] { n1.X, n2.X, n3.X };
                    double[] y = new double[] { n1.Y, n2.Y, n3.Y };
                    double[] z = new double[] { n1.Z, n2.Z, n3.Z };
                    string newName1 = "";
                    int ret1 = mySapModel.AreaObj.AddByCoord(3, ref x, ref y, ref z, ref newName1, areaProperty);
                    if (ret1 == 0 && !string.IsNullOrEmpty(newName1)) subdividedCount++;

                    if (adjacentNodes.Count > 3)
                    {
                        x = new double[] { n1.X, n3.X, n4.X };
                        y = new double[] { n1.Y, n3.Y, n4.Y };
                        z = new double[] { n1.Z, n3.Z, n4.Z };
                        string newName2 = "";
                        int ret2 = mySapModel.AreaObj.AddByCoord(3, ref x, ref y, ref z, ref newName2, areaProperty);
                        if (ret2 == 0 && !string.IsNullOrEmpty(newName2)) subdividedCount++;
                    }
                    else
                    {
                        x = new double[] { n1.X, n3.X, edgeEnd3D.X };
                        y = new double[] { n1.Y, n3.Y, edgeEnd3D.Y };
                        z = new double[] { n1.Z, n3.Z, edgeEnd3D.Z };
                        string newName2 = "";
                        int ret2 = mySapModel.AreaObj.AddByCoord(3, ref x, ref y, ref z, ref newName2, areaProperty);
                        if (ret2 == 0 && !string.IsNullOrEmpty(newName2)) subdividedCount++;
                    }
                }
            }

            return subdividedCount;
        }
    }
}
