# Running

Make sure to run `docker-compose up` before running either of the console projects.

# A few considerations

These considerations primarily concern the `Sub` project since `Pub` is only a few lines of code long and is primarily useful as a smoke test helper.

Comments in the code should add a bit of context to why the implementation looks like it does.

## Overall approach

This project ran through a bunch of iterations. Both in terms of approach and the libraries used. I ultimately settled on a message passing architecture for intra-process communication, mirroring the message queue used for the inter-process communication. This allowed me to abstract the infrastructure quite nicely through the use of generics without resorting to a type hierarchy of interfaces and implementors.

## Failed original approach

My original idea was to simply poll for messages within a `while`-loop in the `Main` method, handling all processing, database updating, and re-publishing of messages within that loop. This should be immediately apparent as a bad idea -- making receiving messages from RabbitMQ wait for a roundtrip to the database is bound to spell trouble when a larger amount of messages could be received. I also decided to not use the official RabbitMQ library, instead of opting to use EasyNetQ, which abstracts most of the exchange and queue declarations away from the end user. While very useful for more advanced implementations it was simply noise in this specific implementation.

## Channels

My large experience with procedural programming in Rust has made me very well acquainted with the concept of message-passing channels. These channels are low-overhead and [don't allocate anything when not experiencing back pressure](https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/#performance).

The creation of the two channels and their associated tasks was initially done in two completely separate methods that each instantiated a channel, and constructed a lambda delegate to create a task. This creation machinery was later pulled out into `CreateChannel`, where it becomes clear that it is a generalizable pattern that is useful in a wider context where channels are used.

`HandleMessageDb` and `HandleSendMessage` both have some amount of boilerplate around first `.WaitToReadAsync` and `.TryRead`. I considered whether this could be pulled out into the delegate in `CreateChannel`, but since `HandleMessageDb` has to initialize there are, as I see it, two options to resolve this - neither of which are particularly ergonomic.

1. You could move the initialization to `Main` and move the context into the handler like was done with the `bus` for `HandleSendMessage`. This is not desirable in the current implementation because it moves initialization and usage further apart, a property I dislike without a clear gain from doing it.

2. An overload of `CreateChannel` could be made where a initialization delegate could be passed. That delegate could return an initialized object that would then be further passed to the actual handler. This is entirely doable, but the usage of such an overload simply exchanges one set of boilerplate in the handler for another set of boilerplate at channel creation time. If this was a common pattern in a larger codebase it could be worth considering, but as it stands it's just over-engineering.

## Failures

For this project I really wanted to do proper configuration management, so as to not hard-code the database password and avoid using the localhost-only RabbitMQ user. After scouring through too many MSDN articles, and using code examples in test projects that simply didn't work, I decided to use the hardcoded and default users. Had I started over on configuration handling I would've simply loaded a YAML or JSON configuration file via the ordinary deserializers for those formats. This avoids the official machinery for configurations, something I wouldn't be happy about, but it would still work.

## Future extension and modifications

If I had to add more features to this project I'd start out by evaluating implementations of the database connectivity layer in Dapper and in EntityFramework Core. EFCore is a very convenient framework for the simple task for inserting rows in a table, but I trust my own abilities in writing more complex queries. In previous hobby projects I've run into pathological SQL generation, and the upgrade from EF Core 2 to EF Core 3 required a rewrite of queries that was previously tuned for documented behavior.

I would also want to create a few facade interfaces. One to simply abstract away the specific MQ library and server, and others for modifying the behavior inside the incoming message handler. The one highlighted in the code is how the "new timestamp" requirement is interpreted.

Lastly, the code is primarily written as static methods inside of the `Program` class. With the addition of a few facades this would quickly become unwieldy very quickly. Moving the channel functionality from a destructured tuple to a wrapping class is one such possible improvement.
