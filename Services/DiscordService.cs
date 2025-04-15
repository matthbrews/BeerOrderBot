using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using static System.Runtime.InteropServices.JavaScript.JSType;
using BeerOrderBot.Services;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static Org.BouncyCastle.Math.EC.ECCurve;

public class DiscordService
{
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly string _token;
    private readonly ulong _ordersChannelId;
    private readonly ulong _pickupsChannelId;
    private readonly ulong _registrationChannelId;
    private readonly UserService _userService;

    public DiscordService(IConfiguration config, UserService userService)
    {
        _config = config;
        _token = config["Discord:Token"];
        _ordersChannelId = ulong.Parse(config["Discord:Channels:Orders"]);
        _pickupsChannelId = ulong.Parse(config["Discord:Channels:Pickups"]);
        _registrationChannelId = ulong.Parse(config["Discord:Channels:Registration"]);
        _userService = userService;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds |
                     GatewayIntents.GuildMessages |
                     GatewayIntents.MessageContent  // THIS ONE
        });

        _client.Log += Log;
    }

    public async Task StartAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        _client.MessageReceived += HandleMessageAsync;

        await Task.Delay(2000); // wait for ready

        Console.WriteLine("✅ Discord bot started.");
    }

    private async Task HandleMessageAsync(SocketMessage message)
    {
        if (message.Author.IsBot || string.IsNullOrWhiteSpace(message.Content))
            return;

        var content = message.Content.Trim();
        Console.WriteLine(content);

        if (content.Equals("!help", StringComparison.OrdinalIgnoreCase))
        {
            _ = ShowHelp(message);
        }

        if (content.Equals("!whoami", StringComparison.OrdinalIgnoreCase))
        {
            _ = HandleWhoAmIAsync(message);
        }
    }

    private async Task ShowHelp(SocketMessage triggerMessage)
    {
        var channel = triggerMessage.Channel;

        await channel.SendMessageAsync($"Please register by typing /Register.\nTo check your current information, use !whoami\nTo initiate a pickup, please use /Pickup.");
    }


    private async Task<SocketMessage?> WaitForResponseFromUserAsync(ulong userId, IMessageChannel channel, int timeoutSeconds = 30)
    {
        var tcs = new TaskCompletionSource<SocketMessage>();

        Task Handler(SocketMessage msg)
        {
            if (msg.Author.Id == userId && msg.Channel.Id == channel.Id)
            {
                Console.WriteLine($"🕵️ Match: {msg.Author.Username} in #{msg.Channel}");
                tcs.TrySetResult(msg);
            }
            else
            {
                Console.WriteLine($"🔸 Ignored message from {msg.Author.Username} in {msg.Channel.Name}: {msg.Content}");
            }

            return Task.CompletedTask;
        }

        _client.MessageReceived += Handler;

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        _client.MessageReceived -= Handler;

        if (completedTask == timeoutTask)
        {
            Console.WriteLine("⏰ Timed out waiting for user response.");
            return null;
        }

        return await tcs.Task;
    }

    public async Task PostBeerOrderAsync(BeerOrder order)
    {
        if (_client.LoginState != LoginState.LoggedIn)
            return;

        var channel = _client.GetChannel(_ordersChannelId) as IMessageChannel;
        if (channel == null) return;

        var embed = new EmbedBuilder()
            .WithTitle($"🍺 New Order #{order.OrderNumber} from {order.Brewery}")
            .WithDescription(string.Join("\n", order.Items.Select(i => $"• {i}")))
            .WithFooter(footer => footer.Text = $"For: {order.Purchaser ?? "unknown"} — Not yet claimed")
            .WithColor(Color.Gold)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private async Task HandleWhoAmIAsync(SocketMessage message)
    {
        var channel = message.Channel;
        var userId = message.Author.Id;
        var guild = (message.Channel as SocketGuildChannel)?.Guild;
        var member = guild?.GetUser(userId);

        var user = await _userService.GetUserByDiscordIdAsync((long)userId);
        if (user == null)
        {
            await channel.SendMessageAsync("🫥 You’re not registered. Use `/register` to get started.");
            return;
        }

        var allowedRoles = _config.GetSection("Discord:AllowedRoles").Get<Dictionary<string, string>>();
        var roleNames = new List<string>();

        if (member != null)
        {
            foreach (var role in member.Roles)
            {
                if (allowedRoles.ContainsValue(role.Id.ToString()))
                    roleNames.Add(role.Name);
            }
        }

        string roleText = roleNames.Count > 0
            ? $"👥 Group(s): {string.Join(", ", roleNames)}"
            : "👥 Group(s): *none assigned*";

        var embed = new EmbedBuilder()
            .WithTitle("👤 Your Registration")
            .WithDescription($"📧 Email: `{user.Email}`\n🏷️ Name on Order: `{user.Alias ?? "n/a"}`\n{roleText}")
            .WithColor(Color.Blue)
            .Build();

        await channel.SendMessageAsync(embed: embed);

        // Offer a way to update roles
        var roleOptions = allowedRoles.Keys.Select(key =>
            new SelectMenuOptionBuilder(label: key, value: key)).ToList();

        var builder = new ComponentBuilder()
            .WithSelectMenu("select-roles", roleOptions, "Update your groups", 1, roleOptions.Count);

        await channel.SendMessageAsync("🔧 Want to update your group(s)? Select below:", components: builder.Build());
    }



    private Task Log(LogMessage msg)
    {
        Console.WriteLine($"[DISCORD] {msg}");
        return Task.CompletedTask;
    }
}
