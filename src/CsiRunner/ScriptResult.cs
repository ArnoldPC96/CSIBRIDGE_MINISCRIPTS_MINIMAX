using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace CsiRunner
{
    public class ScriptResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Data { get; set; }

        public ScriptResult()
        {
            Data = new Dictionary<string, object>();
            Success = false;
        }

        public string ToJson()
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(this);
        }
    }
}
