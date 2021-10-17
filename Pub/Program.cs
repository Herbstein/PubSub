using System;
using System.Threading;
using System.Threading.Tasks;
using EasyNetQ;
using Shared;

namespace Pub
{
    internal static class Program
    {
        private static async Task Main()
        {
            var keepRunning = true;

            // Stop running when the user cancels the process
            Console.CancelKeyPress += delegate { keepRunning = false; };

            // Connect to RabbitMQ using the default guest:guest account
            using var bus = RabbitHutch.CreateBus("host=localhost");

            while (keepRunning)
            {
                var message = new MessageObject("some_message");

                await bus.SendReceive.SendAsync("pubsub", message);

                Console.WriteLine("Published message");

                // Sleep amount skips seconds and alternates between even and odd
                Thread.Sleep(3300);
            }
        }
    }
}