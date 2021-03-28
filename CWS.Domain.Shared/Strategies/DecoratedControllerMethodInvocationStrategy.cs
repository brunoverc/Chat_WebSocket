using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CWS.Domain.Shared.Networking;

namespace CWS.Domain.Shared.Strategies
{
    /// <summary>
    /// A estratégia de invocação do método do controller gravado ou decorado.
    /// Encontra métodos em diversas classes utilizando reflection
    /// </summary>
    public class DecoratedControllerMethodInvocationStrategy : MethodInvocationStrategy
    {
        public DecoratedControllerMethodInvocationStrategy()
        {
        }

        public DecoratedControllerMethodInvocationStrategy(string prefix)
        {
            Prefix = prefix;
        }

        public DecoratedControllerMethodInvocationStrategy(string prefix, char separator)
        {
            Prefix = prefix;
            Separator = separator;
        }

        public void Register(string prefix, object controller)
        {
            Controllers.Add(prefix.ToLower(), controller);
        }


        public string Prefix { get; } = "";

        public char Separator { get; } = '/';

        public bool NoWebsocketArgument { get; set; } = false;

        public Dictionary<string, object> Controllers { get; } = new Dictionary<string, object>();

        public override async Task<object> OnInvokeMethodReceivedAsync(WebSocket socket, InvocationDescriptor invocationDescriptor)
        {

            if (!invocationDescriptor.MethodName.Contains(Separator)) throw new Exception($"Invalid controller or method name '{invocationDescriptor.MethodName}'.");


            string[] names = invocationDescriptor.MethodName.Split(Separator);
            string controller = names[0].ToLower();
            string command = Prefix + names[1];


            if (Controllers.TryGetValue(controller, out object self))
            {

                MethodInfo method = self.GetType().GetMethod(command);


                if (method == null)
                    throw new Exception($"Comando desconhecido recebido '{command}' para o controller '{controller}'.");
                
                List<object> args = invocationDescriptor.Arguments.ToList();
                if (!NoWebsocketArgument)
                    args.Insert(0, socket);
                try
                {
                    return await Task.Run(() => method.Invoke(self, args.ToArray()));
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
            }
            else throw new Exception($"Comando desconhecido recebido '{command}' para o controller desconhecido '{controller}'.");
        }
    }
}
