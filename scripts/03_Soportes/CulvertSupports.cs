using System;
using CSiAPIv1;
using CsiRunner;

namespace Scripts.Supports
{
    public class CulvertSupports : ICsiScript
    {
        public ScriptResult Run(cOAPI app, cSapModel model)
        {
            var result = new ScriptResult();
            try
            {
                int numPoints = 0;
                string[] pointNames = null;
                
                // Obtenemos todos los nodos
                model.PointObj.GetNameList(ref numPoints, ref pointNames);

                int countRestrained = 0;
                bool[] restraints = new bool[] { true, true, true, true, true, true }; // Empotrado total

                if (numPoints > 0 && pointNames != null)
                {
                    foreach (string name in pointNames)
                    {
                        double x = 0, y = 0, z = 0;
                        model.PointObj.GetCoordCartesian(name, ref x, ref y, ref z);
                        
                        // Restringimos los nodos en z = -0.075 (cota fundacion losa)
                        if (Math.Abs(z - (-0.075)) < 0.001)
                        {
                            model.PointObj.SetRestraint(name, ref restraints);
                            countRestrained++;
                        }
                    }
                }
                
                model.View.RefreshView(0, false);
                result.Success = true;
                result.Message = $"Fijados {countRestrained} nodos como apoyo fijo (Empotramiento) en el estrato base Z=-0.075.";
            }
            catch(Exception ex)
            {
                result.Success = false;
                result.Message = $"Error Supports: {ex.Message}";
            }
            
            return result;
        }
    }
}
