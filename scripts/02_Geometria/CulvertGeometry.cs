using System;
using System.Collections.Generic;
using CSiAPIv1;
using CsiRunner;

namespace Scripts.Geometry
{
    public class CulvertGeometry : ICsiScript
    {
        public ScriptResult Run(cOAPI app, cSapModel model)
        {
            var result = new ScriptResult();
            try
            {
                string areaName = "";
                List<string> createdAreas = new List<string>();

                // Geometria Parametrizada
                double xL = -0.70, xR = 0.70;
                double y0 = 0.0, y1 = 4.5;
                double zBot = -0.075, zTop = 1.10;

                // 1. Losa Fondo
                double[] xF = { xR, xL, xL, xR };
                double[] yF = { y0, y0, y1, y1 };
                double[] zF = { zBot, zBot, zBot, zBot };
                model.AreaObj.AddByCoord(4, ref xF, ref yF, ref zF, ref areaName, "Fondo_15");
                createdAreas.Add(areaName);

                // 2. Losa Techo
                double[] xT = { xR, xL, xL, xR };
                double[] yT = { y0, y0, y1, y1 };
                double[] zT = { zTop, zTop, zTop, zTop };
                model.AreaObj.AddByCoord(4, ref xT, ref yT, ref zT, ref areaName, "Losa_20");
                createdAreas.Add(areaName);

                // 3. Pared Izquierda (xL)
                double[] xW1 = { xL, xL, xL, xL };
                double[] yW1 = { y0, y1, y1, y0 };
                double[] zW1 = { zBot, zBot, zTop, zTop };
                model.AreaObj.AddByCoord(4, ref xW1, ref yW1, ref zW1, ref areaName, "Pared_20");
                createdAreas.Add(areaName);

                // 4. Pared Derecha (xR)
                double[] xW2 = { xR, xR, xR, xR };
                double[] yW2 = { y0, y1, y1, y0 };
                double[] zW2 = { zBot, zBot, zTop, zTop };
                model.AreaObj.AddByCoord(4, ref xW2, ref yW2, ref zW2, ref areaName, "Pared_20");
                createdAreas.Add(areaName);

                // 5. Aleros (Salida, y1 = 4.5)
                double dW = 0.8308;
                
                // Alero Izq Salida
                double[] xALL = { xL, xL, xL - dW };
                double[] yALL = { y1, y1, y1 + dW };
                double[] zALL = { zBot, zTop, zBot };
                model.AreaObj.AddByCoord(3, ref xALL, ref yALL, ref zALL, ref areaName, "Alero_20");
                createdAreas.Add(areaName);

                // Alero Der Salida
                double[] xALR = { xR, xR, xR + dW };
                double[] yALR = { y1, y1, y1 + dW };
                double[] zALR = { zBot, zTop, zBot };
                model.AreaObj.AddByCoord(3, ref xALR, ref yALR, ref zALR, ref areaName, "Alero_20");
                createdAreas.Add(areaName);

                // Losa Fondo Salida
                double[] xEL = { xR, xL, xL - dW, xR + dW };
                double[] yEL = { y1, y1, y1 + dW, y1 + dW };
                double[] zEL = { zBot, zBot, zBot, zBot };
                model.AreaObj.AddByCoord(4, ref xEL, ref yEL, ref zEL, ref areaName, "Fondo_15");
                createdAreas.Add(areaName);

                // 6. Aleros (Entrada, y0 = 0)
                // Alero Izq Entrada
                double[] xALL_in = { xL, xL, xL - dW };
                double[] yALL_in = { y0, y0, y0 - dW };
                double[] zALL_in = { zBot, zTop, zBot };
                model.AreaObj.AddByCoord(3, ref xALL_in, ref yALL_in, ref zALL_in, ref areaName, "Alero_20");
                createdAreas.Add(areaName);

                // Alero Der Entrada
                double[] xALR_in = { xR, xR, xR + dW };
                double[] yALR_in = { y0, y0, y0 - dW };
                double[] zALR_in = { zBot, zTop, zBot };
                model.AreaObj.AddByCoord(3, ref xALR_in, ref yALR_in, ref zALR_in, ref areaName, "Alero_20");
                createdAreas.Add(areaName);

                // Losa Fondo Entrada
                double[] xEL_in = { xR, xL, xL - dW, xR + dW };
                double[] yEL_in = { y0, y0, y0 - dW, y0 - dW };
                double[] zEL_in = { zBot, zBot, zBot, zBot };
                model.AreaObj.AddByCoord(4, ref xEL_in, ref yEL_in, ref zEL_in, ref areaName, "Fondo_15");
                createdAreas.Add(areaName);

                // ==========================
                // MALLADO FISICO EXPLICITO
                // ==========================
                // Usamos model.EditArea.Divide para obligar a que aparezcan los nudos fisicos.
                int cAreas = 0;
                foreach (string nName in createdAreas)
                {
                    int nAreas = 0;
                    string[] outNames = null;
                    // Int32 MeshType = 2 (General)
                    // MaxSize1 y MaxSize2 = 0.2 (m)
                    // Boolean parameters: true, true, false
                    model.EditArea.Divide(nName, 2, ref nAreas, ref outNames, 0, 0, 0.2, 0.2, true, true, false, false, 0.0, 0.2, false, false, false, false);
                    cAreas += nAreas;
                }

                model.View.RefreshView(0, false);

                result.Success = true;
                result.Message = $"Geometria y Aleros Creados Exitosamente. Mallado Completo en {cAreas} subd-shells de max 0.2m.";
            }
            catch(Exception ex)
            {
                result.Success = false;
                result.Message = $"Error Geometry: {ex.Message}";
            }
            
            return result;
        }
    }
}
