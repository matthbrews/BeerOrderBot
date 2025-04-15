// Required NuGet: Discord.Net.Interactions
// Program.cs or Startup.cs should initialize the InteractionService

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Threading.Tasks;
using BeerOrderBot.Services;
using Microsoft.Extensions.Configuration;

public class RegisterModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly UserService _userService;
    private readonly IConfiguration _config;
    private readonly EmailService _emailService;

    public RegisterModule(UserService userService, IConfiguration config, EmailService emailService)
    {
        _userService = userService;
        _config = config;
        _emailService = emailService;
    }

    [SlashCommand("register", "Register your email and name as it appears on your orders")]
    public async Task Register()
    {
        var existing = await _userService.GetUserByDiscordIdAsync((long)Context.User.Id);
        if (existing != null)
        {
            await RespondAsync("You're already registered. Use `/whoami` to check your info.", ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle("BeerBot Registration")
            .WithCustomId("register_modal")
            .AddTextInput("Email Address", "email_input", TextInputStyle.Short, placeholder: "you@example.com", required: true)
            .AddTextInput("Name on Orders", "name_input", TextInputStyle.Short, required: true)
            .Build();

        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("register_modal")]
    public async Task HandleRegisterModal(RegisterModal modalData)
    {
        await DeferAsync(ephemeral: true);

        var user = new RegisteredUser
        {
            DiscordUserId = (long)Context.User.Id,
            DisplayName = Context.User.Username,
            Email = modalData.Email,
            Alias = string.IsNullOrWhiteSpace(modalData.Alias) ? null : modalData.Alias
        };

        await _userService.RegisterUserAsync(user);
        await FollowupAsync(
            $"✅ Registered **{user.DisplayName}** with email `{user.Email}`" +
            (user.Alias != null ? $" (Name on Order: `{user.Alias}`)" : "") +
            "\n📬 DM sent to select your group(s).",
            ephemeral: true
        );


        var allowedRoles = _config.GetSection("Discord:AllowedRoles").Get<Dictionary<string, string>>();
        var roleOptions = allowedRoles.Keys.Select(roleName =>
            new SelectMenuOptionBuilder(roleName, roleName, $"Assign yourself to the {roleName} group")).ToList();

        var component = new ComponentBuilder()
            .WithSelectMenu("select-roles", roleOptions, "Select your Group(s)", 1, roleOptions.Count);

        try
        {
            var dm = await Context.User.CreateDMChannelAsync();
            await dm.SendMessageAsync("👥 Choose your group(s):", components: component.Build());
        }
        catch
        {
            await FollowupAsync("❌ Couldn't send DM. Please enable DMs from server members.", ephemeral: true);
        }


    }

    [ComponentInteraction("select-roles")]
    public async Task HandleRoleSelection(string[] selectedRoles)
    {
        var allowedRoles = _config.GetSection("Discord:AllowedRoles").Get<Dictionary<string, string>>();

        // Manually get the Guild and User since this came from a DM
        var guildId = ulong.Parse(_config["Discord:TestGuildId"]);
        var guild = (Context.Client as DiscordSocketClient)?.GetGuild(guildId);
        if (guild == null)
        {
            await RespondAsync("❌ Could not find server context.", ephemeral: true);
            return;
        }

        var user = guild.GetUser(Context.User.Id);
        if (user == null)
        {
            await RespondAsync("❌ Could not find your user in the server.", ephemeral: true);
            return;
        }

        foreach (var roleKey in selectedRoles)
        {
            if (allowedRoles.TryGetValue(roleKey, out var roleIdStr) && ulong.TryParse(roleIdStr, out var roleId))
            {
                var role = guild.GetRole(roleId);
                if (role != null)
                    await user.AddRoleAsync(role);
            }
        }

        await RespondAsync("✅ Roles assigned!", ephemeral: true);
    }



}

public class RegisterModal : IModal
{
    public string Title => "BeerBot Registration";

    [InputLabel("Email Address")]
    [ModalTextInput("email_input", TextInputStyle.Short, placeholder: "you@example.com")]
    public string Email { get; set; }

    [InputLabel("Name On Orders")]
    [ModalTextInput("name_input", TextInputStyle.Short)]
    public string Alias { get; set; }
}

// Make sure to add this module in your bot startup using:
// await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);