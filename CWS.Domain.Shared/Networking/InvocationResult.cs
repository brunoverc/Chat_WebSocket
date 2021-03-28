using System;
using Newtonsoft.Json;

namespace CWS.Domain.Shared.Networking
{
    /// <summary>
    /// Representa o valor de retorno de um método executado remotamente
    /// </summary>
    public class InvocationResult
    {
        [JsonProperty("identifier")]
        public Guid Identifier { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("exception")]
        public RemoteException Exception { get; set; }
    }
}
