using System.Collections.Generic;
using UnityEngine.Localization.Settings;

namespace MegabonkTogether.Helpers
{
    /// <summary>
    /// The mod's own texts, in the language the game is set to. Texts are written in English in the code and
    /// looked up by that English text: a language without an entry (or a text without a translation) shows English.
    /// Add a language by adding a dictionary to <see cref="Languages"/>, keyed by the locale code of the game ("ru", "de"...).
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Languages = new()
        {
            ["ru"] = new()
            {
                // Notifications
                ["Match"] = "Матч",
                ["Joined as {0} a lobby of {1} players successfully!"] = "Вы вошли как {0} в лобби из {1} игроков!",
                ["Disconnected"] = "Отключение",
                ["Player {0} disconnected"] = "Игрок {0} отключился",
                ["All players left!"] = "Все игроки вышли!",
                ["Host left!"] = "Хост вышел!",
                ["Client disconnected"] = "Клиент отключился",
                ["Friendlies"] = "Друзья",
                ["Hosting a friendly lobby"] = "Вы открыли лобби для друзей",
                ["Joined {0} lobby"] = "Вы вошли в лобби {0}",
                ["{0} joined "] = "{0} вошёл ",
                ["Mod version mismatch"] = "Версии мода не совпадают",
                ["{0} has {1}, you have {2}. Install the same build on every PC."] = "У {0} версия {1}, у вас {2}. Поставьте одну сборку на все ПК.",
                ["{0} has an older build of the mod. Install the same build on every PC."] = "У {0} старая сборка мода. Поставьте одну сборку на все ПК.",
                ["Gift"] = "Подарок",
                ["{0} gave you {1}"] = "{0} дарит вам: {1}",
                ["Run modifiers"] = "Модификаторы забега",
                ["Coop challenge"] = "Кооп-испытание",
                ["Teammate"] = "Напарник",
                ["{0} took {1}"] = "{0} берёт: {1}",

                // Challenges
                ["Challenges: no death this stage, kill the boss with everyone alive"] = "Испытания: уровень без смертей и босс при всех живых",
                ["Challenge failed: someone died this stage"] = "Испытание провалено: на уровне кто-то погиб",
                ["Challenge complete: boss killed with everyone alive, a chest dropped!"] = "Испытание выполнено: босс убит при всех живых, выпал сундук!",
                ["Challenge complete: no death this stage, a bonus chest awaits on the next one!"] = "Испытание выполнено: уровень без смертей, бонусный сундук ждёт на следующем!",

                // Waiting for other players
                ["Waiting for other player(s) choices..."] = "Ждём выбор других игроков...",
                ["Waiting for other player(s) choices in Microwave..."] = "Ждём выбор других игроков в микроволновке...",
                ["Waiting for other player(s) choices in Balance Shrine..."] = "Ждём выбор других игроков у святилища баланса...",
                ["Waiting for other player(s) choices in Moai Shrine..."] = "Ждём выбор других игроков у святилища Моаи...",
                ["Waiting for other player(s) choices in Chest..."] = "Ждём выбор других игроков в сундуке...",
                ["Waiting for other player(s) choices with Shady Guy..."] = "Ждём выбор других игроков у тёмного типа...",
                ["Waiting for other player(s) choices...\nStuck? Press F8 to continue"] = "Ждём выбор других игроков...\nЗавис? Нажмите F8, чтобы продолжить",
                ["Waiting for the host..."] = "Ждём хоста...",
                ["Waiting for all players to be ready..."] = "Ждём готовности всех игроков...",
                ["Locking lobby..."] = "Закрываем лобби...",
                ["Failed to lock lobby. Please try again."] = "Не удалось закрыть лобби. Попробуйте ещё раз.",
                ["Waiting for other players"] = "Ждём других игроков",
                ["Waiting for players to be ready..."] = "Ждём готовности игроков...",
                ["Waiting for players to be ready...\nReady: {0}/{1}"] = "Ждём готовности игроков...\nГотовы: {0}/{1}",

                // In a run
                ["You are dead!"] = "Вы погибли!",
                ["Left Click: Previous | Right Click: Next"] = "ЛКМ: назад | ПКМ: вперёд",
                ["Spectating: {0} ({1}/{2})"] = "Наблюдение: {0} ({1}/{2})",
                ["You will be vulnerable in {0}:{1} seconds"] = "Вы будете уязвимы через {0}:{1} сек.",
                ["You are now vulnerable!"] = "Теперь вы уязвимы!",
                ["You are vulnerable during shady guy interaction!"] = "Вы уязвимы во время общения с тёмным типом!",
                ["Give to {0}"] = "Отдать: {0}",
                ["DOWN! Reviving... {0}"] = "ВЫ УПАЛИ! Поднимают... {0}",
                ["DOWN! {0}s left\nA teammate must stand next to you"] = "ВЫ УПАЛИ! Осталось {0} с\nНапарник должен встать рядом",
                ["Reviving {0}..."] = "Поднимаем: {0}...",
                ["{0} is down! {1}m"] = "{0} упал! {1} м",
                ["{0} - DOWN"] = "{0} - УПАЛ",
                ["TEAM"] = "КОМАНДА",
                ["{0}: {1} damage, {2} kills, {3} gold"] = "{0}: урон {1}, убийств {2}, золото {3}",
                ["TEAM BUFF! {0}s"] = "КОМАНДНЫЙ БОНУС! {0} с",
                ["TEAM [{0}]"] = "КОМАНДА [{0}]",

                // Menus
                ["Close"] = "Закрыть",
                ["Player Name:"] = "Имя игрока:",
                ["Enter your name..."] = "Введите имя...",
                ["Host"] = "Хост",
                ["Client"] = "Клиент",
                ["Join"] = "Войти",
                ["Friends"] = "Друзья",
                ["Back"] = "Назад",
                ["Stop"] = "Стоп",
                ["Connecting..."] = "Подключение...",
                ["Joining room..."] = "Вход в комнату...",
                ["Joined!"] = "Вы вошли!",
                ["Match found!"] = "Матч найден!",
                ["No Steam friends are hosting right now.\nAsk a friend to press Host."] = "Сейчас никто из друзей Steam не хостит.\nПопросите друга нажать «Хост».",
                ["Failed to connect: {0}"] = "Не удалось подключиться: {0}",
                ["Failed to connect to server : {0}"] = "Не удалось подключиться к серверу: {0}",
                ["Network interrupted. Please try again : {0}"] = "Сеть прервана. Попробуйте ещё раз: {0}",
                ["Steam is not available. Is Steam running?"] = "Steam недоступен. Steam запущен?",
                ["Failed to establish P2P connection."] = "Не удалось установить P2P-соединение.",
                ["Host has disconnected"] = "Хост отключился",
                ["Megaphonk Mod: What's New?"] = "Megaphonk Mod: что нового?",
                ["Got it!"] = "Понятно!",
                ["Quit & Update"] = "Выйти и обновить",
                ["Megaphonk Mod version {0}\nis available!\nDownload it from GitHub."] = "Megaphonk Mod версии {0}\nдоступен!\nСкачайте его на GitHub.",
                ["Megaphonk Mod version {0}\ndownloaded and ready\nto be applied! Restart the game after the update!"] = "Megaphonk Mod версии {0}\nзагружен и готов\nк установке! Перезапустите игру после обновления!",
                ["Role: {0}"] = "Роль: {0}",
                ["Shared Experience: {0}"] = "Общий опыт: {0}",
                ["Copy Code"] = "Копировать код",

                // Netplay options
                ["Netplay Options"] = "Настройки сети",
                ["Reward pause\nParty: everyone pauses, Own: no pause"] = "Пауза при выборе\nПати: пауза у всех, Свой: без паузы",
                ["Downed state\nA teammate can revive you"] = "Нокдаун\nНапарник может вас поднять",
                ["Team meter\nKill together for a team buff"] = "Командная шкала\nУбивайте вместе ради командного бонуса",
                ["Coop challenges\nRewarded with chests"] = "Кооп-испытания\nНаграда: сундуки",
                ["Party luck/difficulty\nHow players' values combine"] = "Удача/сложность пати\nКак складываются значения игроков",
                ["ON"] = "ВКЛ",
                ["OFF"] = "ВЫКЛ",
                ["Party"] = "Пати",
                ["Own"] = "Свой",
                ["Sum"] = "Сумма",
                ["MaxPlusHalf"] = "Макс + ½",
                ["Average"] = "Среднее",
                ["Glass Cannon"] = "Стеклянная пушка",
                ["Damage x2, max HP x0.5"] = "Урон x2, макс. здоровье x0,5",
                ["Horde"] = "Орда",
                ["Enemies x1.5, XP +25%"] = "Врагов x1,5, опыт +25%",
                ["Thick Skinned"] = "Толстокожие",
                ["Enemy HP x2, luck +0.25"] = "Здоровье врагов x2, удача +0,25",
                ["Frenzy"] = "Неистовство",
                ["Enemies +10% speed every 5 min"] = "Враги +10% скорости каждые 5 мин",
                ["Lucky"] = "Везунчик",
                ["Luck +0.5, difficulty +0.3"] = "Удача +0,5, сложность +0,3",
                ["Poverty"] = "Нищета",
                ["Gold -50%, XP +25%"] = "Золото -50%, опыт +25%",
                ["Colossus"] = "Колосс",
                ["Enemy HP +50%, XP +50%"] = "Здоровье врагов +50%, опыт +50%",
                ["Brittle Swarm"] = "Хрупкий рой",
                ["Enemy HP -40%, enemies x1.5"] = "Здоровье врагов -40%, врагов x1,5",
                ["Bloodbath"] = "Кровавая баня",
                ["Enemy damage +50%, healing -50%, gold +25%"] = "Урон врагов +50%, лечение -50%, золото +25%",
                ["Pinball"] = "Пинбол",
                ["Knockback x2, move speed +15%"] = "Отбрасывание x2, скорость +15%",
            },
        };

        /// <summary>
        /// Locale code of the game's language ("en", "ru"...), "en" until the game's localization is ready
        /// </summary>
        public static string Language
        {
            get
            {
                try
                {
                    if (LocalizationSettings.HasSettings)
                    {
                        var code = LocalizationSettings.SelectedLocale?.Identifier.Code;
                        if (!string.IsNullOrEmpty(code))
                        {
                            var dash = code.IndexOf('-');
                            return (dash > 0 ? code.Substring(0, dash) : code).ToLowerInvariant();
                        }
                    }
                }
                catch
                {
                    // Not ready: English
                }

                return "en";
            }
        }

        /// <summary>
        /// The text in the game's language
        /// </summary>
        public static string T(string english)
        {
            return !string.IsNullOrEmpty(english) && Languages.TryGetValue(Language, out var table) && table.TryGetValue(english, out var translated)
                ? translated
                : english;
        }

        /// <summary>
        /// A text with {0}-style placeholders in the game's language
        /// </summary>
        public static string F(string english, params object[] args)
        {
            try
            {
                return string.Format(T(english), args);
            }
            catch (System.FormatException)
            {
                return string.Format(english, args);
            }
        }
    }
}
