using HtmlAgilityPack;
using System.Text.RegularExpressions;
using BeerOrderBot.Services.Breweries;

public interface IOrderParser
{
    BeerOrder? Parse(string html);
    string? ExtractPurchaserName(string html);
}

public class OrderParserService
{
    public IOrderParser? GetParserForSender(string senderEmail)
    {
        return senderEmail.ToLowerInvariant() switch
        {
            "orders@sideprojectbrewing.com" => new SideProjectOrderParser(),
            _ => null
        };
    }
           
    public string? ExtractForwardedSender(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html); 

        var fromNodes = doc.DocumentNode.SelectNodes("//div[contains(text(), 'From:')]");
        if (fromNodes != null)
        {
            foreach (var node in fromNodes.Reverse())
            {
                var match = Regex.Match(node.InnerText, @"<(.+?@.+?)>");
                if (match.Success)
                {
                    var email = match.Groups[1].Value.Trim();
                    Console.WriteLine($"🔍 Candidate sender: {email}");
                    return email;
                }
            }
        }
        var mailto = doc.DocumentNode.SelectSingleNode("//a[starts-with(@href, 'mailto:')]");
        if (mailto != null)
        {
            var href = mailto.GetAttributeValue("href", "");
            return href.Replace("mailto:", "").Split('?')[0];
        }

        return null;
    }

    public string? ExtractOriginalToRecipient(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        // Load the HTML content into HtmlAgilityPack
        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        // Extract the plain text from the HTML
        var textContent = doc.DocumentNode.InnerText;

        // Decode HTML entities to get the actual characters
        var decodedText = HtmlEntity.DeEntitize(textContent);

        // Define regex patterns to match 'To:' lines with or without angle brackets
        var patterns = new[]
        {
        @"To:\s*<(?<email>[^>]+)>",      // Matches 'To: <email@example.com>'
        @"To:\s*(?<email>\S+@\S+)"       // Matches 'To: email@example.com'
    };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(decodedText, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var email = match.Groups["email"].Value.Trim();
                Console.WriteLine($"📩 Extracted original To: {email}");
                return email.ToLowerInvariant();
            }
        }

        Console.WriteLine("⚠️ No 'To:' line found in the forwarded email.");
        return null;
    }


}

public static class BreweryResolver
{
    public static string? GetBreweryFromEmail(string sender)
    {
        var normalized = sender.ToLowerInvariant();

        return normalized switch
        {
            "orders@sideprojectbrewing.com" => "Side Project",
            "orders@otherhalfbrewing.com" => "Other Half",
            "orders@weldwerks.com" => "WeldWerks",
            "notifications@oznr.com" => "Oznr",
            _ => null // unknown or not supported
        };
    }
}

public class BeerOrder
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; }
    public List<string> Items { get; set; } = new();
    public string? Purchaser { get; set; }
    public string Brewery { get; set; }
    public bool IsPickedUp { get; set; }
    public bool IsReceived { get; set; }
    public string? PickedUpBy { get; set; }
}
