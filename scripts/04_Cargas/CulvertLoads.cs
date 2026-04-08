using System;
using CSiAPIv1;
using CsiRunner;

namespace Scripts.Loads
{
    public class CulvertLoads : ICsiScript
    {
        public ScriptResult Run(cOAPI app, cSapModel model)
        {
            var result = new ScriptResult();
            try
            {
                // 1. Crear Load Patterns
                // Limpiar todo antes o asumir si ya estan no pasa nada.
                // 1 = Dead, 8 = Other, 3 = Live, 5 = Quake
                model.LoadPatterns.Add("DC", (eLoadPatternType)1);
                model.LoadPatterns.Add("EH", (eLoadPatternType)8);
                model.LoadPatterns.Add("LL", (eLoadPatternType)3);
                model.LoadPatterns.Add("LS", (eLoadPatternType)8);
                model.LoadPatterns.Add("EQ", (eLoadPatternType)5);

                // 2. Identificar areas por sus coordenadas locales y aplicar cargas
                int numAreas = 0;
                string[] areaNames = null;
                model.AreaObj.GetNameList(ref numAreas, ref areaNames);

                int loadsApplied = 0;

                if (numAreas > 0 && areaNames != null)
                {
                    foreach (string name in areaNames)
                    {
                        string propName = "";
                        model.AreaObj.GetProperty(name, ref propName);

                        // Losa Techo
                        if (propName == "Losa_20")
                        {
                            model.AreaObj.SetLoadUniform(name, "LS", 3.6, 10, true, "Global"); // 10=Gravity
                            model.AreaObj.SetLoadUniform(name, "LL", 13.3, 10, true, "Global");
                            loadsApplied += 2;
                        }

                        // Paredes Generales
                        if (propName == "Pared_20")
                        {
                            model.AreaObj.SetLoadUniform(name, "EH", 4.05, 3, true, "Local"); // 3=Local 3
                            model.AreaObj.SetLoadUniform(name, "EQ", 1.5, 3, true, "Local");
                            loadsApplied += 2;
                        }

                        // Aleros
                        if (propName == "Alero_20")
                        {
                            // Sobrecarga de tierra (EH) lateral en direccion Local 3
                            model.AreaObj.SetLoadUniform(name, "EH", 4.05, 3, true, "Local");
                            // Sobrecarga Vehicular paralela (LS) en direccion Global
                            // Asumido perpendicular a piso, o presurizando vehiculo (horizontal / inclinado)
                            // Al ser muro de contención, usamos Local 3 tambien
                            model.AreaObj.SetLoadUniform(name, "LS", 3.6, 3, true, "Local");
                            loadsApplied += 2;
                        }
                    }
                }
                
                model.View.RefreshView(0, false);
                result.Success = true;
                result.Message = $"Patrones de carga creados y {loadsApplied} pases de carga asignados a Area Objects incluyendo Aleros.";
            }
            catch(Exception ex)
            {
                result.Success = false;
                result.Message = $"Error Loads: {ex.Message}";
            }
            
            return result;
        }
    }
}
