# Megaphonk Mod

Co-op mod for **Megabonk 1.0.69** (up to 6 players), built on BepInEx 6 IL2CPP.

[Русская версия ниже](#на-русском)

## What the mod adds

### Playing together
- Online co-op for up to 6 players: **MEGAPHONK!** button in the main menu.
- Rooms through **Steam**, no matchmaking server and no room codes:
  - **Host**: MEGAPHONK! → Host. The character selection opens right away, friends can join until the run starts. Nobody joined? The host can start the run alone, with the mod's features (run modifiers, challenges, team stats).
  - **Join**: MEGAPHONK! → Join → pick a friend from the list. The list shows Steam friends who have the game running and a room open, and refreshes every 2 seconds.
- The host runs the game: enemies, damage and the clients' weapons, computed with each client's real stats.
- Shared experience (on by default): every XP orb picked up by anyone gives its XP to every player, each with their own XP gain (so levels may differ), and a reward window pauses the game for everyone.
- Saving progression works during netplay (on by default).
- Mod version check: a player with another build cannot join and is told why.
- Fixes for Megabonk 1.0.69: startup crashes, stuck pauses, enemies wandering off, instant coin and XP pickup, the teleport to the final boss leaving everyone stuck on "Waiting for other players".

### Network and performance
- The network messages of a frame are sent as one packet instead of hundreds of separate ones, so the late game no longer stutters when lots of XP, gold and enemies spawn and die. Big snapshots are split.
- The host merges damage per enemy and per player each frame before sending it: clients see fewer, bigger damage numbers.
- Over `MaxPickupObjects` (200 by default) XP orbs or gold coins on the map, the oldest ones merge into one worth as much: fewer objects, less lag.
- Far enemies stay visible (they are only updated less often on clients), and the host resends every enemy's position at least once a second, so a lost packet never leaves an enemy stuck on a client's screen.

### Rewards
- While anyone picks a reward (level up, chest, shrine), the game pauses for everyone. `RewardPauseMode = Own` turns the shared pause off: you pick without a pause and stay invulnerable meanwhile, the others keep playing.
- **F8** unlocks a stuck pause for everyone.
- Clicks are ignored for the first second after a reward window appears, so nobody picks by accident.
- **Give to a teammate**: a chest window has a "Give to <name>" button under the decline button. The item goes to the nearest living teammate, who gets a "Gift" notification.

### Chests and gold
- A chest bought by one player opens for everyone, only the buyer pays.
- Chest price grows with the number of players (`ChestPricePerExtraPlayer`, +25% per extra player by default).
- Gold is per player.

### Stats
- Character passive bonuses (the ones that grow with the character's level) apply to the whole party across the whole map, and the same characters stack. `SharedPassiveRadius` limits them to the area around their owner.
- Party luck and difficulty (`PartyStatMode`): sum, highest + half of the others, or average of all players, with optional caps.
- Enemy HP grows with the number of players (`EnemyHpPerExtraPlayer`, +10% per extra player) and the amount of enemies has its own multiplier (`EnemyAmountMultiplier`).
- Damage and kills count for the player who dealt them; item effects trigger for their owner (no double damage, no item effects on other players' characters).

### Co-op mechanics
- **Downed state**: lethal damage downs a player instead of killing them. They bleed out for 25 seconds, invulnerable but unable to attack, they barely crawl (sliding and jumping included), and enemies ignore them. A downed player sways, dazed. A teammate standing within 8 meters revives them in 3 seconds with 30% HP. The timers stop while a reward (chest, level up...) is being picked. Nobody came in time: a normal death and a grave. Screen hints show the timer and the distance to a downed teammate.
- **Teammates through walls**: a teammate hidden by a wall, a tree or the ground shows as a silhouette in their color, and their name stays on screen at any distance (with the distance past 25 m).
- **Ping**: the middle mouse button (`PingKey`) marks a spot for the whole team: a beam of the player's color and an arrow on the minimap, for 6 seconds.
- **Team meter**: a kill fills the meter only when at least two different players killed within the last 5 seconds. A full meter gives everyone +25% damage and +15% move speed for 10 seconds, then the next threshold is 1.5 times higher.
- **Co-op challenges** (`ChallengesEnabled`), announced at the start of a stage, rewarded with chests that drop from the sky at the start of the next stage (downs don't count as deaths):
  - a chest for each player who didn't die during the stage;
  - a chest for each player who was alive when the stage boss appeared and didn't die before it was killed.
- **Enemy aggro** is decided by the host and spread across the party:
  - regular enemies spawn next to a random alive player and chase them, then every 40-60 seconds may switch to another random player within 50 m;
  - stage bosses switch target every 20-40 seconds, the final boss every 30-50 seconds, within 300 m;
  - bosses, minibosses and other enemies spawned at a fixed spot start on a random alive player, not on the host;
  - downed and dead players are never chased: their enemies switch to someone alive.

### Ascension (co-op endgame)
Once the final boss is dead, a second portal stands next to the final one. The final portal ends the run as usual (character unlocks, map progress); the **Ascend** portal takes the whole party back to the first stage of the same map, builds kept, at ascension 1, 2, 3...
- Each ascension makes enemies stronger, never more numerous: enemy HP x1.5 (`AscensionEnemyHpGrowth`) and damage x1.25 (`AscensionEnemyDamageGrowth`) per level, compounding, and at most 300 enemies on the map at once (`AscensionMaxEnemies`). Turned off with `AscensionEnabled`.
- **Curses**: after each ascension, every player picks one of three random curses for themselves (1/2/3 or a click), since each character has its own weaknesses. The party pauses meanwhile, as for a level up. Curses stack for the rest of the run.
- 26 curses, from one stat (max HP, damage, attack speed, armor, evasion, shield, lifesteal, luck...) to two or more (crits, elemental damage, power-ups, gold and silver). Each card rolls its strength: lesser (x0.6, 45%), normal (40%) or greater (x1.6, 15%). A curse takes a percentage of what the player has, so a build without that stat doesn't care. One stat loses at most 75% to curses.
- **Rifts**: from the first ascension, every stage but the final boss's opens a rift 60 seconds in, then 3 minutes after the previous one ends, a random kind, within 120 seconds:
  - zones to hold (one per player, at most two; purple ring): each fills in 20 seconds while a player, alive and not downed, stands in it, faster with more players in it;
  - shards to collect (3 per player, 4 to 8; golden): each is picked up by walking to it;
  - a storm to survive (red circle around the party): the circle shrinks to 12 m in 60 seconds, then stays 30 seconds; outside, a player loses 8% of their max HP every second. Failed if someone dies, or is down when it ends.
  They go anywhere on the map on open, even ground, with a beam to the sky and a ring seen through walls at any distance, and an arrow on the minimap. A rift adds no enemy. Cleared: every player picks a **pact**, a boon (+15 to 40% to a stat) paid with a lesser or normal curse. Failed: every player picks a curse.
- Co-op only, the host decides: a client using the portal asks the host.

### Run modifiers
The host ticks any number of them in **Netplay Options** on the character selection (or sets `RunModifiers` in the config). They apply to the whole party, and a notification announces them at the start of a run.

| Modifier | Effect |
| --- | --- |
| Glass Cannon | Damage +50%, max HP -40% |
| Horde | Enemy HP +50%, XP +25%, gold +15% |
| Thick Skinned | Enemy HP x2, luck +0.25 |
| Frenzy | Enemies get 10% faster every 5 minutes, gold +20% |
| Lucky | Luck +0.3, difficulty +0.3 |
| Poverty | Gold -50%, XP +15% |
| Colossus | Enemy HP +50%, XP +25% |
| Brittle Swarm | Enemy HP -40%, enemy damage +40%, XP -10% |
| Bloodbath | Enemy damage +50%, healing -50%, gold +25% |
| Pinball | Knockback x2, move speed +15% |
| Silver Rush | Silver +50%, enemy damage +25% |
| Overdrive | Move speed +20%, projectile speed +25%, enemies 20% faster |
| Shockwave | Weapon area +25%, damage -20% |
| Rising Tide | Enemies grow stronger 50% faster, chests 25% cheaper |

Bonuses of several modifiers add up, with caps so that stacking them makes a harder run rather than an overpowered character: XP gain at most +50%, luck +0.5, gold +50%, damage +50%, move speed +30%. Penalties are not capped.

### Convenience
- Netplay Options (the host's button on the character selection): reward pause, downed state, team meter, co-op challenges, party luck/difficulty mode and the run modifiers. The host's values apply to everyone and are resent when changed. Numeric values (timers, multipliers) are in the config file.
- Team table at the end of a run: damage, kills and gold of each player (defeat screen).
- Music: the mod's tracks play in a random order instead of the stage music, and the main menu has its own track; boss music stays the game's. In co-op everybody hears the host's track. Turned off with `CustomMusic` in the config.
- Player name, key bindings and every party rule in `BepInEx\config\MegaphonkMod.cfg`.
- Notice when a new version is out on GitHub (`CheckForUpdates`) and a changelog window. The mod never downloads or installs anything itself.
- The mod's texts follow the game's language: English and Russian (other languages show English).
- Testing shortcuts (`DebugTools`, off; set by the host for the whole party): the first stage's portal leads to the final boss, F10 on the host kills every enemy, F9 on the host opens a rift.

## Online services and privacy

Besides the game itself, the mod talks to:
- **Steam**, for rooms (Steam lobbies and Steam relays between the players).
- **GitHub**, to tell you when a new version is out and to show its changelog (`CheckForUpdates`). It only reads the latest release; it never downloads or runs code.
- **The Megaphonk co-op leaderboard** (`https://megaphonk-leaderboard.megaphonk-leaderboard.workers.dev`, run by the mod's author), only if you agree. The mod asks once in the main menu (`ShareCoopRuns` in the config: `Ask`, `Yes` or `No`). With `Yes`, a finished co-op run sends your Steam name and Steam ID (proved with a Steam session ticket), your character and kills, and the run's map, tier, modifiers and challenge. A run is listed only when every player of it said `Yes`. The friends tab of the leaderboard sends your Steam friends' IDs to show their runs, also only with `Yes`. Reading the global leaderboard sends nothing about you.

The mod's music was made by the mod's author with Suno (paid plan).

## Installation

With a mod manager: install [MegaphonkMod on Thunderstore](https://thunderstore.io/c/megabonk/p/ROFL_MODS/MegaphonkMod/) with r2modman or Gale, the mod manager installs BepInEx and updates the mod. Manual installation:

1. Close the game.
2. Download `MegaphonkMod-2.10.0-full.zip` from the [latest release](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
3. Extract it into the game folder (Steam → Megabonk → Manage → Browse local files), replacing files. The folder must contain `winhttp.dll`, `doorstop_config.ini`, `BepInEx` and `dotnet` next to `Megabonk.exe`.
4. Start the game through Steam. The first launch takes longer while BepInEx prepares the game.

Every player of a room needs the same mod version and must be on the host's Steam friends list. The `MegaphonkMod-X.Y.Z.zip` asset contains only the mod files, to update an existing installation by hand.

Versions follow [semantic versioning](https://semver.org): X.Y.**Z** for fixes, X.**Y**.0 for new features, **X**.0.0 for changes that break compatibility (for example, players on different major versions cannot play together).

## Playing

- Host: MEGAPHONK! → Host, pick a character, adjust Netplay Options if you want, wait for your friends, or start alone.
- Others: MEGAPHONK! → Join → pick the host from the list.
- Joining is possible only on the character selection, not during a run.

## Limitations

- No Steam overlay invites or "Join Game" button: the game consumes the Steam callbacks they need.
- Random matchmaking is disabled: its public server's players run an incompatible mod.
- Joining through the friends list is new and has been tested on one PC only. If a friend does not show up, send `BepInEx\LogOutput.log` in an issue.
- In a solo run there is no downed state (nobody can revive you) and no team meter (it needs kills by two players).

## Credits

Megaphonk Mod is based on [Megabonk Together](https://github.com/Fcornaire/megabonk-together) by Fcornaire, licensed under GPL-2.0. This mod is distributed under the same license.

---

## На русском

Кооп-мод для **Megabonk 1.0.69** до 6 игроков, на BepInEx 6 IL2CPP.

### Что добавляет мод

#### Игра вместе
- Онлайн-кооп до 6 игроков: кнопка **MEGAPHONK!** в главном меню.
- Комнаты через **Steam**, без сервера и без кодов:
  - **Хост**: MEGAPHONK! → Host. Сразу открывается выбор персонажа, друзья могут войти до старта забега. Никто не зашёл? Хост может начать забег один, с возможностями мода (модификаторы забега, испытания, статистика).
  - **Вход**: MEGAPHONK! → Join → выбрать друга в списке. В списке друзья из Steam, у которых запущена игра и открыта комната, список обновляется раз в 2 секунды.
- Хост ведёт игру: врагов, урон и оружие клиентов, которое считается с их настоящими статами.
- Общий опыт (включён по умолчанию): сфера опыта, подобранная кем угодно, даёт опыт каждому игроку с его собственным множителем опыта (уровни могут различаться), а окно награды ставит игру на паузу у всех.
- Сохранение прогресса работает во время коопа (включено по умолчанию).
- Проверка версии: игрока с другой версией мода не пустят в комнату и объяснят почему.
- Исправления под Megabonk 1.0.69: вылеты при запуске, зависшие паузы, уходящие враги, мгновенный подбор монет и опыта, зависание всех на «Ждём других игроков» при телепорте к финальному боссу.

#### Сеть и производительность
- Сетевые сообщения за кадр уходят одним пакетом, а не сотнями отдельных: поздняя игра больше не фризит, когда появляется и умирает много опыта, золота и врагов. Большие снимки состояния режутся на части.
- Хост сливает урон по каждому врагу от каждого игрока за кадр перед отправкой: у клиентов меньше цифр урона, но они крупнее.
- Если на карте больше `MaxPickupObjects` (по умолчанию 200) сфер опыта или монет, самые старые сливаются в одну с той же суммой: меньше объектов, меньше лагов.
- Дальние враги остаются видимыми (у клиентов они просто реже обновляются), а хост раз в секунду заново шлёт позиции всех врагов, поэтому потерянный пакет не оставляет врага застрявшим у клиента.

#### Награды
- Пока кто-то выбирает награду (уровень, сундук, святилище), игра на паузе у всех. `RewardPauseMode = Own` отключает общую паузу: выбираешь без паузы и неуязвим, остальные играют.
- **F8** снимает зависшую паузу у всех.
- Первую секунду после появления окна награды клики игнорируются, чтобы не выбрать случайно.
- **Передача напарнику**: в окне сундука под кнопкой отказа есть кнопка «Give to <имя>». Предмет уходит ближайшему живому напарнику, и он видит уведомление «Gift».

#### Сундуки и золото
- Купленный сундук открывается у всех, платит только покупатель.
- Цена сундука растёт с числом игроков (`ChestPricePerExtraPlayer`, по умолчанию +25% за каждого лишнего игрока).
- Золото у каждого своё.

#### Статы
- Пассивные бонусы персонажей (те, что растут с уровнем героя) действуют на всю пати по всей карте, одинаковые персонажи складываются. `SharedPassiveRadius` ограничивает их зоной вокруг владельца.
- Общие удача и сложность (`PartyStatMode`): сумма, максимум + половина остальных или среднее, с необязательными потолками.
- Здоровье врагов растёт с числом игроков (`EnemyHpPerExtraPlayer`, по умолчанию +10% за лишнего игрока), у количества врагов свой множитель (`EnemyAmountMultiplier`).
- Урон и убийства засчитываются тому, кто их нанёс; эффекты предметов срабатывают у владельца (без двойного урона и без эффектов предметов на чужих персонажах).

#### Кооп-механики
- **Нокдаун**: смертельный урон не убивает, а роняет игрока. Он истекает кровью 25 секунд, неуязвим, но не может атаковать и еле ползёт (скольжение и прыжки тоже не помогают), враги его игнорируют. Упавший персонаж шатается, как оглушённый. Напарник в радиусе 8 метров поднимает его за 3 секунды с 30% здоровья. Пока идёт выбор награды (сундук, уровень...), таймеры стоят. Не успели: обычная смерть и могила. Подсказки на экране показывают таймер и расстояние до упавшего напарника.
- **Напарники сквозь стены**: напарник за стеной, деревом или рельефом виден силуэтом своего цвета, а его ник виден на любой дистанции (дальше 25 м с расстоянием).
- **Пинг**: средняя кнопка мыши (`PingKey`) ставит метку для всей команды: столб света цвета игрока и стрелка на миникарте, на 6 секунд.
- **Командная шкала**: убийство заполняет шкалу, только если за последние 5 секунд убивали минимум двое разных игроков. Полная шкала даёт всем +25% урона и +15% скорости на 10 секунд, следующий порог в 1,5 раза выше.
- **Кооп-испытания** (`ChallengesEnabled`), объявляются в начале уровня, награда: сундуки, которые падают с неба в начале следующего уровня (нокдаун смертью не считается):
  - по сундуку каждому, кто ни разу не умер на уровне;
  - по сундуку каждому, кто был жив при появлении босса уровня и не умер до его смерти.
- **Агр врагов** решает хост и распределяет его по всей пати:
  - обычные враги появляются рядом со случайным живым игроком и идут за ним, раз в 40–60 секунд могут переключиться на другого случайного игрока в пределах 50 м;
  - боссы уровня меняют цель раз в 20–40 секунд, финальный босс раз в 30–50 секунд, в пределах 300 м;
  - боссы, мини-боссы и другие враги, появляющиеся в заданном месте, сразу выбирают случайного живого игрока, а не хоста;
  - упавших и погибших враги не преследуют и переключаются на живых.

#### Восхождение (кооп-эндгейм)
Когда финальный босс убит, рядом с финальным порталом появляется второй. Финальный портал завершает забег как обычно (открытие персонажей, прогресс карты); портал **Восхождение** переносит всю пати на первый этап той же карты с сохранёнными билдами: восхождение 1, 2, 3...
- Каждое восхождение делает врагов сильнее, но не многочисленнее: здоровье врагов x1,5 (`AscensionEnemyHpGrowth`) и урон x1,25 (`AscensionEnemyDamageGrowth`) за уровень, с накоплением, и не больше 300 врагов на карте одновременно (`AscensionMaxEnemies`). Отключается параметром `AscensionEnabled`.
- **Проклятия**: после каждого восхождения каждый игрок сам выбирает одно из трёх случайных проклятий (1/2/3 или клик): у каждого персонажа свои слабости. Пати на это время стоит на паузе, как при повышении уровня. Проклятия копятся до конца забега.
- 26 проклятий: на один стат (здоровье, урон, скорость атаки, броня, уклонение, щит, вампиризм, удача...) и на несколько (криты, урон стихиями, усиления, золото и серебро). Сила каждой карточки случайна: слабое (x0,6, 45%), обычное (40%) или тяжёлое (x1,6, 15%). Проклятие снимает процент от того, что есть у игрока, поэтому билду без этого стата оно не мешает. Один стат теряет от проклятий не больше 75%.
- **Разломы**: с первого восхождения на каждом этапе, кроме арены финального босса, через 60 секунд открывается разлом, следующий через 3 минуты после конца предыдущего, случайного вида, на всё 120 секунд:
  - зоны для удержания (по одной на игрока, не больше двух; фиолетовое кольцо): зона заполняется за 20 секунд, пока в ней стоит живой игрок не в нокдауне, быстрее, если игроков в ней больше;
  - осколки (по 3 на игрока, от 4 до 8; золотые): осколок подбирается, если подойти к нему;
  - буря (красный круг вокруг пати): круг сужается до 12 м за 60 секунд и держится ещё 30 секунд; снаружи игрок теряет 8% макс. HP в секунду. Провал, если кто-то погиб или к концу бури лежит в нокдауне.
  Они появляются в любом месте карты на ровной открытой земле, с лучом в небо и кольцом, видными сквозь стены на любой дистанции, и стрелкой на миникарте. Разлом не добавляет врагов. Закрыт: каждый выбирает **пакт**, дар (+15–40% к стату) ценой слабого или обычного проклятия. Провален: каждый выбирает проклятие.
- Только в коопе, решает хост: портал клиента отправляет запрос хосту.

#### Модификаторы забега
Хост отмечает любое их число в **Netplay Options** на экране выбора персонажа (или задаёт `RunModifiers` в конфиге). Они действуют на всю пати, в начале забега приходит уведомление.

| Модификатор | Эффект |
| --- | --- |
| Glass Cannon | Урон +50%, здоровье -40% |
| Horde | Здоровье врагов +50%, опыт +25%, золото +15% |
| Thick Skinned | Здоровье врагов x2, удача +0,25 |
| Frenzy | Каждые 5 минут враги на 10% быстрее, золото +20% |
| Lucky | Удача +0,3, сложность +0,3 |
| Poverty | Золото -50%, опыт +15% |
| Colossus | Здоровье врагов +50%, опыт +25% |
| Brittle Swarm | Здоровье врагов -40%, урон врагов +40%, опыт -10% |
| Bloodbath | Урон врагов +50%, лечение -50%, золото +25% |
| Pinball | Отбрасывание x2, скорость +15% |
| Silver Rush | Серебро +50%, урон врагов +25% |
| Overdrive | Скорость +20%, скорость снарядов +25%, враги на 20% быстрее |
| Shockwave | Область оружия +25%, урон -20% |
| Rising Tide | Враги усиливаются на 50% быстрее, сундуки на 25% дешевле |

Бонусы нескольких модификаторов складываются, но с потолками, чтобы вместе они давали тяжёлый забег, а не имбу: прирост опыта не больше +50%, удача +0,5, золото +50%, урон +50%, скорость +30%. Штрафы не ограничены.

#### Удобство
- Netplay Options (кнопка хоста на экране выбора персонажа): пауза при выборе награды, нокдаун, командная шкала, кооп-испытания, режим общей удачи и сложности и модификаторы забега. Значения хоста действуют на всех и рассылаются заново при изменении. Числовые параметры (таймеры, множители) только в конфиге.
- Таблица команды в конце забега: урон, убийства и золото каждого игрока (экран поражения).
- Музыка: треки мода играют в случайном порядке вместо музыки уровня, в главном меню свой трек; музыка босса остаётся игровой. В коопе у всех играет трек хоста. Отключается параметром `CustomMusic` в конфиге.
- Имя игрока, клавиши и все правила пати в `BepInEx\config\MegaphonkMod.cfg`.
- Уведомление о новой версии на GitHub (`CheckForUpdates`) и окно со списком изменений. Сам мод ничего не скачивает и не устанавливает.
- Тексты мода следуют языку игры: английский и русский (в остальных языках английский).
- Отладка (`DebugTools`, выключена; включает хост для всей пати): портал первого этапа ведёт к финальному боссу, F10 у хоста убивает всех врагов, F9 у хоста открывает разлом.

### Онлайн-сервисы и личные данные

Кроме самой игры, мод обращается к:
- **Steam** — для комнат (лобби Steam и релеи Steam между игроками).
- **GitHub** — чтобы сообщить о новой версии и показать список изменений (`CheckForUpdates`). Мод только читает последний релиз, ничего не скачивает и не запускает.
- **Кооп-лидерборду Megaphonk** (`https://megaphonk-leaderboard.megaphonk-leaderboard.workers.dev`, его держит автор мода) — только с твоего согласия. Мод один раз спрашивает в главном меню (`ShareCoopRuns` в конфиге: `Ask`, `Yes` или `No`). При `Yes` завершённый кооп-забег отправляет твоё имя и Steam ID (подтверждённый сессионным тикетом Steam), персонажа и убийства, карту, уровень сложности, модификаторы и испытание забега. Забег попадёт в таблицу, только если все его игроки ответили `Yes`. Вкладка друзей в лидерборде отправляет Steam ID твоих друзей, чтобы показать их забеги, — тоже только при `Yes`. Просмотр общей таблицы ничего о тебе не отправляет.

Музыку мода сделал автор мода в Suno (платный аккаунт).

### Установка
Через менеджер модов: установи [MegaphonkMod из Thunderstore](https://thunderstore.io/c/megabonk/p/ROFL_MODS/MegaphonkMod/) через r2modman или Gale, менеджер сам поставит BepInEx и будет обновлять мод. Ручная установка:

1. Закрой игру.
2. Скачай `MegaphonkMod-2.10.0-full.zip` из [последнего релиза](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
3. Распакуй в папку игры (Steam → Megabonk → Управление → Просмотреть локальные файлы) с заменой файлов. Рядом с `Megabonk.exe` должны лежать `winhttp.dll`, `doorstop_config.ini`, `BepInEx` и `dotnet`.
4. Запусти игру через Steam. Первый запуск дольше обычного.

У всех в комнате должна быть одна версия мода, а игроки должны быть в списке друзей Steam у хоста. Архив `MegaphonkMod-X.Y.Z.zip` содержит только файлы мода, чтобы обновить уже установленный мод вручную.

Версии нумеруются по [семантическому версионированию](https://semver.org/lang/ru/): X.Y.**Z** для исправлений, X.**Y**.0 для новых возможностей, **X**.0.0 для изменений, ломающих совместимость (например, игроки с разными первыми цифрами версии не смогут играть вместе).

### Как играть
- Хост: MEGAPHONK! → Host, выбрать персонажа, при желании настроить Netplay Options и дождаться друзей или начать одному.
- Остальные: MEGAPHONK! → Join → выбрать хоста в списке.
- Войти можно только на экране выбора персонажа, не во время забега.

### Ограничения
- Нет приглашений через оверлей Steam и кнопки «Присоединиться к игре»: нужные им колбэки Steam перехватывает сама игра.
- Случайный подбор игроков отключён: его публичный сервер используют игроки с несовместимым модом.
- Вход через список друзей новый и проверен только на одном ПК. Если друг не появился в списке, пришли `BepInEx\LogOutput.log` в issue.
- В соло-забеге нет нокдауна (поднять некому) и командной шкалы (ей нужны убийства двух игроков).

### Благодарности
За основу взят мод [Megabonk Together](https://github.com/Fcornaire/megabonk-together) (автор Fcornaire, лицензия GPL-2.0). Megaphonk Mod распространяется под той же лицензией.
