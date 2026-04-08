using System;
using CSiAPIv1;

namespace CsiRunner
{
    public interface ICsiScript
    {
        ScriptResult Run(cOAPI app, cSapModel model);
    }
}
