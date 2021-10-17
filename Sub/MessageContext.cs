using System;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace Sub
{
    public class MessageContext : DbContext
    {
        public DbSet<Message> Messages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Username=postgres;Password=psqlpassword;Database=postgres");
        }
    }

    public class Message
    {
        public int Id { get; set; }
        public string Msg { get; set; }
        public DateTime Timestamp { get; set; }

        public Message()
        {
        }

        public Message(MessageObject from)
        {
            Msg = from.Message;
            Timestamp = from.Timestamp;
        }
    }
}