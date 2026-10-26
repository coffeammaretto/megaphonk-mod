using System.Collections.Generic;
using UnityEngine.Localization.Settings;

namespace Megaphonk.Helpers
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
                ["Joined a lobby of {1} players as {0}!"] = "Ты в лобби из {1} игроков как {0}!",
                ["Disconnected"] = "Отключение",
                ["Player {0} disconnected"] = "Игрок {0} отключился",
                ["All players left!"] = "Все игроки вышли!",
                ["Host left!"] = "Хост вышел!",
                ["Client disconnected"] = "Клиент отключился",
                ["Friendlies"] = "Друзья",
                ["Hosting a friendly lobby"] = "Лобби для друзей открыто",
                ["Joined the lobby"] = "Ты в лобби",
                ["{0} joined"] = "{0} теперь в лобби",
                ["Mod version mismatch"] = "Версии мода не совпадают",
                ["{0} has {1}, you have {2}. Install the same build on every PC."] = "У {0} версия {1}, у тебя {2}. Поставьте одну версию на все ПК.",
                ["{0} has an older build of the mod. Install the same build on every PC."] = "У {0} старая версия мода. Поставьте одну версию на все ПК.",
                ["Gift"] = "Подарок",
                ["{0} gave you {1}"] = "{0} дарит тебе: {1}",
                ["Run modifiers"] = "Модификаторы забега",
                ["Coop challenge"] = "Кооп-испытание",
                ["Ascension"] = "Восхождение",
                ["Level {0}: enemies are stronger"] = "Уровень {0}: враги сильнее",
                ["Ascend (level {0})"] = "Восхождение (уровень {0})",

                // Rifts and pacts
                ["Rift"] = "Разлом",
                ["Hold the zones before time runs out"] = "Удержите зоны, пока не вышло время",
                ["hold the zones"] = "удержите зоны",
                ["collect the shards"] = "соберите осколки",
                ["survive the storm, stay in the circle"] = "переживите бурю, держитесь в круге",
                ["OUTSIDE THE CIRCLE!"] = "ВНЕ КРУГА!",
                ["Survive the storm: stay in the shrinking circle, nobody may die"] = "Переживите бурю: держитесь в сужающемся круге, никто не должен погибнуть",
                ["Collect the shards scattered over the map"] = "Соберите осколки, разбросанные по карте",
                ["Rift cleared!"] = "Разлом закрыт!",
                ["Rift failed..."] = "Разлом провален...",
                ["Rift cleared"] = "Разлом закрыт",
                ["Rift failed"] = "Разлом провален",
                ["Choose a pact: its boon and its curse stay for the rest of the run"] = "Выбери пакт: его дар и проклятие останутся до конца забега",
                ["Press 1, 2 or 3, or click a card"] = "Нажми 1, 2 или 3 или кликни по карточке",
                ["Might"] = "Мощь",
                ["Haste"] = "Спешка",
                ["Vigor"] = "Живучесть",
                ["Swiftness"] = "Стремительность",
                ["Precision"] = "Точность",
                ["Reach"] = "Размах",
                ["Endurance"] = "Стойкость",
                ["Bulwark"] = "Оплот",
                ["Fortune"] = "Удача судьбы",
                ["Leech"] = "Пиявка",
                ["Velocity"] = "Напор",
                ["Elemental Fury"] = "Ярость стихий",
                ["Wisdom"] = "Мудрость",
                ["Agility"] = "Ловкость",
                ["Slayer"] = "Истребитель",
                ["XP gain"] = "Опыт",
                ["Multishot"] = "Мультивыстрел",
                ["Ricochet"] = "Рикошет",
                ["Skywalker"] = "Небоходец",
                ["Inferno"] = "Инферно",
                ["Empowered"] = "Наполненность",
                ["Juggernaut"] = "Джаггернаут",
                ["Barrage"] = "Шквал",
                ["Blast Rounds"] = "Разрывные снаряды",
                ["Splinter"] = "Раскол",
                ["Storm Caller"] = "Зов грозы",
                ["Lance"] = "Копьё",
                ["Reaper"] = "Жнец",
                ["Hunter's Mark"] = "Метка охотника",
                ["Echo"] = "Эхо",
                ["Plague"] = "Чума",
                ["Hits explode: {0}% of the damage within 3.5 m"] = "Попадания взрываются: {0}% урона в радиусе 3.5 м",
                ["Hits split into {0} shards of 50% damage"] = "Попадание раскалывается на {0} осколка по 50% урона",
                ["{0}% chance of chain lightning: 3 jumps of 60% damage"] = "{0}% шанс цепной молнии: 3 прыжка по 60% урона",
                ["Hits pierce up to {0} enemies behind for 60% damage"] = "Попадание пробивает до {0} врагов позади, 60% урона",
                ["Hits kill non-boss enemies under {0}% HP"] = "Попадание добивает врагов (не боссов) ниже {0}% HP",
                ["Repeated hits on an enemy deal +{0}% damage"] = "Повторные попадания по врагу: +{0}% урона",
                ["{0}% chance a weapon attacks again"] = "{0}% шанс, что оружие ударит ещё раз",
                ["{0}% chance a kill leaves a poison cloud for 3 s"] = "{0}% шанс, что убитый оставит ядовитое облако на 3 с",

                // Ascension curses
                ["Ascension {0}"] = "Восхождение {0}",
                ["Choose a curse: it stays for the rest of the run"] = "Выбери проклятие: оно останется до конца забега",
                ["Press 1, 2 or 3, or click a curse"] = "Нажми 1, 2 или 3 или кликни по проклятию",
                ["Your curses"] = "Твои проклятия",
                ["none"] = "нет",
                ["Lesser"] = "Слабое",
                ["Normal"] = "Обычное",
                ["Greater"] = "Тяжёлое",
                ["Frailty"] = "Хрупкость",
                ["Weakness"] = "Слабость",
                ["Lethargy"] = "Вялость",
                ["Burden"] = "Тяжесть",
                ["Rot"] = "Гниение",
                ["Myopia"] = "Близорукость",
                ["Avarice"] = "Скупость",
                ["Misfortune"] = "Невезение",
                ["Exposure"] = "Обнажённость",
                ["Stiffness"] = "Скованность",
                ["Fading"] = "Угасание",
                ["Withering"] = "Усыхание",
                ["Dull Edge"] = "Тупой клинок",
                ["Cracked Shield"] = "Треснувший щит",
                ["Bloodless"] = "Бескровие",
                ["Blunted Thorns"] = "Затупленные шипы",
                ["Thick Air"] = "Вязкий воздух",
                ["Cowardice"] = "Трусость",
                ["Elemental Silence"] = "Стихийная немота",
                ["Cold Ashes"] = "Остывший пепел",
                ["Dim Power"] = "Тусклая сила",
                ["Inflation"] = "Инфляция",
                ["Grounded"] = "Приземлённость",
                ["Glass Bones"] = "Стеклянные кости",
                ["Tremor"] = "Дрожь",
                ["Exhaustion"] = "Изнеможение",
                ["Dull Mind"] = "Тупость",
                ["Leaden Rounds"] = "Свинцовые пули",
                ["Scattershot"] = "Разброс",
                ["Reckless"] = "Безрассудство",
                ["Bloodlust"] = "Жажда крови",
                ["Giant"] = "Великан",
                ["Short Fuse"] = "Короткий фитиль",
                ["Wild Bounce"] = "Шальной отскок",
                ["Projectiles"] = "Снаряды",
                ["Projectile bounces"] = "Отскоки снарядов",
                ["Extra jumps"] = "Доп. прыжки",
                ["Recoil"] = "Отдача",
                ["Jammed"] = "Заедание",
                ["Vengeful Dead"] = "Месть мёртвых",
                ["Blood Price"] = "Кровавая цена",
                ["Short Range"] = "Короткая дальность",
                ["Jinx"] = "Сглаз",
                ["Your shots push you back"] = "Выстрелы отталкивают тебя назад",
                ["{0}% of attacks misfire"] = "{0}% атак дают осечку",
                ["Enemies dying within 4 m hurt you for {0}% max HP"] = "Враги, погибшие в 4 м от тебя, бьют на {0}% макс. HP",
                ["Each attack costs {0}% max HP (never kills)"] = "Каждая атака стоит {0}% макс. HP (не убивает)",
                ["Projectiles last {0}% shorter"] = "Снаряды живут на {0}% меньше",
                ["{0}% of hits deal half damage"] = "{0}% попаданий наносят половину урона",
                ["Max HP"] = "Макс. HP",
                ["Damage"] = "Урон",
                ["Attack speed"] = "Скорость атаки",
                ["Movement speed"] = "Скорость бега",
                ["Healing"] = "Лечение",
                ["HP regen"] = "Реген HP",
                ["Pickup range"] = "Радиус подбора",
                ["Gold"] = "Золото",
                ["Silver"] = "Серебро",
                ["Luck"] = "Удача",
                ["Armor"] = "Броня",
                ["Evasion"] = "Уклонение",
                ["Duration"] = "Длительность",
                ["Effect duration"] = "Длительность эффектов",
                ["Size"] = "Размер",
                ["Crit chance"] = "Шанс крита",
                ["Crit damage"] = "Крит. урон",
                ["Shield"] = "Щит",
                ["Lifesteal"] = "Вампиризм",
                ["Thorns"] = "Шипы",
                ["Projectile speed"] = "Скорость снарядов",
                ["Knockback"] = "Отбрасывание",
                ["Elite damage"] = "Урон по элитам",
                ["Fire damage"] = "Урон огнём",
                ["Ice damage"] = "Урон льдом",
                ["Lightning damage"] = "Урон молнией",
                ["Poison damage"] = "Урон ядом",
                ["Burn chance"] = "Шанс поджога",
                ["Freeze chance"] = "Шанс заморозки",
                ["Power-up chance"] = "Шанс усилений",
                ["Power-up strength"] = "Сила усилений",
                ["Chest price"] = "Цена сундуков",
                ["Jump height"] = "Высота прыжка",

                // Challenges
                ["Challenges: a chest for each player with no death this stage, and for each player who survives the boss fight"] = "Испытания: сундук каждому, кто не умрёт на уровне, и каждому, кто переживёт бой с боссом",
                ["Someone died: no stage chest for them this time"] = "Кто-то погиб: этому игроку сундука за уровень не будет",
                ["Boss defeated: a chest for each survivor of the fight awaits on the next stage!"] = "Босс повержен: по сундуку каждому выжившему в бою ждёт на следующем уровне!",
                ["Stage cleared: a chest for each player with no death awaits on the next stage!"] = "Уровень пройден: по сундуку каждому, кто не умирал, ждёт на следующем уровне!",

                // Waiting for other players
                ["Waiting for other player(s) choices..."] = "Ждём выбор других игроков...",
                ["Waiting for other player(s) choices at the Microwave..."] = "Ждём выбор других игроков у микроволновки...",
                ["Waiting for other player(s) choices at the Shrine of Balance..."] = "Ждём выбор других игроков у Святыни Баланса...",
                ["Waiting for other player(s) choices at the Moai..."] = "Ждём выбор других игроков у Моаи...",
                ["Waiting for other player(s) choices in a chest..."] = "Ждём выбор других игроков в сундуке...",
                ["Waiting for other player(s) choices with the Shady Guy..."] = "Ждём выбор других игроков у Подозрительного типа...",
                ["Waiting for other player(s) choices...\nStuck? Press F8 to continue"] = "Ждём выбор других игроков...\nЗавис? Нажми F8, чтобы продолжить",
                ["Waiting for the host..."] = "Ждём хоста...",
                ["Waiting for all players to be ready..."] = "Ждём готовности всех игроков...",
                ["Locking lobby..."] = "Закрываем лобби...",
                ["Failed to lock lobby. Please try again."] = "Не удалось закрыть лобби. Попробуй ещё раз.",
                ["A friend just joined: wait for them to be ready, then start the run again."] = "Только что зашёл друг: дождись его готовности и снова начни забег.",
                ["Waiting for other players"] = "Ждём других игроков",
                ["Waiting for players to be ready..."] = "Ждём готовности игроков...",
                ["Waiting for players to be ready...\nReady: {0}/{1}"] = "Ждём готовности игроков...\nГотовы: {0}/{1}",

                // In a run
                ["You are dead!"] = "Тебя убили!",
                ["Left Click: Previous | Right Click: Next"] = "ЛКМ: назад | ПКМ: вперёд",
                ["Spectating: {0} ({1}/{2})"] = "Наблюдение: {0} ({1}/{2})",
                ["You will be vulnerable in {0}:{1} seconds"] = "Неуязвимость кончится через {0}:{1} сек.",
                ["You are now vulnerable!"] = "Неуязвимость закончилась!",
                ["No invulnerability while talking to the Shady Guy!"] = "Пока говоришь с Подозрительным типом, неуязвимости нет!",
                ["Give to {0}"] = "Отдать: {0}",
                ["DOWN! Reviving... {0}"] = "НОКДАУН! Тебя поднимают... {0}",
                ["DOWN! {0}s left\nA teammate must stand next to you"] = "НОКДАУН! Осталось {0} с\nНапарник должен встать рядом",
                ["Reviving {0}..."] = "Поднимаем: {0}...",
                ["{0} is down! {1}m"] = "{0}: нокдаун! {1} м",
                ["{0} - DOWN"] = "{0} - НОКДАУН",
                ["TEAM"] = "КОМАНДА",
                ["{0}: {1} damage, {2} kills, {3} gold"] = "{0}: урон {1}, убийств {2}, золото {3}",
                ["TEAM BUFF! {0}s"] = "КОМАНДНЫЙ БОНУС! {0} с",
                ["TEAM [{0}]"] = "КОМАНДА [{0}]",

                // Menus
                ["Close"] = "Закрыть",
                ["Player Name:"] = "Имя игрока:",
                ["Enter your name..."] = "Введи имя...",
                ["Host"] = "Хост",
                ["Client"] = "Клиент",
                ["Join"] = "Войти",
                ["Megaphonk co-op leaderboard"] = "Кооп-лидерборд Megaphonk",
                ["Share your finished co-op runs with the online leaderboard? It receives your Steam name and Steam ID (proved with a Steam session ticket), your character and kills, and the run's map and tier. A run is listed only when every player of it agreed. You can change this later in the mod's config (ShareCoopRuns)."] = "Отправлять завершённые кооп-забеги в онлайн-лидерборд? Он получит твоё имя и Steam ID в Steam (подтверждённый сессионным тикетом Steam), персонажа и убийства, карту и уровень сложности забега. Забег попадёт в таблицу, только если согласились все его игроки. Изменить выбор можно в конфиге мода (ShareCoopRuns).",
                ["Share"] = "Отправлять",
                ["Don't share"] = "Не отправлять",
                ["Friends"] = "Друзья",
                ["Back"] = "Назад",
                ["Stop"] = "Стоп",
                ["Connecting..."] = "Подключение...",
                ["Joining room..."] = "Вход в комнату...",
                ["Joined!"] = "Вы вошли!",
                ["Match found!"] = "Матч найден!",
                ["No Steam friends are hosting right now.\nAsk a friend to press Host."] = "Сейчас никто из друзей Steam не хостит.\nПопроси друга нажать «Хост».",
                ["Failed to connect: {0}"] = "Не удалось подключиться: {0}",
                ["Failed to connect to server: {0}"] = "Не удалось подключиться к серверу: {0}",
                ["Network interrupted, please try again: {0}"] = "Связь прервалась, попробуй ещё раз: {0}",
                ["Steam is not available. Is Steam running?"] = "Steam недоступен. Steam запущен?",
                ["Failed to establish P2P connection."] = "Не удалось установить P2P-соединение.",
                ["Host has disconnected"] = "Хост отключился",
                ["Megaphonk Mod: What's New?"] = "Megaphonk Mod: что нового?",
                ["Got it!"] = "Понятно!",
                ["Co-op Leaderboard"] = "Кооп-рекорды",
                ["Forest"] = "Лес",
                ["Desert"] = "Пустыня",
                ["Hell"] = "Ад",
                ["Graveyard"] = "Кладбище",
                ["Tier {0}"] = "Тир {0}",
                ["{0}P"] = "{0} игр.",
                ["Global"] = "Все",
                ["Loading..."] = "Загрузка...",
                ["Could not load the leaderboard"] = "Не удалось загрузить рекорды",
                ["No runs yet"] = "Пока нет забегов",
                ["Megaphonk Mod version {0}\nis available!\nDownload it from GitHub."] = "Megaphonk Mod версии {0}\nдоступен!\nСкачай его на GitHub.",
                ["Megaphonk Mod version {0}\nis available!\nUpdate it in your mod manager."] = "Megaphonk Mod версии {0}\nдоступен!\nОбнови его в менеджере модов.",
                ["Role: {0}"] = "Роль: {0}",
                ["Shared Experience: {0}"] = "Общий опыт: {0}",
                ["Copy Code"] = "Копировать код",
                ["Code: ******"] = "Код: ******",
                ["Thanks for playing!"] = "Спасибо, что играешь!",
                ["REVIVE!"] = "ПОДНЯТЬ!",
                ["A teammate"] = "Напарник",
                ["Invalid room code {0}"] = "Неверный код комнаты {0}",
                ["Room not found: the host is not hosting, or the code is wrong"] = "Комната не найдена: хост не открыл её или код неверный",
                ["Game has already started"] = "Игра уже началась",
                ["Room is full"] = "Комната заполнена",
                ["The host runs mod version {0}, you have {1}"] = "У хоста версия мода {0}, у тебя {1}",
                ["Could not start networking"] = "Не удалось запустить сеть",
                ["Unexpected message from matchmaking server"] = "Непонятный ответ сервера подбора",

                // Netplay options
                ["Netplay Options"] = "Настройки сети",
                ["Reward pause\nParty: everyone pauses, Own: no pause"] = "Пауза при выборе\nПати: пауза у всех, Свой: без паузы",
                ["Downed state\nA teammate can revive you"] = "Нокдаун\nНапарник может тебя поднять",
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
                ["Damage +50%, max HP -40%"] = "Урон +50%, макс. ХП -40%",
                ["Horde"] = "Орда",
                ["Enemy HP +50%, XP +25%, gold +15%"] = "Здоровье врагов +50%, опыт +25%, золото +15%",
                ["Thick Skinned"] = "Толстокожие",
                ["Enemy HP x2, luck +0.25"] = "Здоровье врагов x2, удача +0,25",
                ["Frenzy"] = "Неистовство",
                ["Enemies +10% speed every 5 min, gold +20%"] = "Скорость врагов +10% каждые 5 мин, золото +20%",
                ["Lucky"] = "Везунчик",
                ["Luck +0.3, difficulty +0.3"] = "Удача +0,3, сложность +0,3",
                ["Poverty"] = "Нищета",
                ["Gold -50%, XP +15%"] = "Золото -50%, опыт +15%",
                ["Colossus"] = "Колосс",
                ["Enemy HP +50%, XP +25%"] = "Здоровье врагов +50%, опыт +25%",
                ["Brittle Swarm"] = "Хрупкий рой",
                ["Enemy HP -40%, enemy damage +40%, XP -10%"] = "Здоровье врагов -40%, урон врагов +40%, опыт -10%",
                ["Bloodbath"] = "Кровавая баня",
                ["Enemy damage +50%, healing -50%, gold +25%"] = "Урон врагов +50%, лечение -50%, золото +25%",
                ["Pinball"] = "Пинбол",
                ["Knockback x2, move speed +15%"] = "Отдача x2, скорость +15%",
                ["Silver Rush"] = "Серебряная лихорадка",
                ["Silver +50%, enemy damage +25%"] = "Серебро +50%, урон врагов +25%",
                ["Overdrive"] = "Разгон",
                ["Move speed +20%, projectile speed +25%, enemy speed +20%"] = "Скорость +20%, скорость снарядов +25%, скорость врагов +20%",
                ["Shockwave"] = "Ударная волна",
                ["Size +25%, damage -20%"] = "Размер +25%, урон -20%",
                ["Rising Tide"] = "Прилив",
                ["Enemies scale 50% faster, chests -25% price"] = "Враги усиливаются на 50% быстрее, сундуки на 25% дешевле",
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
