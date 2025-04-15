// /Services/Breweries/SideProjectOrderParser.cs
using HtmlAgilityPack;

namespace BeerOrderBot.Services.Breweries;
public class SideProjectOrderParser : IOrderParser
{
    public BeerOrder? Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var orderNode = doc.DocumentNode.SelectSingleNode("//span[contains(text(),'Order #')]");
        var orderNumber = orderNode?.InnerText?.Replace("Order #", "").Trim();

        var itemNodes = doc.DocumentNode.SelectNodes("//span[contains(text(),'×')]");
        var items = itemNodes?.Select(x => x.InnerText.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new();

        if (string.IsNullOrWhiteSpace(orderNumber) || items.Count == 0)
            return null;

        return new BeerOrder
        {
            OrderNumber = orderNumber,
            Items = items
        };
    }

    public string? ExtractPurchaserName(string html)
    {
        html = html.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var billingNode = doc.DocumentNode.SelectSingleNode("//h4[contains(text(),'Billing address')]");
        var parent = billingNode?.ParentNode;
        var rawText = parent?.SelectSingleNode(".//p")?.InnerText;

        return rawText?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
    }
}
