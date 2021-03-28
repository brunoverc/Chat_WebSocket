using System;
namespace CWS.Domain.Shared
{
    public enum MessageType
    {
        Text,
        MethodInvocation,
        ConnectionEvent,
        MethodReturnValues
    }

    public class Message
    {
        public MessageType MessageType { get; set; }
        public string Data { get; set; }
    }
}
