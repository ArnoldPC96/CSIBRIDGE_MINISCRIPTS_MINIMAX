using System;
using CSiAPIv1;
using CsiRunner;

namespace Scripts.Combinations
{
    public class CulvertCombinations : ICsiScript
    {
        public ScriptResult Run(cOAPI app, cSapModel model)
        {
            var result = new ScriptResult();
            try
            {
                // Combinaciones AASHTO LRFD
                eCNameType ctype = eCNameType.LoadCase; // 0 = LoadCase
                
                // 1. Strength I: 1.25DC + 1.50EH + 1.75LL + 1.75LS
                model.RespCombo.Add("Strength I", 0); // 0 = Linear Add
                model.RespCombo.SetCaseList("Strength I", ref ctype, "DC", 1.25);
                model.RespCombo.SetCaseList("Strength I", ref ctype, "EH", 1.50);
                model.RespCombo.SetCaseList("Strength I", ref ctype, "LL", 1.75);
                model.RespCombo.SetCaseList("Strength I", ref ctype, "LS", 1.75);

                // 2. Strength III: 1.25DC + 1.50EH
                model.RespCombo.Add("Strength III", 0);
                model.RespCombo.SetCaseList("Strength III", ref ctype, "DC", 1.25);
                model.RespCombo.SetCaseList("Strength III", ref ctype, "EH", 1.50);

                // 3. Extreme Event I: 1.25DC + 1.50EH + 1.00EQ + 0.50LL
                model.RespCombo.Add("Extreme Event", 0);
                model.RespCombo.SetCaseList("Extreme Event", ref ctype, "DC", 1.25);
                model.RespCombo.SetCaseList("Extreme Event", ref ctype, "EH", 1.50);
                model.RespCombo.SetCaseList("Extreme Event", ref ctype, "EQ", 1.00);
                model.RespCombo.SetCaseList("Extreme Event", ref ctype, "LL", 0.50);

                // 4. Service I: 1.00DC + 1.00EH + 1.00LL + 1.00LS
                model.RespCombo.Add("Service I", 0);
                model.RespCombo.SetCaseList("Service I", ref ctype, "DC", 1.00);
                model.RespCombo.SetCaseList("Service I", ref ctype, "EH", 1.00);
                model.RespCombo.SetCaseList("Service I", ref ctype, "LL", 1.00);
                model.RespCombo.SetCaseList("Service I", ref ctype, "LS", 1.00);

                result.Success = true;
                result.Message = "Combinaciones LRFD AASHTO (Strength I, III, Extreme y Service) configuradas exitosamente en CSiBridge.";
            }
            catch(Exception ex)
            {
                result.Success = false;
                result.Message = $"Error Combinations: {ex.Message}";
            }
            
            return result;
        }
    }
}
