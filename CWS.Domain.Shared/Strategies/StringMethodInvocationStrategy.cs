using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading.Tasks;
using CWS.Domain.Shared.Networking;

namespace CWS.Domain.Shared.Strategies
{
    /// <summary>
    /// Estratégia de invocação de método para string. Encontra métodos registrando os nomes e retornos de chamada.
    /// </summary>
    public class StringMethodInvocationStrategy : MethodInvocationStrategy
    {
        private Dictionary<string, InvocationHandler> _handlers = new Dictionary<string, InvocationHandler>();

        public void On(string methodName, Action<object[]> handler)
        {
            var invocationHandler = new InvocationHandler(handler, new Type[] { });
            _handlers.Add(methodName, invocationHandler);
        }

        public void On(string methodName, Func<object[], object> handler)
        {
            var invocationHandler = new InvocationHandler(handler, new Type[] { });
            _handlers.Add(methodName, invocationHandler);
        }

        private class InvocationHandler
        {
            public Func<object[], object> Handler { get; set; }
            public Type[] ParameterTypes { get; set; }

            public InvocationHandler(Func<object[], object> handler, Type[] parameterTypes)
            {
                Handler = handler;
                ParameterTypes = parameterTypes;
            }

            public InvocationHandler(Action<object[]> handler, Type[] parameterTypes)
            {
                Handler = (args) => { handler(args); return null; };
                ParameterTypes = parameterTypes;
            }
        }

        public override async Task<object> OnInvokeMethodReceivedAsync(WebSocket socket, InvocationDescriptor invocationDescriptor)
        {
            if (!_handlers.ContainsKey(invocationDescriptor.MethodName))
                throw new Exception($"Comando desconhecido recebido '{invocationDescriptor.MethodName}'.");
            var invocationHandler = _handlers[invocationDescriptor.MethodName];
            if (invocationHandler != null)
                return await Task.Run(() => invocationHandler.Handler(invocationDescriptor.Arguments));
            return await Task.FromResult<object>(null);
        }
    }
}
