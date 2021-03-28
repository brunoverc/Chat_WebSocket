using System;
using Newtonsoft.Json;

namespace CWS.Domain.Shared.Networking
{
    /// <summary>
    /// Exceptions que ocorrem remotamente
    /// </summary>
    public class RemoteException
    {
        public RemoteException()
        {
        }

        public RemoteException(Exception exception)
        {
            Message = $"A Exceção remota ocorreu em: '{exception.Message}'.";
        }

        [JsonProperty("message")]
        public string Message { get; set; } = $" Exceção remota ocorreu";


    }
}
