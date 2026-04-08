# Puente IA ↔ CSiBridge 26 via C#.NET COM

## Contexto del Proyecto

Se ha establecido un puente de comunicación entre IAs avanzadas (como Claude Code, Antigravity, OpenCode) y una instancia activa de **CSiBridge 26**. El puente permite que la IA escriba, compile y ejecute scripts C# sin necesidad de reiniciar CSiBridge cada vez.

## Arquitectura del Puente

```
IA (Antigravity) → run.ps1 → MSBuild (compila x64) → CsiRunner.exe → CSiBridge (vía COM activa)
                                                                  ↓
                                                          proof.json ← IA (lee resultado)
```

1. **Escritura**: La IA escribe o edita archivos `.cs` en la carpeta `scripts/`.
2. **Compilación y Orquestación**: Se invoca `.\run.ps1 -ScriptClass "Namespaces.MiScript"`. Este usa `MSBuild.exe` nativo de Visual Studio 2022 o BuildTools para compilar al vuelo sin depender de `.sln`.
3. **Ejecución y Attach OAPI**: `CsiRunner.exe` usa `Marshal.GetActiveObject("CSI.CSiBridge.API.SapObject")` para ubicar el programa abierto, arrancar la Interfaz `cOAPI` e inyectar el método `Run()`.
4. **Verificación**: Al terminar se deposita de manera plana un `proof.json` en `output/` con los resultados para ser validados de inmediato.

### Requisitos y Restricciones
- **.NET Framework 4.8, x64**: Requerido pues `CSiAPIv1.dll` es un wrapper COM antiguo de 64 bits y no funciona bien con .NET Core out-of-the-box sin un wrapper especial.
- El modelo **debe estar desbloqueado** en CSiBridge antes de correr mallas o cambiar materiales.
- **NO DEBES** requerir NuGets. Serializamos JSON estáticos u orientados a `System.Web.Extensions` para agilizar el tiempo cero en pruebas.

### Estructura de Directorios

```
E:\VS_PROYECTOS\CSIBRIDGE_MINISCRIPTS\
├── run.ps1                           # CLI compila y corre el binario
├── src\CsiRunner\                    # Entorno NET Framework 4.8
│   ├── CsiRunner.csproj              # Ref-> CSiAPIv1.dll EmbedInteropTypes=False
│   ├── Program.cs                    # Entry Point: Atrapa el COM y ejecuta el script
│   ├── ICsiScript.cs                 # Interfaz requerida
│   └── ScriptResult.cs               # Clase de volcado
├── scripts\                          # Rutinas que la IA va creando
│   └── HelloCsi.cs                   # Smoke test inicial.
├── output\                           # logs locales (proof.json)
├── openspec\                         # SDD Methodology (Gentle AI)
└── Resumen para IAs\                 # Este documento de contexto técnico
```
