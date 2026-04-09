using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using CSiAPIv1;

namespace MeshGuiGeneral
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
                
                string modelPath = mySapModel.GetModelFilename(true);
                if (string.IsNullOrEmpty(modelPath)) modelPath = mySapModel.GetModelFilepath();

                string modelName = string.IsNullOrEmpty(modelPath) ? "Sin Guardar" : System.IO.Path.GetFileName(modelPath);

                TxtLog.Text = $"Conectado. CSiBridge OAPI: {ver} | BDB: {modelName}";
                
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
                List<GeneralMesher.Point3D> boundaryNodes = new List<GeneralMesher.Point3D>();
                foreach (var pt in capturedPoints)
                    boundaryNodes.Add(new GeneralMesher.Point3D(pt.X, pt.Y, pt.Z));

                int deleted = DeleteMatchingAreas(boundaryNodes);
                MessageBox.Show($"Se eliminaron {deleted} elementos de área.", "Limpieza Completada", MessageBoxButton.OK, MessageBoxImage.Information);
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

                double delta = double.Parse(TxtDelta.Text);
                double tolPercent = double.Parse(TxtTol.Text) / 100.0;

                bool orderXYZ = RbXYZ.IsChecked == true;
                bool orderYXZ = RbYXZ.IsChecked == true;
                bool orderZXY = RbZXY.IsChecked == true;

                bool usePerpendicular = RbPerpendicular.IsChecked == true;
                bool useRaySlicing = RbRaySlicing.IsChecked == true;

                List<GeneralMesher.Point3D> boundaryNodes = new List<GeneralMesher.Point3D>();
                foreach (var pt in capturedPoints)
                    boundaryNodes.Add(new GeneralMesher.Point3D(pt.X, pt.Y, pt.Z));

                string areaProperty = "Default";
                if (activeCaptureMode == "AREA" && !string.IsNullOrEmpty(activeSourceAreaName))
                {
                    mySapModel.AreaObj.GetProperty(activeSourceAreaName, ref areaProperty);
                }

                mySapModel.SetModelIsLocked(false);

                int deletedInternals = DeleteMatchingAreas(boundaryNodes);

                List<GeneralMesher.Point3D> continuityNodes = GetGlobalContinuityNodes(boundaryNodes, delta * 0.05);

                int successCt = 0;

                if (usePerpendicular)
                {
                    // New perpendicular-to-edges meshing algorithm
                    var meshResult = GeneralMesher.GeneratePerpendicularMesh(
                        boundaryNodes, continuityNodes, delta, tolPercent, orderXYZ, orderYXZ, orderZXY);

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
                }
                else
                {
                    // Legacy ray-slicing (reuse existing logic)
                    var meshResult = GeneralMesher.GenerateSlicerMesh(
                        boundaryNodes, continuityNodes, delta, delta, tolPercent, true, true, 0);

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
                }

                if (successCt > 0)
                {
                    if (activeCaptureMode == "AREA" && !string.IsNullOrEmpty(activeSourceAreaName))
                        mySapModel.AreaObj.Delete(activeSourceAreaName);
                }

                mySapModel.View.RefreshWindow(0);
                TxtLog.Text += $"\nMalla Generada: {successCt} elementos. (Borrados: {deletedInternals})";
                MessageBox.Show($"Malla generada con éxito ({successCt} elementos).", "Terminado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error en proceso: {ex.Message}\n{ex.StackTrace}");
                TxtLog.Text = "Error: " + ex.Message;
            }
        }

        private List<GeneralMesher.Point3D> GetGlobalContinuityNodes(List<GeneralMesher.Point3D> boundary, double tolerance)
        {
            List<GeneralMesher.Point3D> cont = new List<GeneralMesher.Point3D>();
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
                    GeneralMesher.Point3D p = new GeneralMesher.Point3D(x, y, z);

                    if (GeneralMesher.IsPointOnPolygonBoundary(p, boundary, tolerance))
                    {
                        cont.Add(p);
                    }
                }
            }
            catch { }
            return cont;
        }

        private int DeleteMatchingAreas(List<GeneralMesher.Point3D> boundaryNodes)
        {
            int deletedCount = 0;
            int numAreas = 0;
            string[] areaNames = null;
            mySapModel.AreaObj.GetNameList(ref numAreas, ref areaNames);
            if (numAreas == 0 || areaNames == null) return 0;

            GeneralMesher.Point3D O = boundaryNodes[0];
            GeneralMesher.Point3D vecU = (boundaryNodes[1] - boundaryNodes[0]).Normalize();
            GeneralMesher.Point3D tempV = (boundaryNodes[2] - boundaryNodes[0]).Normalize();
            GeneralMesher.Point3D normal = GeneralMesher.Point3D.Cross(vecU, tempV).Normalize();
            GeneralMesher.Point3D vecV = GeneralMesher.Point3D.Cross(normal, vecU).Normalize();

            List<GeneralMesher.Point2D> poly2D = new List<GeneralMesher.Point2D>();
            foreach (var p3 in boundaryNodes)
            {
                GeneralMesher.Point3D diff = p3 - O;
                poly2D.Add(new GeneralMesher.Point2D(GeneralMesher.Point3D.Dot(diff, vecU), GeneralMesher.Point3D.Dot(diff, vecV)));
            }

            double cx = 0; double cy = 0;
            foreach (var p2 in poly2D) { cx += p2.U; cy += p2.V; }
            cx /= poly2D.Count; cy /= poly2D.Count;

            poly2D.Sort((a, b) => Math.Atan2(a.V - cy, a.U - cx).CompareTo(Math.Atan2(b.V - cy, b.U - cx)));

            foreach (string aName in areaNames)
            {
                if (activeCaptureMode == "AREA" && aName == activeSourceAreaName) continue;

                int nPts = 0;
                string[] ptNames = null;
                mySapModel.AreaObj.GetPoints(aName, ref nPts, ref ptNames);

                bool isCompletelyInside = true;

                for (int i = 0; i < nPts; i++)
                {
                    double x = 0, y = 0, z = 0;
                    mySapModel.PointObj.GetCoordCartesian(ptNames[i], ref x, ref y, ref z);
                    
                    GeneralMesher.Point3D node3D = new GeneralMesher.Point3D(x, y, z);

                    double distToPlane = Math.Abs(GeneralMesher.Point3D.Dot(node3D - O, normal));
                    if (distToPlane > 0.05) { isCompletelyInside = false; break; }

                    GeneralMesher.Point3D diff = node3D - O;
                    GeneralMesher.Point2D p2D = new GeneralMesher.Point2D(GeneralMesher.Point3D.Dot(diff, vecU), GeneralMesher.Point3D.Dot(diff, vecV));
                    
                    if (!GeneralMesher.IsInsidePoly(p2D, poly2D) && !GeneralMesher.IsPointOnPolygonBoundary2D(p2D, poly2D, 0.05))
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
    }
}
