using System.Xml;
using System.Xml.Linq;
using LpDashboard.Application.Market;
using LpDashboard.Application.Risk;
using LpDashboard.Domain.Market;
using Microsoft.Extensions.Options;

namespace LpDashboard.Infrastructure.Market;

/// <summary>
/// Headlines from RSS 2.0 feeds (the Federal Reserve press feed by default). One feed failing does not hide the others;
/// the source fails only when none answered. Nothing is sent but the GET itself, and a failure never names the URL.
/// </summary>
internal sealed class FeedHeadlineSource(HttpClient http, IOptions<MarketSettings> settings) : IHeadlineSource
{
    public async Task<SourceOutcome<IReadOnlyList<RawHeadline>>> GetLatestAsync(CancellationToken ct)
    {
        var feeds = settings.Value.HeadlineFeeds
            .Where(f => Uri.TryCreate(f.Url, UriKind.Absolute, out _))
            .DistinctBy(f => f.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (feeds.Count == 0) return SourceOutcome<IReadOnlyList<RawHeadline>>.Fail("no feeds configured");

        var all = new List<RawHeadline>();
        var answered = false;
        foreach (var feed in feeds)
        {
            var xml = await FetchAsync(feed.Url, ct);
            if (xml is null) continue;
            var parsed = Parse(xml, feed.Name);
            if (!parsed.Answered) continue;
            answered = true;
            all.AddRange(parsed.Value!);
        }

        return answered
            ? SourceOutcome<IReadOnlyList<RawHeadline>>.Ok(all.OrderByDescending(h => h.PublishedAt).ToList())
            : SourceOutcome<IReadOnlyList<RawHeadline>>.Fail("no feed answered");
    }

    private async Task<string?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    internal static SourceOutcome<IReadOnlyList<RawHeadline>> Parse(string xml, string sourceName)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml.TrimStart('﻿'));
        }
        catch (XmlException)
        {
            return SourceOutcome<IReadOnlyList<RawHeadline>>.Fail("unreadable feed");
        }

        var items = new List<RawHeadline>();
        foreach (var item in doc.Descendants("item"))
        {
            var title = MarketText.Clean(item.Element("title")?.Value);
            var link = MarketText.Clean(item.Element("link")?.Value);
            if (title.Length == 0 || link.Length == 0) continue;
            if (!DateTimeOffset.TryParse(item.Element("pubDate")?.Value, out var published)) continue;
            items.Add(new RawHeadline(title, sourceName, link, published.ToUniversalTime(), ImageOf(item)));
        }
        return SourceOutcome<IReadOnlyList<RawHeadline>>.Ok(items.OrderByDescending(i => i.PublishedAt).ToList());
    }

    /// <summary>The picture a feed attaches to a story (media:thumbnail, media:content or an image enclosure), https only.</summary>
    private static string? ImageOf(XElement item)
    {
        foreach (var e in item.Elements())
        {
            var url = e.Name.LocalName switch
            {
                "thumbnail" => e.Attribute("url")?.Value,
                "content" when IsImage(e) => e.Attribute("url")?.Value,
                "enclosure" when IsImage(e) => e.Attribute("url")?.Value,
                _ => null,
            };
            url = url?.Trim();
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                return uri.AbsoluteUri;
        }
        return null;
    }

    private static bool IsImage(XElement e) =>
        string.Equals(e.Attribute("medium")?.Value, "image", StringComparison.OrdinalIgnoreCase)
        || (e.Attribute("type")?.Value ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
