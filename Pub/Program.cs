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

            var counter = 0;

            while (keepRunning)
            {
                // Construct a message object with a inconsequential message attached.
                // This simply simulates an object with more data attached to the, in this case, all-important timestamp
                var message = new MessageObject($"some_message_{counter++}");

                // Send the message on the `pubsub` queue
                await bus.SendReceive.SendAsync("pubsub", message);

                // Sleep amount skips seconds and alternates between even and odd
                Thread.Sleep(3300);
            }
        }
    }
}