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
- Shared experience (on by default): one level for the whole party, and a reward window pauses the game for everyone.
- Saving progression works during netplay (on by default).
- Mod version check: a player with another build cannot join and is told why.
- Fixes for Megabonk 1.0.69: startup crashes, stuck pauses, enemies wandering off, instant coin and XP pickup, the teleport to the final boss leaving everyone stuck on "Waiting for other players".

### Network and performance
- The network messages of a frame are sent as one packet instead of hundreds of separate ones, so the late game no longer stutters when lots of XP, gold and enemies spawn and die. Big snapshots are split.
- The host merges damage per enemy and per player each frame before sending it: clients see fewer, bigger damage numbers.
- XP is synchronized once per frame, not on every orb.
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

### Run modifiers
The host ticks any number of them in **Netplay Options** on the character selection (or sets `RunModifiers` in the config). They apply to the whole party, and a notification announces them at the start of a run.

| Modifier | Effect |
| --- | --- |
| Glass Cannon | Damage +50%, max HP -40% |
| Horde | Enemies x1.5 (more XP and gold from kills) |
| Thick Skinned | Enemy HP x2, luck +0.25 |
| Frenzy | Enemies get 10% faster every 5 minutes, gold +20% |
| Lucky | Luck +0.3, difficulty +0.3 |
| Poverty | Gold -50%, XP +15% |
| Colossus | Enemy HP +50%, XP +25% |
| Brittle Swarm | Enemy HP -40%, enemies x1.5, XP -20% |
| Bloodbath | Enemy damage +50%, healing -50%, gold +25% |
| Pinball | Knockback x2, move speed +15% |
| Silver Rush | Silver +50%, enemy damage +25% |
| Overdrive | Move speed +20%, projectile speed +25%, enemies 20% faster |
| Shockwave | Weapon area +25%, damage -20% |
| Rising Tide | Enemies grow stronger 50% faster, chests 25% cheaper |

Bonuses of several modifiers add up, with caps so that stacking them makes a harder run rather than an overpowered character: XP gain at most +50% counting the XP of the extra enemies (with Horde or Brittle Swarm the XP bonus goes down, even below zero), luck +0.5, gold +50%, damage +50%, move speed +30%. Penalties are not capped.

### Convenience
- Netplay Options (the host's button on the character selection): reward pause, downed state, team meter, co-op challenges, party luck/difficulty mode and the run modifiers. The host's values apply to everyone and are resent when changed. Numeric values (timers, multipliers) are in the config file.
- Team table at the end of a run: damage, kills and gold of each player (defeat screen).
- Player name, key bindings and every party rule in `BepInEx\config\MegaphonkMod.cfg`.
- Automatic check for new versions on GitHub (`CheckForUpdates`) and a changelog window.
- The mod's texts follow the game's language: English and Russian (other languages show English).

## Installation

1. Close the game.
2. If you used **Megabonk Together**, delete the `BepInEx\plugins\MegabonkTogether` folder from the game folder: both mods must not be installed together.
3. Download `MegaphonkMod-2.0.2-full.zip` from the [latest release](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
4. Extract it into the game folder (Steam → Megabonk → Manage → Browse local files), replacing files. The folder must contain `winhttp.dll`, `doorstop_config.ini`, `BepInEx` and `dotnet` next to `Megabonk.exe`.
5. Start the game through Steam. The first launch takes longer while BepInEx prepares the game.

Every player of a room needs the same mod version and must be on the host's Steam friends list. The `MegaphonkMod-X.Y.Z.zip` asset contains only the mod files: the mod uses it to update itself.

Versions follow [semantic versioning](https://semver.org): X.Y.**Z** for fixes, X.**Y**.0 for new features, **X**.0.0 for changes that break compatibility (for example, players on different major versions cannot play together).

## Playing

- Host: MEGAPHONK! → Host, pick a character, adjust Netplay Options if you want, wait for your friends, or start alone.
- Others: MEGAPHONK! → Join → pick the host from the list.
- Joining is possible only on the character selection, not during a run.

## Limitations

- No Steam overlay invites or "Join Game" button: the game consumes the Steam callbacks they need.
- Random matchmaking is disabled: it relied on the Megabonk Together server, whose players run an incompatible mod.
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
- Общий опыт (включён по умолчанию): один уровень на всю пати, а окно награды ставит игру на паузу у всех.
- Сохранение прогресса работает во время коопа (включено по умолчанию).
- Проверка версии: игрока с другой версией мода не пустят в комнату и объяснят почему.
- Исправления под Megabonk 1.0.69: вылеты при запуске, зависшие паузы, уходящие враги, мгновенный подбор монет и опыта, зависание всех на «Ждём других игроков» при телепорте к финальному боссу.

#### Сеть и производительность
- Сетевые сообщения за кадр уходят одним пакетом, а не сотнями отдельных: поздняя игра больше не фризит, когда появляется и умирает много опыта, золота и врагов. Большие снимки состояния режутся на части.
- Хост сливает урон по каждому врагу от каждого игрока за кадр перед отправкой: у клиентов меньше цифр урона, но они крупнее.
- Опыт синхронизируется раз в кадр, а не на каждую сферу.
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

#### Модификаторы забега
Хост отмечает любое их число в **Netplay Options** на экране выбора персонажа (или задаёт `RunModifiers` в конфиге). Они действуют на всю пати, в начале забега приходит уведомление.

| Модификатор | Эффект |
| --- | --- |
| Glass Cannon | Урон +50%, здоровье -40% |
| Horde | Врагов x1,5 (больше опыта и золота с убийств) |
| Thick Skinned | Здоровье врагов x2, удача +0,25 |
| Frenzy | Каждые 5 минут враги на 10% быстрее, золото +20% |
| Lucky | Удача +0,3, сложность +0,3 |
| Poverty | Золото -50%, опыт +15% |
| Colossus | Здоровье врагов +50%, опыт +25% |
| Brittle Swarm | Здоровье врагов -40%, врагов x1,5, опыт -20% |
| Bloodbath | Урон врагов +50%, лечение -50%, золото +25% |
| Pinball | Отбрасывание x2, скорость +15% |
| Silver Rush | Серебро +50%, урон врагов +25% |
| Overdrive | Скорость +20%, скорость снарядов +25%, враги на 20% быстрее |
| Shockwave | Область оружия +25%, урон -20% |
| Rising Tide | Враги усиливаются на 50% быстрее, сундуки на 25% дешевле |

Бонусы нескольких модификаторов складываются, но с потолками, чтобы вместе они давали тяжёлый забег, а не имбу: прирост опыта не больше +50% с учётом опыта лишних врагов (с Ордой или Хрупким роем бонус к опыту снижается, вплоть до минуса), удача +0,5, золото +50%, урон +50%, скорость +30%. Штрафы не ограничены.

#### Удобство
- Netplay Options (кнопка хоста на экране выбора персонажа): пауза при выборе награды, нокдаун, командная шкала, кооп-испытания, режим общей удачи и сложности и модификаторы забега. Значения хоста действуют на всех и рассылаются заново при изменении. Числовые параметры (таймеры, множители) только в конфиге.
- Таблица команды в конце забега: урон, убийства и золото каждого игрока (экран поражения).
- Имя игрока, клавиши и все правила пати в `BepInEx\config\MegaphonkMod.cfg`.
- Автоматическая проверка новых версий на GitHub (`CheckForUpdates`) и окно со списком изменений.
- Тексты мода следуют языку игры: английский и русский (в остальных языках английский).

### Установка
1. Закрой игру.
2. Если стоял **Megabonk Together**, удали папку `BepInEx\plugins\MegabonkTogether` в папке игры: оба мода вместе стоять не должны.
3. Скачай `MegaphonkMod-2.0.2-full.zip` из [последнего релиза](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
4. Распакуй в папку игры (Steam → Megabonk → Управление → Просмотреть локальные файлы) с заменой файлов. Рядом с `Megabonk.exe` должны лежать `winhttp.dll`, `doorstop_config.ini`, `BepInEx` и `dotnet`.
5. Запусти игру через Steam. Первый запуск дольше обычного.

У всех в комнате должна быть одна версия мода, а игроки должны быть в списке друзей Steam у хоста. Архив `MegaphonkMod-X.Y.Z.zip` содержит только файлы мода: им мод обновляет сам себя.

Версии нумеруются по [семантическому версионированию](https://semver.org/lang/ru/): X.Y.**Z** для исправлений, X.**Y**.0 для новых возможностей, **X**.0.0 для изменений, ломающих совместимость (например, игроки с разными первыми цифрами версии не смогут играть вместе).

### Как играть
- Хост: MEGAPHONK! → Host, выбрать персонажа, при желании настроить Netplay Options и дождаться друзей или начать одному.
- Остальные: MEGAPHONK! → Join → выбрать хоста в списке.
- Войти можно только на экране выбора персонажа, не во время забега.

### Ограничения
- Нет приглашений через оверлей Steam и кнопки «Присоединиться к игре»: нужные им колбэки Steam перехватывает сама игра.
- Случайный подбор игроков отключён: он работал через сервер Megabonk Together, игроки которого используют несовместимый мод.
- Вход через список друзей новый и проверен только на одном ПК. Если друг не появился в списке, пришли `BepInEx\LogOutput.log` в issue.
- В соло-забеге нет нокдауна (поднять некому) и командной шкалы (ей нужны убийства двух игроков).

### Благодарности
За основу взят мод [Megabonk Together](https://github.com/Fcornaire/megabonk-together) (автор Fcornaire, лицензия GPL-2.0). Megaphonk Mod распространяется под той же лицензией.
