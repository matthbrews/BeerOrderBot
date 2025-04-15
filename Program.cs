// Program.cs — Fully Integrated with Discord.Net.Interactions and polling

using System.Reflection;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BeerOrderBot.Services;
using Dapper;

namespace SpPickups
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Load config
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // Setup Discord client & interaction system
            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            });

            SqlMapper.AddTypeHandler(new JsonListHandler());

            var interactionService = new InteractionService(client.Rest);

            // DI container setup
            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton(config)
                .AddSingleton(client)
                .AddSingleton(interactionService)
                .AddSingleton<UserService>()
                .AddSingleton<OrderDbService>()
                .AddSingleton<DiscordService>()
                .AddSingleton<EmailService>()
                .AddSingleton(provider => new Lazy<EmailService>(() => provider.GetRequiredService<EmailService>()))
                .BuildServiceProvider();

            // Register slash command modules
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), services);

            // Register slash commands to guild (for faster testing)
            client.Ready += async () =>
            {
                ulong guildId = ulong.Parse(config["Discord:TestGuildId"]); // Add this to appsettings.json
                await interactionService.RegisterCommandsToGuildAsync(guildId);

            };

            // Handle interactions (slash + modal)
            client.InteractionCreated += async interaction =>
            {
                Console.WriteLine($"🔥 Interaction received! Type: {interaction.Type}");

                if (interaction is SocketMessageComponent component)
                    Console.WriteLine($"🧩 Component Interaction ID: {component.Data.CustomId}");

                var ctx = new SocketInteractionContext(client, interaction);
                var result = await interactionService.ExecuteCommandAsync(ctx, services);

                if (!result.IsSuccess)
                    Console.WriteLine($"❌ Interaction failed: {result.ErrorReason}");
            };



            // Login bot
            await client.LoginAsync(TokenType.Bot, config["Discord:Token"]);
            await client.StartAsync();


            // ✅ Start DiscordService to wire up message handlers
            var discordService = services.GetRequiredService<DiscordService>();
            await discordService.StartAsync();

            // Start polling email
            var emailService = services.GetRequiredService<EmailService>();

            while (true)
            {
                try
                {
                    await emailService.CheckInboxAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Email check failed: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        }
    }
}
