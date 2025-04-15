// Partial `EmailService` - Refactored `CheckInboxAsync`
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using BeerOrderBot.Services.Breweries;
using BeerOrderBot.Services;
using MailKit;
using Discord.Interactions;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly DiscordService _discord;
    private readonly OrderDbService _db;
    private readonly UserService _userService;

    public EmailService(IConfiguration config, DiscordService discord, OrderDbService db, UserService userService)
    {
        _config = config;
        _discord = discord;
        _db = db;
        _userService = userService;
    }

    public async Task CheckInboxAsync()
    {
        using var client = new ImapClient();

        var email = _config["Email:Address"];
        var password = _config["Email:AppPassword"];
        var server = _config["Email:ImapServer"];
        var port = int.Parse(_config["Email:ImapPort"]);

        await client.ConnectAsync(server, port, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(email, password);

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadWrite);

        var uids = await inbox.SearchAsync(SearchQuery.Not(SearchQuery.HasGMailLabel("processed")));
        Console.WriteLine($"🔍 Found {uids.Count} unseen message(s).");

        foreach (var uid in uids)
        {
            var message = await inbox.GetMessageAsync(uid);
            var dispatcher = new OrderParserService();

            // Extract the original recipient from the forwarded email
            var recipientEmail = dispatcher.ExtractOriginalToRecipient(message.HtmlBody ?? message.TextBody ?? "");

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                Console.WriteLine("⚠️ Could not determine original recipient. Labeling as unregistered.");
                await inbox.AddLabelsAsync(uid, new[] { "unregistered" }, true);
                continue;
            }

            recipientEmail = recipientEmail.ToLowerInvariant();
            var isRegistered = await _userService.IsEmailRegisteredAsync(recipientEmail);

            if (!isRegistered)
            {
                await inbox.AddLabelsAsync(uid, new[] { "unregistered" }, true);
                Console.WriteLine($"⚠️ Unregistered recipient: {recipientEmail} → Label applied.");
                continue;
            }

            var senderEmail = dispatcher.ExtractForwardedSender(message.HtmlBody);
            var parser = dispatcher.GetParserForSender(senderEmail ?? "");

            if (parser == null)
            {
                Console.WriteLine($"❌ Unknown sender: {senderEmail}");
                continue;
            }

            var order = parser.Parse(message.HtmlBody);
            if (order == null)
            {
                Console.WriteLine("⚠️ Could not parse order.");
                continue;
            }

            order.Purchaser = parser.ExtractPurchaserName(message.HtmlBody);
            order.Brewery = BreweryResolver.GetBreweryFromEmail(senderEmail ?? "Unknown");

            if (!await _db.OrderExistsAsync(order.OrderNumber))
            {
                await inbox.AddLabelsAsync(uid, new[] { "processed" }, true);
                order.Id = Guid.NewGuid();
                await _db.SaveOrderAsync(order);
                await _discord.PostBeerOrderAsync(order);
                Console.WriteLine($"✅ Saved and posted Order #{order.OrderNumber}");
            }
            else
            {
                Console.WriteLine($"⏭️ Duplicate order #{order.OrderNumber}, skipping.");
            }
        }

        await client.DisconnectAsync(true);
    }

    public async Task RecheckUnregisteredAsync()
    {
        using var client = new ImapClient();

        await client.ConnectAsync(_config["Email:ImapServer"], int.Parse(_config["Email:ImapPort"]), SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(_config["Email:Address"], _config["Email:AppPassword"]);

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadWrite);

        var uids = await inbox.SearchAsync(SearchQuery.HasGMailLabel("unregistered"));
        Console.WriteLine($"🔁 Rechecking {uids.Count} unregistered emails.");

        foreach (var uid in uids)
        {
            var message = await inbox.GetMessageAsync(uid);
            var recipientEmail = message.To.Mailboxes.FirstOrDefault()?.Address.ToLowerInvariant();

            var isRegistered = recipientEmail != null && await _userService.IsEmailRegisteredAsync(recipientEmail);
            if (isRegistered)
            {
                await inbox.RemoveLabelsAsync(new[] { uid }, new[] { "unregistered" }, true);
                Console.WriteLine($"🔄 Recipient now registered: {recipientEmail} → Label removed.");
                
                // Optional: re-process the message
            }
        }

        await client.DisconnectAsync(true);
        await CheckInboxAsync();
    }
}
