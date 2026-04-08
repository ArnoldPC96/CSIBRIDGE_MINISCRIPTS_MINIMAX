using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CSiAPIv1;

namespace CsiRunner
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string scriptClass = args.Length > 0 ? args[0] : "Scripts.HelloCsi";
            string outputDir = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../output");
            
            Console.WriteLine($"[Runner] Iniciando ejecucion de {scriptClass} ...");

            ScriptResult result = new ScriptResult();
            cOAPI app = null;
            cSapModel model = null;

            try
            {
                // Conectar al COM object activo de CSiBridge
                Console.WriteLine("[Runner] Consiguiendo COM Object 'CSI.CSiBridge.API.SapObject'...");
                app = Marshal.GetActiveObject("CSI.CSiBridge.API.SapObject") as cOAPI;
                if (app == null)
                {
                    throw new Exception("No se pudo obtener la instancia de CSiBridge. Asegurate de que el programa este abierto.");
                }

                model = app.SapModel;

                // Cargar script mediante reflection
                Type type = Assembly.GetExecutingAssembly().GetType(scriptClass);
                if (type == null)
                {
                    throw new Exception($"La clase {scriptClass} no fue encontrada en el ensamblado compilado.");
                }

                ICsiScript scriptInstance = Activator.CreateInstance(type) as ICsiScript;
                if (scriptInstance == null)
                {
                    throw new Exception($"La clase {scriptClass} no implementa ICsiScript.");
                }

                Console.WriteLine("[Runner] Ejecutando logica del script...");
                // Ejecucion de la rutina de prueba
                result = scriptInstance.Run(app, model);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Excepcion en Runner: {ex.Message} \n {ex.StackTrace}";
            }
            finally
            {
                Console.WriteLine("[Runner] Liberando objetos COM y finalizando.");
                // COM cleanup, critical to avoid locking CSiBridge UI thread
                if (app != null)
                {
                    app = null;
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            // Guardar proof.json
            try
            {
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                string jsonOutput = result.ToJson();
                string jsonPath = Path.Combine(outputDir, "proof.json");
                File.WriteAllText(jsonPath, jsonOutput);
                Console.WriteLine($"[Runner] proof.json volcado exitosamente en {jsonPath}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Runner] Error critico guardando proof.json: {e.Message}");
            }
        }
    }
}
