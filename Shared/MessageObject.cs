using System;

namespace Shared
{
    [Serializable]
    public class MessageObject
    {
        public DateTime Timestamp;
        public string Message;

        public MessageObject(string message)
        {
            Message = message;
            Timestamp = DateTime.Now;
        }
    }
}