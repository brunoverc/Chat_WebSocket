using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CWS.Domain;
using CWS.Domain.Shared;
using CWS.Domain.Shared.Json;
using CWS.Domain.Shared.Networking;
using CWS.Domain.Shared.Strategies;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CWS.ApplicationService
{
    public abstract class WebSocketHandler
    {
        protected WebSocketConnectionManager WebSocketConnectionManager { get; set; }

        private JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.All,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            SerializationBinder = new JsonBinderWithoutAssembly()
        };

        private Dictionary<Guid, TaskCompletionSource<InvocationResult>> _waitingRemoteInvocations = new Dictionary<Guid, TaskCompletionSource<InvocationResult>>();


        public MethodInvocationStrategy MethodInvocationStrategy { get; }


        public WebSocketHandler(WebSocketConnectionManager webSocketConnectionManager, MethodInvocationStrategy methodInvocationStrategy)
        {
            _jsonSerializerSettings.Converters.Insert(0, new PrimitiveJsonConverter());
            WebSocketConnectionManager = webSocketConnectionManager;
            MethodInvocationStrategy = methodInvocationStrategy;
        }

        public virtual async Task OnConnected(WebSocket socket)
        {
            WebSocketConnectionManager.AddSocket(socket);

            await SendMessageAsync(socket, new Message()
            {
                MessageType = MessageType.ConnectionEvent,
                Data = WebSocketConnectionManager.GetId(socket)
            }).ConfigureAwait(false);
        }

        public virtual async Task OnDisconnected(WebSocket socket)
        {
            await WebSocketConnectionManager.RemoveSocket(WebSocketConnectionManager.GetId(socket)).ConfigureAwait(false);
        }

        public async Task SendMessageAsync(WebSocket socket, Message message)
        {
            if (socket.State != WebSocketState.Open)
                return;

            var serializedMessage = JsonConvert.SerializeObject(message, _jsonSerializerSettings);
            var encodedMessage = Encoding.UTF8.GetBytes(serializedMessage);
            await socket.SendAsync(buffer: new ArraySegment<byte>(array: encodedMessage,
                                                                  offset: 0,
                                                                  count: encodedMessage.Length),
                                   messageType: WebSocketMessageType.Text,
                                   endOfMessage: true,
                                   cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }

        public async Task SendMessageAsync(string socketId, Message message)
        {
            await SendMessageAsync(WebSocketConnectionManager.GetSocketById(socketId), message).ConfigureAwait(false);
        }

        public async Task SendMessageToAllAsync(Message message)
        {
            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                try
                {
                    if (pair.Value.State == WebSocketState.Open)
                        await SendMessageAsync(pair.Value, message).ConfigureAwait(false);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        await OnDisconnected(pair.Value);
                    }
                }
            }
        }

        public async Task InvokeClientMethodAsync(string socketId, string methodName, object[] arguments)
        {
            var message = new Message()
            {
                MessageType = MessageType.MethodInvocation,
                Data = JsonConvert.SerializeObject(new InvocationDescriptor()
                {
                    MethodName = methodName,
                    Arguments = arguments
                }, _jsonSerializerSettings)
            };

            await SendMessageAsync(socketId, message).ConfigureAwait(false);
        }

        public async Task<T> InvokeClientMethodAsync<T>(string socketId, string methodName, object[] arguments)
        {
            InvocationDescriptor invocationDescriptor = new InvocationDescriptor { MethodName = methodName, Arguments = arguments };

            invocationDescriptor.Identifier = Guid.NewGuid();

            TaskCompletionSource<InvocationResult> task = new TaskCompletionSource<InvocationResult>();
            new CancellationTokenSource(1000 * 60).Token.Register(() => { _waitingRemoteInvocations.Remove(invocationDescriptor.Identifier); task.TrySetCanceled(); });
            _waitingRemoteInvocations.Add(invocationDescriptor.Identifier, task);

            var message = new Message() { MessageType = MessageType.MethodInvocation, Data = JsonConvert.SerializeObject(invocationDescriptor, _jsonSerializerSettings) };
            await SendMessageAsync(socketId, message).ConfigureAwait(false);

            InvocationResult result = await task.Task;


            if (task.Task.IsCompleted)
            {
                if (result.Exception != null)
                    throw new Exception(result.Exception.Message);

                if (result.Result == null) return default(T);
                return (T)result.Result;
            }

            throw new TimeoutException(); 
        }

        public async Task InvokeClientMethodToAllAsync(string methodName, params object[] arguments)
        {
            foreach (var pair in WebSocketConnectionManager.GetAll())
            {
                try
                {
                    if (pair.Value.State == WebSocketState.Open)
                        await InvokeClientMethodAsync(pair.Key, methodName, arguments).ConfigureAwait(false);
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        await OnDisconnected(pair.Value);
                    }
                }
            }
        }

        public async Task SendMessageToGroupAsync(string groupID, Message message)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var socket in sockets)
                {
                    await SendMessageAsync(socket, message);
                }
            }
        }

        public async Task SendMessageToGroupAsync(string groupID, Message message, string except)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    if (id != except)
                        await SendMessageAsync(id, message);
                }
            }
        }

        public async Task InvokeClientMethodToGroupAsync(string groupID, string methodName, params object[] arguments)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    await InvokeClientMethodAsync(id, methodName, arguments);
                }
            }
        }

        public async Task InvokeClientMethodToGroupAsync(string groupID, string methodName, string except, params object[] arguments)
        {
            var sockets = WebSocketConnectionManager.GetAllFromGroup(groupID);
            if (sockets != null)
            {
                foreach (var id in sockets)
                {
                    if (id != except)
                        await InvokeClientMethodAsync(id, methodName, arguments);
                }
            }
        }

        public async Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, Message receivedMessage)
        {
            if (receivedMessage.MessageType == MessageType.MethodInvocation)
            {
                InvocationDescriptor invocationDescriptor = null;
                try
                {
                    invocationDescriptor = JsonConvert.DeserializeObject<InvocationDescriptor>(receivedMessage.Data, _jsonSerializerSettings);
                    if (invocationDescriptor == null) return;
                }
                catch { return; } // ignore invalid data sent to the server.

                if (invocationDescriptor.Identifier == Guid.Empty)
                {
                    try
                    {
                        await MethodInvocationStrategy.OnInvokeMethodReceivedAsync(socket, invocationDescriptor);
                    }
                    catch (Exception)
                    {
                        
                    }
                }
                else
                {
                    InvocationResult invokeResult;
                    try
                    {
                        invokeResult = new InvocationResult()
                        {
                            Identifier = invocationDescriptor.Identifier,
                            Result = await MethodInvocationStrategy.OnInvokeMethodReceivedAsync(socket, invocationDescriptor),
                            Exception = null
                        };
                    }
                    catch (Exception ex)
                    {
                        invokeResult = new InvocationResult()
                        {
                            Identifier = invocationDescriptor.Identifier,
                            Result = null,
                            Exception = new RemoteException(ex)
                        };
                    }

                    var message = new Message()
                    {
                        MessageType = MessageType.MethodReturnValues,
                        Data = JsonConvert.SerializeObject(invokeResult, _jsonSerializerSettings)
                    };
                    await SendMessageAsync(socket, message).ConfigureAwait(false);
                }
            }

            // method return value.
            else if (receivedMessage.MessageType == MessageType.MethodReturnValues)
            {
                var invocationResult = JsonConvert.DeserializeObject<InvocationResult>(receivedMessage.Data, _jsonSerializerSettings);
                if (_waitingRemoteInvocations.ContainsKey(invocationResult.Identifier))
                {
                    _waitingRemoteInvocations[invocationResult.Identifier].SetResult(invocationResult);                    _waitingRemoteInvocations.Remove(invocationResult.Identifier);
                }
            }
        }

        public async Task InvokeClientMethodOnlyAsync(string socketId, string method) => await InvokeClientMethodAsync(socketId, method, new object[] { });

        public async Task InvokeClientMethodOnlyAsync<T1>(string socketId, string method, T1 arg1) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2>(string socketId, string method, T1 arg1, T2 arg2) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 });

        public async Task InvokeClientMethodOnlyAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await InvokeClientMethodAsync(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 });

        public async Task<Result> InvokeClientMethodAsync<Result>(string socketId, string method) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { });

        public async Task<Result> InvokeClientMethodAsync<Result, T1>(string socketId, string method, T1 arg1) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2>(string socketId, string method, T1 arg1, T2 arg2) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 });

        public async Task<Result> InvokeClientMethodAsync<Result, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string socketId, string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await InvokeClientMethodAsync<Result>(socketId, method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 });

        public async Task InvokeClientMethodToAllAsync(string method) => await InvokeClientMethodToAllAsync(method, new object[] { });

        public async Task InvokeClientMethodToAllAsync<T1>(string method, T1 arg1) => await InvokeClientMethodToAllAsync(method, new object[] { arg1 });

        public async Task InvokeClientMethodToAllAsync<T1, T2>(string method, T1 arg1, T2 arg2) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3>(string method, T1 arg1, T2 arg2, T3 arg3) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15 });

        public async Task InvokeClientMethodToAllAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16) => await InvokeClientMethodToAllAsync(method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 });
    }
}
