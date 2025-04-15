// /Services/UserService.cs
using System.Data;
using Dapper;
using Npgsql;
using BeerOrderBot.Services;
using Microsoft.Extensions.Configuration;

namespace BeerOrderBot.Services;

public class UserService
{
    private readonly string _connectionString;
    private readonly Lazy<EmailService> _emailService;

    public UserService(IConfiguration config, Lazy<EmailService> emailService)
    {
        _connectionString = config.GetConnectionString("Postgres");
        _emailService = emailService;
    }

    public async Task<bool> IsEmailRegisteredAsync(string email)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM RegisteredUsers WHERE Email = @email)",
            new { email });
        return exists;
    }

    public async Task RegisterUserAsync(RegisteredUser user)
    {
        var existing = await GetUserByEmailAsync(user.Email);
        if (existing != null && existing.DiscordUserId != user.DiscordUserId)
            throw new InvalidOperationException("That email is already registered to another user.");

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync(@"
            INSERT INTO RegisteredUsers (DiscordUserId, DisplayName, Email, Alias)
            VALUES (@DiscordUserId, @DisplayName, @Email, @Alias)
            ON CONFLICT (DiscordUserId) DO UPDATE 
            SET DisplayName = @DisplayName, Email = @Email, Alias = @Alias;
        ", user);

        // Check any previously unregistered emails
        await _emailService.Value.RecheckUnregisteredAsync();
    }

    public async Task<RegisteredUser?> GetUserByEmailAsync(string email)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<RegisteredUser>(
            "SELECT * FROM RegisteredUsers WHERE Email = @email",
            new { email });
    }

    public async Task<RegisteredUser?> GetUserByDiscordIdAsync(long discordId)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryFirstOrDefaultAsync<RegisteredUser>(
            "SELECT * FROM RegisteredUsers WHERE DiscordUserId = @discordId",
            new { discordId });
    }

    public async Task<IEnumerable<RegisteredUser>> GetAllUsersAsync()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        return await conn.QueryAsync<RegisteredUser>("SELECT * FROM RegisteredUsers");
    }
}

public class RegisteredUser
{
    public long DiscordUserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string? Alias { get; set; }
}
