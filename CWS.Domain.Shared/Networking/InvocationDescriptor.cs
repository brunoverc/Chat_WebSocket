using System;
using Newtonsoft.Json;

namespace CWS.Domain.Shared.Networking
{
    /// <summary>
    /// Representa um nome de método com parâmetros que devem ser executados remotamente
    /// </summary>
    public class InvocationDescriptor
    {
        [JsonProperty("methodName")]
        public string MethodName { get; set; }

        [JsonProperty("arguments")]
        public object[] Arguments { get; set; }

        [JsonProperty("identifier")]
        public Guid Identifier { get; set; } = Guid.Empty;
    }
}
