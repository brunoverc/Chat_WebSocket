using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Tasks;
using CWS.Domain.Shared.Networking;

namespace CWS.Domain.Shared.Strategies
{
    /// <summary>
    /// Utilizado para encontrar métodos em uma única classe utilizando reflection 
    /// </summary>
    public class ControllerMethodInvocationStrategy : MethodInvocationStrategy
    {
        public ControllerMethodInvocationStrategy()
        {

        }

        public ControllerMethodInvocationStrategy(object controller)
        {
            Controller = controller;
        }

        public ControllerMethodInvocationStrategy(string prefix, object controller)
        {
            Prefix = prefix;
            Controller = controller;
        }

        public object Controller { get; set; }
        public string Prefix { get; } = "";
        public bool NoWebsocketArgument { get; set; } = false;


        public override async Task<object> OnInvokeMethodReceivedAsync(WebSocket socket, InvocationDescriptor invocationDescriptor)
        {
            string command = Prefix + invocationDescriptor.MethodName;

            MethodInfo method = Controller.GetType().GetMethod(command);

            if(method == null)
                throw new Exception($"Comando desconhecido recebido '{command}' para o controller '{Controller.GetType().Name}'.");

            List<object> args = invocationDescriptor.Arguments.ToList();

            if (!NoWebsocketArgument)
                args.Insert(0, socket);

            try
            {
                return await Task.Run(() => method.Invoke(Controller, args.ToArray()));
            }
            catch(TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }
        
    }
}
