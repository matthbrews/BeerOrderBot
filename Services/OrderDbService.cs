using Dapper;
using Npgsql;
using BeerOrderBot.Services.Breweries;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Data;

namespace BeerOrderBot.Services;

public class OrderDbService
{
    private readonly string _connectionString;
    private static bool _handlerRegistered = false;

    public OrderDbService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Postgres");

        // ✅ Register JSON list handler only once
        if (!_handlerRegistered)
        {
            SqlMapper.AddTypeHandler(new JsonListHandler());
            _handlerRegistered = true;
        }
    }

    public async Task<bool> OrderExistsAsync(string orderNumber)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM BeerOrders WHERE OrderNumber = @orderNumber",
            new { orderNumber });

        return count > 0;
    }

    public async Task SaveOrderAsync(BeerOrder order)
    {
        using var conn = new NpgsqlConnection(_connectionString);

        await conn.ExecuteAsync(@"
            INSERT INTO BeerOrders (Id, OrderNumber, Brewery, Purchaser, Items, IsPickedUp)
            VALUES (@Id, @OrderNumber, @Brewery, @Purchaser, @Items::jsonb, @IsPickedUp)
            ON CONFLICT (OrderNumber) DO NOTHING;
        ", new
        {
            order.Id,
            order.OrderNumber,
            order.Brewery,
            order.Purchaser,
            Items = JsonSerializer.Serialize(order.Items), // ← Stored as JSON string
            order.IsPickedUp
        });
    }

    public async Task<List<BeerOrder>> GetUnpickedOrdersAsync(List<string>? filterByPurchasers = null)
    {
        Console.WriteLine("🔥 Running GetUnpickedOrdersAsync");

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
        SELECT * FROM BeerOrders
        WHERE NOT IsPickedUp
        AND Purchaser IS NOT NULL";

        if (filterByPurchasers != null && filterByPurchasers.Count > 0)
        {
            sql += " AND Purchaser = ANY(@Purchasers)";
            Console.WriteLine($"✅ Applying purchaser filter: {string.Join(", ", filterByPurchasers)}");
        }
        else
        {
            Console.WriteLine("⚠️ No purchaser filter applied");
        }

        Console.WriteLine($"🔍 Final SQL:\n{sql}");

        try
        {
            var orders = (await conn.QueryAsync<BeerOrder>(sql, new
            {
                Purchasers = filterByPurchasers?.ToArray()
            })).ToList();

            Console.WriteLine($"📦 Query returned {orders.Count} unpicked orders");

            foreach (var order in orders)
            {
                Console.WriteLine($"➡️ Order #{order.OrderNumber} for {order.Purchaser}");
            }

            return orders;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Query failed: {ex.Message}");
            return new List<BeerOrder>();
        }
    }
    public async Task MarkOrdersPickedUpAsync(IEnumerable<string> orderNumbers, string pickedUpBy)
    {
        using var conn = new NpgsqlConnection(_connectionString);
        var result = await conn.ExecuteAsync(@"
        UPDATE BeerOrders
        SET IsPickedUp = TRUE,
            PickedUpBy = @PickedUpBy
        WHERE OrderNumber = ANY(@OrderNumbers);
    ", new
        {
            OrderNumbers = orderNumbers.ToArray(),
            PickedUpBy = pickedUpBy
        });

        Console.WriteLine($"✅ Marked {result} order(s) as picked up by {pickedUpBy}.");
    }


}

public class JsonListHandler : SqlMapper.TypeHandler<List<string>>
{
    public override List<string> Parse(object value)
    {
        if (value is string str)
            return JsonSerializer.Deserialize<List<string>>(str) ?? new();
        return new List<string>();
    }

    public override void SetValue(IDbDataParameter parameter, List<string> value)
    {
        parameter.Value = JsonSerializer.Serialize(value);
        parameter.DbType = DbType.String;
    }
}
