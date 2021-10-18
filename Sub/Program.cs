using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using EasyNetQ;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace Sub
{
    internal static class Program
    {
        /// <summary>
        /// Create an unbounded channel for sending message objects to the given handler.
        /// </summary>
        /// <param name="handler">Handler receiving and making decisions based on a <see cref="T"/>.</param>
        /// <returns>A tuple containing a <see cref="ChannelWriter{T}"/> for sending message objects to the handler,
        /// and a <see cref="Task"/> representing the handler running in the background.</returns> 
        private static (ChannelWriter<T>, Task) CreateChannel<T>(
                Func<ChannelReader<T>, Task> handler)
        {
            // Unbounded channels could be a problem in internet-facing situations.
            // Used internally between two programs here, with a single producer
            //   and handling at most two messages at a time. 
            var channel = Channel.CreateUnbounded<T>();
            var (channelReader, channelWriter) = (channel.Reader, channel.Writer);

            // Inject the channel reader into the handler and run the task
            var task = Task.Run(async () => await handler(channelReader));
            return (channelWriter, task);
        }

        /// <summary>
        /// Persist a <see cref="MessageObject"/> in the connected database.
        /// </summary>
        /// <param name="reader">The <see cref="ChannelReader{T}"/> to receive messages from.</param>
        private static async Task HandleMessageDb(ChannelReader<MessageObject> reader)
        {
            // Connect to the running Database 
            await using var db = new MessageContext();

            // Make sure the database has had the migrations run
            // Because of this we don't have to manually run `dotnet ef database update`
            await db.Database.MigrateAsync();

            Console.WriteLine("Migrated DB and awaiting message to publish in DB");

            // Waits until there is new messages or the associated writer has been closed
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var message))
                {
                    // Convert the incoming message object to a DB message and save it in the database 
                    await db.Messages.AddAsync(new Message(message));
                    await db.SaveChangesAsync();
                }
            }
        }

        /// <summary>
        /// Sends a <see cref="MessageObject"/> received from the <see cref="reader"/> to a queue on <see cref="bus"/>.
        /// </summary>
        /// <param name="reader">The <see cref="ChannelReader{T}"/> to receive messages from.</param>
        /// <param name="bus">An <see cref="IBus"/> instance representing an active connection to a RabbitMQ instance</param>
        private static async Task HandleSendMessage(ChannelReader<MessageObject> reader, IBus bus)
        {
            Console.WriteLine("Awaiting message to re-publish");

            // Waits until there is new messages or the associated writer has been closed
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var item))
                {
                    // Bus is disposable and will be destroyed in the outer scope.
                    // However, this task is explicitly finished before disposal happens.
                    await bus.SendReceive.SendAsync("pubsub", item);
                }
            }
        }

        /// <summary>
        /// Handle incoming messages from the message queue.
        /// </summary>
        /// <param name="message">The <see cref="MessageObject"/> to evaluate and forward.</param>
        /// <param name="dbChannelWriter">The <see cref="ChannelWriter{T}"/> for forwarding to the database.</param>
        /// <param name="mqChannelWriter">The <see cref="ChannelWriter{T}"/> for re-forwarding to the message queue</param>
        private static async Task HandleIncomingMessage(MessageObject message,
                                                        ChannelWriter<MessageObject> dbChannelWriter,
                                                        ChannelWriter<MessageObject> mqChannelWriter)
        {
            // Check if the message is older than a minute
            var timespanSinceMessage = DateTime.Now.Subtract(message.Timestamp);
            if (timespanSinceMessage.TotalMinutes >= 1)
            {
                // Ignore older messages completely
                return;
            }

            // Check if message timestamp is even
            if (message.Timestamp.Second % 2 == 0)
            {
                // Send even timestamped messages to be handled by DB
                await dbChannelWriter.WriteAsync(message);
            }
            else
            {
                // If the timestamp is currently at an odd second subtracting a single second makes it even
                // var updatedTimestamp = message.Timestamp.Subtract(TimeSpan.FromSeconds(1));
                // message.Timestamp = updatedTimestamp;

                // Timestamp is odd. Give the message a new one.
                // This results in a lot of chatter with the message queue.
                // Use the out-commented implementation above to test without the chatter. 
                //   (this is ripe for using a facade pattern)
                message.Timestamp = DateTime.Now;

                // Send odd 
                await mqChannelWriter.WriteAsync(message);
            }
        }

        private static async Task Main()
        {
            // Connect to RabbitMQ using the default guest:guest account
            var bus = RabbitHutch.CreateBus("host=localhost");

            // Build internal communication channels used to offload db functionality from message handler
            var (dbChannelWriter, dbTask) = CreateChannel<MessageObject>(HandleMessageDb);

            // Taking control of a disposable and then calling `.Dispose()` from this original scope could be bad form
            // Handling the lifetime of disposable objects is crucial because they are not disposed automatically 
            //   during destruction/deallocation/on scope exit
            // https://dotnettips.wordpress.com/2021/10/06/hidden-idisposable-issues-in-microsoft-net/
            // In this specific case it's acceptable because both the channel and the running task is explicitly finished
            //   before `bus` is disposed by the controlling thread, making use-after-free-style invalid states on the bus
            //   impossible.
            // In this specific case we could allow the closure to dispose of the bus, but in a larger application we
            //   might want to continue using it for other operations. 
            var (mqChannelWriter, mqTask) = CreateChannel<MessageObject>(async reader =>
            {
                // ReSharper disable once AccessToDisposedClosure
                await HandleSendMessage(reader, bus);
            });

            // Wrap the message handler in a message-receiving delegate
            var receiver = await bus.SendReceive.ReceiveAsync<MessageObject>(
                    "pubsub",
                    async message => { await HandleIncomingMessage(message, dbChannelWriter, mqChannelWriter); }
            );

            // Wait for user input before exiting
            Console.WriteLine("Press [enter] to exit.");
            Console.ReadLine();

            // Stop handling messages
            receiver.Dispose();

            // Close channels. The message handler using these has been stopped
            mqChannelWriter.Complete();
            dbChannelWriter.Complete();

            // Wait for the handler tasks to stop running as the writers have been completed
            await Task.WhenAll(mqTask, dbTask);

            // Close connection to RabbitMQ
            bus.Dispose();
        }
    }
}