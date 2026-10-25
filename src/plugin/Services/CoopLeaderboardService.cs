using Assets.Scripts.Game.Other;
using MegabonkTogether.Configuration;
using MegabonkTogether.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MegabonkTogether.Services
{
    /// <summary>
    /// Co-op leaderboard (the game's own leaderboards do not take netplay runs): the host submits each finished run
    /// (src/leaderboard). A board is a map, a tier and a party size; each party keeps its best run (total kills).
    /// </summary>
    public interface ICoopLeaderboardService
    {
        public void OnRunStarted(RunConfig runConfig);
        public void SubmitRun();
        public Task<CoopLeaderboardPage> GetPage(string map, int tier, int partySize, bool friendsOnly, int offset, int limit);
        public void Reset();
    }

    public class CoopLeaderboardPlayer
    {
        [JsonPropertyName("steamId")] public string SteamId { get; set; }
        [JsonPropertyName("ticket"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Ticket { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("steamName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string SteamName { get; set; }
        [JsonPropertyName("character")] public string Character { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
    }

    public class CoopLeaderboardEntry
    {
        [JsonPropertyName("rank")] public int Rank { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("modifiers")] public int Modifiers { get; set; }
        [JsonPropertyName("challenge")] public string Challenge { get; set; }
        [JsonPropertyName("at")] public long At { get; set; }
        [JsonPropertyName("players")] public List<CoopLeaderboardPlayer> Players { get; set; } = [];
    }

    public class CoopLeaderboardPage
    {
        [JsonPropertyName("entries")] public List<CoopLeaderboardEntry> Entries { get; set; } = [];
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    internal class CoopLeaderboardService : ICoopLeaderboardService
    {
        public static readonly string[] Maps = ["Forest", "Desert", "Hell", "Graveyard"];
        public const int MAX_TIER = 3;
        public const int MIN_PARTY = 2;
        public const int MAX_PARTY = 6;

        private class RunSubmission
        {
            [JsonPropertyName("map")] public string Map { get; set; }
            [JsonPropertyName("tier")] public int Tier { get; set; }
            [JsonPropertyName("modifiers")] public int Modifiers { get; set; }
            [JsonPropertyName("challenge")] public string Challenge { get; set; }
            [JsonPropertyName("modVersion")] public string ModVersion { get; set; }
            [JsonPropertyName("players")] public List<CoopLeaderboardPlayer> Players { get; set; }
        }

        private class FriendsQuery
        {
            [JsonPropertyName("map")] public string Map { get; set; }
            [JsonPropertyName("tier")] public int Tier { get; set; }
            [JsonPropertyName("size")] public int Size { get; set; }
            [JsonPropertyName("offset")] public int Offset { get; set; }
            [JsonPropertyName("limit")] public int Limit { get; set; }
            [JsonPropertyName("steamIds")] public List<string> SteamIds { get; set; }
        }

        private class SubmitResponse
        {
            [JsonPropertyName("kills")] public int Kills { get; set; }
            [JsonPropertyName("bestKills")] public int BestKills { get; set; }
            [JsonPropertyName("rank")] public int Rank { get; set; }
        }

        private static readonly int[] SUBMIT_RETRY_DELAYS_MS = [0, 5000, 20000];

        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static CoopLeaderboardService()
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MegaphonkMod-Leaderboard");
        }

        private readonly IUdpClientService udpClientService;
        private readonly IPlayerManagerService playerManagerService;
        private readonly IRunStatsService runStatsService;
        private readonly IPartySettingsService partySettingsService;

        private string map;
        private int tier;
        private string challenge = "";
        private bool submitted;

        public CoopLeaderboardService(IUdpClientService udpClientService, IPlayerManagerService playerManagerService, IRunStatsService runStatsService,
            IPartySettingsService partySettingsService)
        {
            this.udpClientService = udpClientService;
            this.playerManagerService = playerManagerService;
            this.runStatsService = runStatsService;
            this.partySettingsService = partySettingsService;
        }

        /// <summary>
        /// Host: the map and tier the run started on (the first map of the session, later stages keep them)
        /// </summary>
        public void OnRunStarted(RunConfig runConfig)
        {
            if (map != null || runConfig?.mapData == null)
            {
                return;
            }

            map = runConfig.mapData.eMap.GetMapName();
            tier = runConfig.stageData != null ? runConfig.mapData.stages.IndexOf(runConfig.stageData) + 1 : 0;
            challenge = runConfig.challenge?.name ?? "";
        }

        /// <summary>
        /// Host, at the end of the run (where the game uploads to its leaderboards): once per run
        /// </summary>
        public void SubmitRun()
        {
            if (submitted || !(udpClientService.IsHost() ?? false) || !ModConfig.SubmitCoopRuns.Value)
            {
                return;
            }
            submitted = true;

            try
            {
                var players = playerManagerService.GetAllPlayers().Select(player => new CoopLeaderboardPlayer
                {
                    SteamId = runStatsService.GetSteamId(player.ConnectionId).ToString(),
                    Ticket = runStatsService.GetSteamTicket(player.ConnectionId),
                    Name = player.Name,
                    Character = ((ECharacter)player.Character).ToString(),
                    Kills = runStatsService.GetKills(player.ConnectionId),
                }).ToList();

                // Every player proves its Steam id with its ticket (checked by the leaderboard with Steam)
                var unverified = players.Where(p => p.SteamId == "0" || string.IsNullOrEmpty(p.Ticket)).Select(p => p.Name).ToList();
                if (!Maps.Contains(map) || tier < 1 || tier > MAX_TIER || players.Count < MIN_PARTY || players.Count > MAX_PARTY || unverified.Count > 0)
                {
                    Plugin.Log.LogInfo($"Co-op run not submitted to the leaderboard (map {map}, tier {tier}, {players.Count} players, no Steam ticket: {string.Join(", ", unverified)})");
                    return;
                }

                var submission = new RunSubmission
                {
                    Map = map,
                    Tier = tier,
                    Modifiers = (int)partySettingsService.Current.RunModifiers,
                    Challenge = challenge,
                    ModVersion = MyPluginInfo.PLUGIN_VERSION,
                    Players = players,
                };

                Task.Run(async () =>
                {
                    // Retried when the leaderboard or Steam could not answer, not when the run was refused
                    for (var attempt = 0; attempt < SUBMIT_RETRY_DELAYS_MS.Length; attempt++)
                    {
                        await Task.Delay(SUBMIT_RETRY_DELAYS_MS[attempt]);
                        try
                        {
                            var result = await PostAsync<SubmitResponse>("/v1/runs", submission);
                            Plugin.Log.LogInfo($"Co-op run submitted to the leaderboard: {result?.Kills} kills (party best {result?.BestKills}, rank {result?.Rank}) ({submission.Map} tier {submission.Tier}, {players.Count} players)");
                            return;
                        }
                        catch (HttpRequestException ex) when (ex.StatusCode is not null && (int)ex.StatusCode < 500 && (int)ex.StatusCode != 429)
                        {
                            Plugin.Log.LogWarning($"Co-op run refused by the leaderboard: {ex.Message}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"Could not submit the co-op run to the leaderboard (attempt {attempt + 1}): {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Could not build the co-op leaderboard run: {ex}");
            }
        }

        public async Task<CoopLeaderboardPage> GetPage(string map, int tier, int partySize, bool friendsOnly, int offset, int limit)
        {
            CoopLeaderboardPage response;
            if (friendsOnly)
            {
                var steamIds = Steam.SteamApi.Initialize()
                    ? Steam.SteamApi.GetFriendSteamIds().Prepend(Steam.SteamApi.GetLocalSteamId()).Select(id => id.ToString()).ToList()
                    : [];
                response = await PostAsync<CoopLeaderboardPage>("/v1/top/friends", new FriendsQuery { Map = map, Tier = tier, Size = partySize, Offset = offset, Limit = limit, SteamIds = steamIds });
            }
            else
            {
                using var httpResponse = await httpClient.GetAsync($"{BaseUrl()}/v1/top?map={Uri.EscapeDataString(map)}&tier={tier}&size={partySize}&offset={offset}&limit={limit}");
                httpResponse.EnsureSuccessStatusCode();
                response = JsonSerializer.Deserialize<CoopLeaderboardPage>(await httpResponse.Content.ReadAsStringAsync());
            }

            return response ?? new CoopLeaderboardPage();
        }

        private static async Task<T> PostAsync<T>(string path, object body)
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(BaseUrl() + path, content);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{(int)response.StatusCode}: {text}", null, response.StatusCode);
            }

            return JsonSerializer.Deserialize<T>(text);
        }

        private static string BaseUrl()
        {
            return ModConfig.LeaderboardUrl.Value.TrimEnd('/');
        }

        public void Reset()
        {
            map = null;
            tier = 0;
            challenge = "";
            submitted = false;
        }
    }
}
