using System;
using CSiAPIv1;
using CsiRunner;

namespace Scripts
{
    public class HelloCsi : ICsiScript
    {
        public ScriptResult Run(cOAPI app, cSapModel model)
        {
            var result = new ScriptResult();
            try
            {
                // Obtener un dato basico de la API para probar que estamos conectados
                double oapiVersion = app.GetOAPIVersionNumber();

                result.Success = true;
                result.Message = $"Conexion Exitosa. OAPI Version: {oapiVersion}";
                
                result.Data["OAPIVersion"] = oapiVersion;
                if (model != null) result.Data["ModelStatus"] = "Loaded";
            }
            catch(Exception ex)
            {
                result.Success = false;
                result.Message = $"Error en HelloCsi: {ex.Message}";
            }
            
            return result;
        }
    }
}
