﻿using System;

namespace WebSocketManager.Common
{
    public enum MessageType
    {
        Text,
        MethodInvocation,
        ConnectionEvent,
        MethodReturnValue
    }

    public class Message
    {
        public MessageType MessageType { get; set; }
        public string Data { get; set; }
        public string UserName { get; set; }
        public string Room { get; set; }
    }
}
