using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Wox.Plugin;
using Wox.Plugin.Logger;
using System.IO;
using System.Text.RegularExpressions;

namespace Community.PowerToys.Run.Plugin.Shlink {

    public class ShlinkResponse
    {
        public string shortUrl { get; set; }
    }

    public class ShlinkRequest
    {
        public string longUrl { get; set; }
        public string customSlug { get; set; }
    }

    /// <summary>
    /// Main class of this plugin that implement all used interfaces.
    /// </summary>
    public class Main : IPlugin, IContextMenu, IDisposable, ISettingProvider
    {
        public static string PluginID => "4692228510C14184AD80DFA2A156EA05";

        public string Name => "Shlink";

        public string Description => "Shorten urls using Shlink";

        public IEnumerable<PluginAdditionalOption> AdditionalOptions => [
            new()
            {
                Key = nameof(ShlinkHosts),
                DisplayLabel = "Shlink hosts",
                DisplayDescription = "Hostnames of your Shlink instances (one on each line) for example: https://shlink.io/",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.MultilineTextbox,
                TextValue = ShlinkHosts,
            },
            new()
            {
                Key = nameof(ShlinkKeys),
                DisplayLabel = "Shlink API key",
                DisplayDescription = "API keys for your Shlink instances (one on each line). Make sure the key is on the same line number as the host in the field above.",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.MultilineTextbox,
                TextValue = ShlinkKeys,
            },
        ];

        private string ShlinkHosts { get; set; }
        private string ShlinkKeys { get; set; }

        private PluginInitContext Context { get; set; }

        private string IconPath { get; set; }

        private bool Disposed { get; set; }

        public List<Result> Query(Query query)
        {
            var url = "";
            // If global matching is enabled, return results if the query is a url.
            if (string.IsNullOrEmpty(query.ActionKeyword))
            {
                Match match = Regex.Match(query.Search, "^\\S+:\\/\\/\\S+$");

                if (match.Success)
                {
                    url = query.Search;
                } else
                {
                    return [];
                }
            }

            var terms = query.Terms.ToList();
            // Provide a hint to the user if they haven't entered a url yet.
            if (terms.Count == 0 && url == "")
            {
                return [
                    new Result
                    {
                        IcoPath = IconPath,
                        Title = "Create a short url",
                        QueryTextDisplay = "url [optional shortcode]",
                        SubTitle = "Enter a url (and optionally shortcode) to shorten",
                    }
                ];
            }

            // Test if the provided url is valid.
            if (terms.Count >= 1)
            {
                if (!Uri.TryCreate(terms[0], UriKind.Absolute, out _))
                {
                    return [
                        new Result
                    {
                        QueryTextDisplay = query.Search,
                        IcoPath = "Images/error.png",
                        Title = "Create a short url",
                        SubTitle = "Please enter a valid url",
                    }
                    ];
                }

                url = terms[0];
            }

            // If the user has provided a shortcode, use it.
            var shortcode = terms.Count == 2 ? terms[1] : null;
            var subtitle = "With a randomly generated shortcode";
            if (shortcode != null)
            {
                subtitle = "With shortcode: " + shortcode;
            }

            // Load the Shlink instances from the settings.
            var hosts = ShlinkHosts?.Split("\r") ?? [];
            var keys = ShlinkKeys?.Split("\r") ?? [];

            // Generate a result for each Shlink instance.
            var results = new List<Result>();
            for ( var i = 0; i < hosts.Length; i++)
            {
                var host = hosts[i];
                var key = keys[i];

                var uri = new Uri(host);

                results.Add(new Result
                {
                    QueryTextDisplay = query.Search,
                    IcoPath = IconPath,
                    Title = "Create a short url with " + uri.Host,
                    SubTitle = subtitle,
                    Action = _ => GenerateShortUrl(url, host, key, shortcode),
                });
            }

            return results;
        }

        public void Init(PluginInitContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(Context.API.GetCurrentTheme());
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            return [];
        }

        public Control CreateSettingPanel() => throw new NotImplementedException();

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            Log.Info("UpdateSettings", GetType());

            ShlinkHosts = settings.AdditionalOptions.FirstOrDefault(x => x.Key == nameof(ShlinkHosts))?.TextValue ?? "";
            ShlinkKeys = settings.AdditionalOptions.FirstOrDefault(x => x.Key == nameof(ShlinkKeys))?.TextValue ?? "";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed || !disposing)
            {
                return;
            }

            if (Context?.API != null)
            {
                Context.API.ThemeChanged -= OnThemeChanged;
            }

            Disposed = true;
        }

        private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? "Images/shlink.light.png" : "Images/shlink.dark.png";

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

        private static bool GenerateShortUrl(string value, string host, string key, string shortcode)
        {
            if (value != null)
            {
                // Send a request to the Shlink API to shorten the url.
                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, host + "/rest/v3/short-urls");
                request.Headers.Add("X-Api-Key", key);

                string jsonString = JsonSerializer.Serialize(new ShlinkRequest { longUrl = value });
                if (shortcode != null)
                {
                    jsonString = JsonSerializer.Serialize(new ShlinkRequest { longUrl = value, customSlug = shortcode });
                }

                var content = new StringContent(jsonString, null, "application/json");
                request.Content = content;

                var response = client.Send(request);

                // Read the response.
                using var reader = new StreamReader(response.Content.ReadAsStream());
                var result = reader.ReadToEnd();

                // If an error response is returned, show it to the user.
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(result, "Received an error from Shlink", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Parse the response and copy the shortened url to the clipboard.
                var shlinkResponse = JsonSerializer.Deserialize<ShlinkResponse>(result);
                var url = shlinkResponse.shortUrl;
                Clipboard.SetText(url);
            }

            return true;
        }
    }
}
