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
using Windows.UI.ViewManagement;

namespace Community.PowerToys.Run.Plugin.Shlink {

    public class ShlinkResponse
    {
        public string shortUrl { get; set; }
    }

    public class ShlinkRequest
    {
        public string[] tags { get; set; }
        public string longUrl { get; set; }
        public string customSlug { get; set; }
        public string title { get; set; }
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
            new()
            {
                Key = nameof(ShlinkTags),
                DisplayLabel = "Shlink Tags",
                DisplayDescription = "Tags that will be added to each url shortened (one on each line).",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.MultilineTextbox,
                TextValue = ShlinkTags,
            },
        ];

        private string ShlinkHosts { get; set; }
        private string ShlinkKeys { get; set; }
        private string ShlinkTags { get; set; }

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
                        QueryTextDisplay = "url [optional shortcode] [optional title]",
                        SubTitle = "Enter a url (and optionally shortcode or title) to shorten",
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

            // If the user has provided a shortcode or title, use it.
            var shortcode = terms.Count >= 2 ? terms[1] : null;
            var title = terms.Count == 3 ? terms[2] : null;
            var subtitle = "With a randomly generated shortcode";
            if ((shortcode != null) && (title != null))
            {
                subtitle = "With shortcode: " + shortcode + " and title: " + title;
            } 
            else if (shortcode != null)
            {
                subtitle = "With shortcode: " + shortcode;
            }

            // Load the Shlink instances from the settings.
            var hosts = ShlinkHosts != "" ? ShlinkHosts.Split("\r") : [];
            var keys = ShlinkKeys != "" ? ShlinkKeys.Split("\r") : [];
            var tags = ShlinkTags != "" ? ShlinkTags.Split("\r") : [];

            // If no Shlink instances are configured, show an error message.
            if (hosts.Length == 0)
            {
                return [
                    new Result
                    {
                        QueryTextDisplay = query.Search,
                        IcoPath = "Images/error.png",
                        Title = "No Shlink instances configured",
                        SubTitle = "Please configure the Shlink hosts and API keys in the settings",
                    }
                ];
            }

            // If the number of hosts and keys don't match, show an error message.
            if (hosts.Length != keys.Length)
            {
                return [
                    new Result
                    {
                        QueryTextDisplay = query.Search,
                        IcoPath = "Images/error.png",
                        Title = "Mismatched number of hosts and keys",
                        SubTitle = "Please make sure the number of hosts and keys match",
                    }
                ];
            }

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
                    Action = _ => GenerateShortUrl(url, host, key, tags, shortcode, title),
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
            ShlinkTags = settings.AdditionalOptions.FirstOrDefault(x => x.Key == nameof(ShlinkTags))?.TextValue ?? "";
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

        private static bool GenerateShortUrl(string value, string host, string key, string[] tags, string shortcode, string title)
        {
            if (value != null)
            {
                // Send a request to the Shlink API to shorten the url.
                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, host + "/rest/v3/short-urls");
                request.Headers.Add("X-Api-Key", key);

                string jsonString = JsonSerializer.Serialize(new ShlinkRequest { tags = tags, longUrl = value });
                if (shortcode != null && title != null)
                {
                    jsonString = JsonSerializer.Serialize(new ShlinkRequest { tags = tags, longUrl = value, customSlug = shortcode, title = title });
                } 
                else if (shortcode != null)
                {
                    jsonString = JsonSerializer.Serialize(new ShlinkRequest { tags = tags, longUrl = value, customSlug = shortcode });
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
