using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Gazelle
{
    public class GazelleParser : IParseIndexerResponse
    {
        private readonly GazelleSettings _settings;
        public ICached<Dictionary<string, string>> AuthCookieCache { get; set; }

        public GazelleParser(GazelleSettings settings)
        {
            _settings = settings;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                // Remove cookie cache
                AuthCookieCache.Remove(_settings.BaseUrl.Trim().TrimEnd('/'));

                throw new IndexerException(indexerResponse, $"Unexpected response status {indexerResponse.HttpResponse.StatusCode} code from API request");
            }

            if (!indexerResponse.HttpResponse.Headers.ContentType.Contains(HttpAccept.Json.Value))
            {
                // Remove cookie cache
                AuthCookieCache.Remove(_settings.BaseUrl.Trim().TrimEnd('/'));

                throw new IndexerException(indexerResponse, $"Unexpected response header {indexerResponse.HttpResponse.Headers.ContentType} from API request, expected {HttpAccept.Json.Value}");
            }

            var jsonResponse = new HttpResponse<GazelleResponse>(indexerResponse.HttpResponse);
            if (jsonResponse.Resource.Status != "success" ||
                jsonResponse.Resource.Status.IsNullOrWhiteSpace() ||
                jsonResponse.Resource.Response == null)
            {
                return torrentInfos;
            }

            foreach (var result in jsonResponse.Resource.Response.Results)
            {
                if (result.Torrents != null)
                {
                    foreach (var torrent in result.Torrents)
                    {
                        var id = torrent.TorrentId;
                        var author = WebUtility.HtmlDecode(result.Author);
                        var book = WebUtility.HtmlDecode(result.GroupName);

                        torrentInfos.Add(new GazelleInfo()
                        {
                            Guid = string.Format("Gazelle-{0}", id),
                            Author = author,

                            // Splice Title from info to avoid calling API again for every torrent.
                            Title = WebUtility.HtmlDecode(result.Author + " - " + result.GroupName + " (" + result.GroupYear + ") [" + torrent.Format + " " + torrent.Encoding + "]"),
                            Book = book,
                            Container = torrent.Encoding,
                            Codec = torrent.Format,
                            Size = long.Parse(torrent.Size),
                            DownloadUrl = GetDownloadUrl(id),
                            InfoUrl = GetInfoUrl(result.GroupId, id),
                            Seeders = int.Parse(torrent.Seeders),
                            Peers = int.Parse(torrent.Leechers) + int.Parse(torrent.Seeders),
                            PublishDate = torrent.Time.ToUniversalTime(),
                            Scene = torrent.Scene,
                        });
                    }
                }
            }

            var torr = torrentInfos;

            // order by date
            return
                torrentInfos
                    .OrderByDescending(o => o.PublishDate)
                    .ToArray();
        }

        private string GetDownloadUrl(int torrentId)
        {
            var url = new HttpUri(_settings.BaseUrl)
                .CombinePath("/torrents.php")
                .AddQueryParam("action", "download")
                .AddQueryParam("id", torrentId)
                .AddQueryParam("authkey", _settings.AuthKey)
                .AddQueryParam("torrent_pass", _settings.PassKey);

            // Some trackers fail to download if usetoken=0 so we need to only add if we will use one
            if (_settings.UseFreeleechToken)
            {
                url = url.AddQueryParam("usetoken", "1");
            }

            return url.FullUri;
        }

        private string GetInfoUrl(string groupId, int torrentId)
        {
            var url = new HttpUri(_settings.BaseUrl)
                .CombinePath("/torrents.php")
                .AddQueryParam("id", groupId)
                .AddQueryParam("torrentid", torrentId);

            return url.FullUri;
        }
    }
}
