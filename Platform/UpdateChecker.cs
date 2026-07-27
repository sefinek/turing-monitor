using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuringMonitor.Logging;

namespace TuringMonitor.Platform;

public sealed record UpdateInfo(string CurrentVersion, string LatestVersion, string ReleaseUrl);

public static class UpdateChecker
{
	private const string LatestReleaseApiUrl = "https://api.github.com/repos/sefinek/TuringMonitor/releases/latest";

	private static readonly HttpClient Http = CreateClient();

	public static async Task<UpdateInfo?> CheckAsync(string currentVersion)
	{
		UpdateInfo? update;
		try
		{
			update = await FetchLatestAsync(currentVersion);
		}
		catch (Exception ex)
		{
			AppLog.Warn($"Update check failed: {ex.Message}");
			return null;
		}

		AppLog.Info(update is null
			? "Turing Monitor is up to date"
			: $"Update available: {update.CurrentVersion} -> {update.LatestVersion}");
		return update;
	}

	private static async Task<UpdateInfo?> FetchLatestAsync(string currentVersion)
	{
		using HttpResponseMessage response = await Http.GetAsync(LatestReleaseApiUrl);
		if (!response.IsSuccessStatusCode)
			return null;

		await using Stream stream = await response.Content.ReadAsStreamAsync();
		var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream);
		if (string.IsNullOrWhiteSpace(release?.TagName))
			return null;

		var latest = release.TagName.TrimStart('v', 'V');
		var current = currentVersion.TrimStart('v', 'V');

		if (!IsNewer(latest, current))
			return null;

		var url = string.IsNullOrWhiteSpace(release.HtmlUrl)
			? "https://github.com/sefinek/TuringMonitor/releases/latest"
			: release.HtmlUrl;

		return new UpdateInfo(current, latest, url);
	}

	private static bool IsNewer(string latest, string current)
	{
		var latestParts = ParseParts(latest);
		var currentParts = ParseParts(current);
		var length = Math.Max(latestParts.Length, currentParts.Length);

		for (var i = 0; i < length; i++)
		{
			var l = i < latestParts.Length ? latestParts[i] : 0;
			var c = i < currentParts.Length ? currentParts[i] : 0;
			if (l != c)
				return l > c;
		}

		return false;
	}

	private static int[] ParseParts(string version)
	{
		var core = version.Split('-', '+')[0];
		return core.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
	}

	private static HttpClient CreateClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd("TuringMonitor");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return client;
	}

	private sealed class GitHubRelease
	{
		[JsonPropertyName("tag_name")] public string? TagName { get; set; }

		[JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
	}
}
