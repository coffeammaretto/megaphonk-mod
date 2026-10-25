using System.Collections.Generic;
using System.Linq;
using Megaphonk.Helpers;

namespace Megaphonk.Services
{
    public interface ILocalizationService
    {
        public string GetCustomLocalizedString(string category, string key);
        public void EnqueueNextLocalizedDescription(IEnumerable<string> descriptionArgs);

        public string GetNextCustomLocalizedDescription(string category, string key);
    }
    public class LocalizationService : ILocalizationService
    {
        public readonly Dictionary<(string, string), string> LocalizationOverrides = new()
        {
            { ("Megaphonk", "MatchSuccess"), "Match" },
            { ("Megaphonk", "MatchSuccessDesc"), "Joined a lobby of {1} players as {0}!" },
            { ("Megaphonk", "PlayerDisconnected"), "Disconnected" },
            { ("Megaphonk", "PlayerDisconnected_Description"), "Player {0} disconnected" },
            { ("Megaphonk", "AllPlayerDisconnected"), "Disconnected" },
            { ("Megaphonk", "AllPlayerDisconnected_Description"), "All players left!" },
            { ("Megaphonk", "HostDisconnected"), "Disconnected" },
            { ("Megaphonk", "HostDisconnected_Description"), "Host left!" },
            { ("Megaphonk", "ClientDisconnected"), "Disconnected" },
            { ("Megaphonk", "ClientDisconnected_Description"), "Client disconnected" },
            { ("Megaphonk", "FriendliesHostSuccess"), "Friendlies" },
            { ("Megaphonk", "FriendliesHostSuccessDesc"), "Hosting a friendly lobby" },
            { ("Megaphonk", "FriendliesClientSuccess"), "Friendlies" },
            { ("Megaphonk", "FriendliesClientSuccessDesc"), "Joined the lobby" },
            { ("Megaphonk", "FriendliesClientJoinSuccess"), "Friendlies" },
            { ("Megaphonk", "FriendliesClientJoinSuccessDesc"), "{0} joined" },
            { ("Megaphonk", "VersionMismatch"), "Mod version mismatch" },
            { ("Megaphonk", "VersionMismatch_Description"), "{0} has {1}, you have {2}. Install the same build on every PC." },
            { ("Megaphonk", "VersionMissing_Description"), "{0} has an older build of the mod. Install the same build on every PC." },
            { ("Megaphonk", "ItemGiven"), "Gift" },
            { ("Megaphonk", "ItemGiven_Description"), "{0} gave you {1}" },
            { ("Megaphonk", "RunModifiers"), "Run modifiers" },
            { ("Megaphonk", "RunModifiers_Description"), "{0}" },
            { ("Megaphonk", "Challenge"), "Coop challenge" },
            { ("Megaphonk", "Challenge_Description"), "{0}" },
        };

        private readonly Queue<IEnumerable<string>> NextLocalizedDescription = new();

        public string GetCustomLocalizedString(string category, string key)
        {
            if (LocalizationOverrides.TryGetValue((category, key), out var localizedValue))
            {
                return Helpers.Loc.T(localizedValue);
            }
            return string.Empty;
        }

        public void EnqueueNextLocalizedDescription(IEnumerable<string> descriptionArgs)
        {
            NextLocalizedDescription.Enqueue(descriptionArgs);
        }

        public string GetNextCustomLocalizedDescription(string category, string key)
        {
            var customString = GetCustomLocalizedString(category, key);
            if (string.IsNullOrEmpty(customString))
            {
                return "";
            }

            if (NextLocalizedDescription.Count > 0)
            {
                var args = NextLocalizedDescription.Dequeue();
                try
                {
                    return string.Format(customString, args.ToArray());
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"Error formatting localized string: {ex.Message}");
                    return customString;
                }
            }

            return customString;
        }
    }
}
