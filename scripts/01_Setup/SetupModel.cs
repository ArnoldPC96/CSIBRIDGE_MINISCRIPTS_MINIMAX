using System;
using CSiAPIv1;
using CsiRunner;

namespace Scripts.Setup
{
    public class SetupModel : ICsiScript
    {
        public ScriptResult Run(cOAPI app, cSapModel model)
        {
            var result = new ScriptResult();
            try
            {
                // 1. Initialize and set units to kN_m_C (enum value 6)
                model.InitializeNewModel(eUnits.kN_m_C);
                model.File.NewBlank();

                // 2. Define Material CONC280 (f'c = 28 MPa ~ 28000 kN/m2)
                string matName = "CONC280";
                
                // Agregamos material base de Concreto
                model.PropMaterial.SetMaterial(matName, eMatType.Concrete);
                
                // Modulo Elasticidad (E) = 25,000 MPa = 25,000,000 kN/m2. Poisson = 0.2
                model.PropMaterial.SetMPIsotropic(matName, 25000000.0, 0.2, 0.00001); 
                
                // Peso volumetrico = 25 kN/m3
                model.PropMaterial.SetWeightAndMass(matName, 1, 25.0); 

                // 3. Define Shell properties (Area Props)
                // 1 = Shell thin, Material = CONC280, Angle = 0, Thickness = 0.2, Bending Thickness = 0.2
                model.PropArea.SetShell("Pared_20", 1, matName, 0, 0.2, 0.2); 
                model.PropArea.SetShell("Losa_20", 1, matName, 0, 0.2, 0.2);
                model.PropArea.SetShell("Fondo_15", 1, matName, 0, 0.15, 0.15);
                model.PropArea.SetShell("Alero_20", 1, matName, 0, 0.2, 0.2);

                result.Success = true;
                result.Message = "Modelo inicializado en Blanco. Material CONC280 y Secciones (Shells) creados.";
            }
            catch(Exception ex)
            {
                result.Success = false;
                result.Message = $"Error SetupModel: {ex.Message}";
            }
            
            return result;
        }
    }
}
