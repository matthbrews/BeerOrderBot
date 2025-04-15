// /Modules/PickupModule.cs
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using BeerOrderBot.Services;

public class PickupModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly OrderDbService _orderDb;
    private readonly IConfiguration _config;
    private readonly UserService _userService;
    private readonly EmailService _emailService;

    public PickupModule(OrderDbService orderDb, IConfiguration config, UserService userService, EmailService emailService )
    {
        _orderDb = orderDb;
        _config = config;
        _userService = userService;
        _emailService = emailService;
    }

    [SlashCommand("pickup", "Start a beer pickup for yourself or others")]
    public async Task StartPickup()
    {
        var options = new List<SelectMenuOptionBuilder>
        {
            new("All", "All", "Pick up for all users with unclaimed orders"),
            new("Specific", "Specific", "Pick up for specific users")
        };

        var menu = new ComponentBuilder()
            .WithSelectMenu("pickup-mode-select", options, "Choose pickup type", 1, 1);

        await RespondAsync("📦 Select your pickup type:", components: menu.Build(), ephemeral: true);
    }

    [ComponentInteraction("pickup-mode-select")]
    public async Task HandlePickupModeSelect(string selected)
    {
        await DeferAsync(ephemeral: true); // This ensures the interaction is acknowledged

        Console.WriteLine($"Mode Selected {selected}");

        if (string.IsNullOrWhiteSpace(selected))
        {
            await FollowupAsync("❌ No pickup type was selected. Please try again.", ephemeral: true);
            return;
        }

        var allUnclaimed = (await _orderDb.GetUnpickedOrdersAsync())
            .GroupBy(o => o.Purchaser ?? "Unknown")
            .ToList();

        if (selected.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            await FinalizePickupAsync(Context.User, allUnclaimed);
        }
        else if (selected.Equals("Specific", StringComparison.OrdinalIgnoreCase))
        {
            if (allUnclaimed.Count == 0)
            {
                await FollowupAsync("🫥 No unclaimed orders available to select from.", ephemeral: true);
                return;
            }

            var options = allUnclaimed.Select(group =>
                new SelectMenuOptionBuilder(group.Key, group.Key, $"{group.Count()} order(s)")).ToList();

            var select = new ComponentBuilder()
                .WithSelectMenu("pickup-select", options, placeholder: "Select who you're picking up for", minValues: 1, maxValues: options.Count);

            await FollowupAsync("🔍 Who are you picking up for?", components: select.Build(), ephemeral: true);
        }
        else
        {
            await FollowupAsync("⚠️ Unknown pickup mode selected. Please try again.", ephemeral: true);
        }
    }

    [SlashCommand("recheck", "Force an email check")]
    public async Task ForceCheck()
    {
        await DeferAsync(ephemeral: true); // Acknowledge the interaction
        await _emailService.CheckInboxAsync();
        await FollowupAsync("✅ Initiated a re-check of the inbox.", ephemeral: true);
    }



    [ComponentInteraction("pickup-select")]
    public async Task HandlePickupSelect(string[] selectedAliases)
    {
        await DeferAsync(ephemeral: true); // Must acknowledge!

        var allUnclaimed = await _orderDb.GetUnpickedOrdersAsync();

        var grouped = allUnclaimed
            .Where(o => o.Purchaser != null && selectedAliases.Contains(o.Purchaser))
            .GroupBy(o => o.Purchaser!)
            .ToList();

        if (grouped.Count == 0)
        {
            await FollowupAsync("❌ No matching orders found for those users.", ephemeral: true);
            return;
        }

        await FinalizePickupAsync(Context.User, grouped);
    }


    private async Task FinalizePickupAsync(SocketUser requester, IEnumerable<IGrouping<string, BeerOrder>> grouped)
    {
        var dm = await requester.CreateDMChannelAsync();
        var embed = new EmbedBuilder()
            .WithTitle("📦 Pickup Details")
            .WithColor(Color.Orange);

        foreach (var group in grouped)
        {
            var content = string.Join("\n\n", group.Select(o =>
                $"`#{o.OrderNumber}`\n{string.Join("\n", o.Items.Select(i => $"• {i}"))}"));

            embed.AddField($"👤 {group.Key}", content);
        }

        await dm.SendMessageAsync(embed: embed.Build());
        await FollowupAsync("📬 Pickup details sent to your DM.", ephemeral: true);

        var summary = string.Join("\n", grouped.Select(g =>
            $"{g.Key}: {string.Join(", ", g.Select(o => o.OrderNumber))}"));

        var pickupChannelId = ulong.Parse(_config["Discord:Channels:Pickups"]);
        var pickupChannel = Context.Client.GetChannel(pickupChannelId) as IMessageChannel;

        var registeredUser = await _userService.GetUserByDiscordIdAsync((long)requester.Id);
        var pickupName = registeredUser?.Alias ?? requester.Username;

        if (pickupChannel != null)
        {
            await pickupChannel.SendMessageAsync(
                $"📦 **Pickup Summary** (requested by {pickupName})\n```{summary}```");
        }

        var allOrderNumbers = grouped.SelectMany(g => g.Select(o => o.OrderNumber)).ToList();
        await _orderDb.MarkOrdersPickedUpAsync(allOrderNumbers, pickupName);
    }
}