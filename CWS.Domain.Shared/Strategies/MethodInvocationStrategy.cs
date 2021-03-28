using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using CWS.Domain.Shared.Networking;

namespace CWS.Domain.Shared.Strategies
{
    /// <summary>
    /// Classe base das estratégias de invocação
    /// </summary>
    public abstract class MethodInvocationStrategy
    {
        public virtual Task<object> OnInvokeMethodReceivedAsync(WebSocket socket, InvocationDescriptor invocationDescriptor)
        {
            throw new NotImplementedException();
        }
    }
}
