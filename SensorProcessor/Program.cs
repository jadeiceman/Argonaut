using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace SensorProcessor
{
    using Argonaut.Networking;
    using System.Threading;

    class Program
    {
        public EventHubSettings eventHubSettings;
        public EventReceiver receiver;

        static void Main(string[] args)
        {
            var p = new Program();

            p.eventHubSettings = new EventHubSettings()
            {
                ServiceBusName = "lightningbolt",
                EventHubName = "sumobot",
                Policies = new Dictionary<string, string>() 
                { 
                    { "manage", "Z6KTK+hqBmMiu0WPLchX0oaYDMd+K2nwSRpZKt4RsqA=" },
                    { "device", "syc3awPWUXN1V+KbdAFmpPwx/r0F3chn1SLZJ6smioY=" },
                    { "processor", "8cxDOUPPQLkFIskUxMFCi9JNrBv6xHN8WcIwDz2fYhA=" }
                }
            };

            p.receiver = new EventReceiver(
                "SensorProcessor", 
                p.eventHubSettings, 
                "processor");

            var fromTimestamp = DateTime.Now.AddHours(-1).ToUniversalTime();

            var receiveLinks = new ReceiveLinkDescription[]
            {
                new ReceiveLinkDescription("temperature", "0")
                {
                    TimeCheckPoint = fromTimestamp
                },
                new ReceiveLinkDescription("sonar", "1")
                {
                    TimeCheckPoint = fromTimestamp
                },
                new ReceiveLinkDescription("ir", "2")
                {
                    TimeCheckPoint = fromTimestamp
                },
            };

            Console.WriteLine("Initializing comms...");
            p.receiver.InitializeChannels(receiveLinks);

            Console.WriteLine("Monitoring incoming transmissions...");
            Message message = null;
            while (true)
            {
                do
                {
                    message = p.receiver.ReadMessage("temperature");
                    if (message != null) p.ShowResponseDetails("temperature", message);
                    else Console.Write(".");

                    message = p.receiver.ReadMessage("ir");
                    if (message != null) p.ShowResponseDetails("ir", message);
                    else Console.Write(".");

                    message = p.receiver.ReadMessage("sonar");
                    if (message != null) p.ShowResponseDetails("sonar", message);
                    else Console.Write(".");
                }
                while (message != null);

                Thread.Sleep(1000);
            }
        }

        public void ShowResponseDetails(string channel, Message message)
        {
            string content = Encoding.UTF8.GetString((byte[])message.Body);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendFormat("{0} Message {1}:\r\n", channel, message.SequenceNumber);
            sb.AppendFormat("  Offset: {0}\r\n", message.Offset);
            sb.AppendFormat("  Enqueued Time UTC: {0} ({1})\r\n",
                message.EnqueuedTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                message.EnqueuedTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.AppendLine("   Content: " + content);
            Console.WriteLine(sb.ToString());
        }

    }
}
