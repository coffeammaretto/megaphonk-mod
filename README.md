# Megaphonk Mod

Co-op mod for **Megabonk 1.0.69** (up to 6 players), built on BepInEx 6 IL2CPP.

[Русская версия ниже](#на-русском)

## What the mod adds

### Playing together
- Online co-op for up to 6 players: **MEGAPHONK!** button in the main menu.
- Rooms through **Steam**, no matchmaking server and no room codes:
  - **Host**: MEGAPHONK! → Host. The character selection opens right away, friends can join until the run starts.
  - **Join**: MEGAPHONK! → Join → pick a friend from the list. The list shows Steam friends who have the game running and a room open, and refreshes every 2 seconds.
- The host runs the game: enemies, damage and the clients' weapons, computed with each client's real stats.
- Shared experience (on by default): one level for the whole party, and a reward window pauses the game for everyone.
- Saving progression works during netplay (on by default).
- Mod version check: a player with another build cannot join and is told why.
- Fixes for Megabonk 1.0.69: startup crashes, stuck pauses, enemies wandering off, lag in long runs (big packets are split), instant coin and XP pickup.

### Rewards
- While anyone picks a reward (level up, chest, shrine), the game pauses for everyone. `RewardPauseMode = Own` turns the shared pause off: you pick without a pause and stay invulnerable meanwhile, the others keep playing.
- **F8** unlocks a stuck pause for everyone.
- Clicks are ignored for the first second after a reward window appears, so nobody picks by accident.
- A notification shows what a teammate took (weapon, item or tome), so builds can be coordinated. `ShowTeammatePicks` turns it off.
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
- **Downed state**: lethal damage downs a player instead of killing them. They bleed out for 25 seconds, invulnerable but unable to attack and 60% slower, and enemies ignore them. A teammate standing within 4 meters revives them in 3 seconds with 30% HP. Nobody came in time: a normal death and a grave. Screen hints show the timer and the distance to a downed teammate.
- **Ping**: the middle mouse button (`PingKey`) marks a spot for the whole team: a beam of the player's color and an arrow on the minimap, for 6 seconds.
- **Team meter**: a kill fills the meter only when at least two different players killed within the last 5 seconds. A full meter gives everyone +25% damage and +15% move speed for 10 seconds, then the next threshold is 1.5 times higher.
- **Co-op challenges** (`ChallengesEnabled`), announced at the start of a stage, rewarded with a chest that drops from the sky:
  - a stage without anyone dying: a bonus chest on the next stage;
  - a stage boss killed with everyone alive: a chest where it died.
- Enemies no longer chase a dead player.

### Run modifiers
The host ticks any number of them in **Netplay Options** on the character selection (or sets `RunModifiers` in the config). They apply to the whole party, and a notification announces them at the start of a run.

| Modifier | Effect |
| --- | --- |
| Glass Cannon | Damage x2, max HP x0.5 |
| Horde | Enemies x1.5, XP +25% |
| Thick Skinned | Enemy HP x2, luck +0.25 |
| Frenzy | Enemies get 10% faster every 5 minutes |
| Lucky | Luck +0.5, difficulty +0.3 |
| Poverty | Gold -50%, XP +25% |
| Colossus | Enemy HP +50%, XP +50% |
| Brittle Swarm | Enemy HP -40%, enemies x1.5 |
| Bloodbath | Enemy damage +50%, healing -50%, gold +25% |
| Pinball | Knockback x2, move speed +15% |

### Convenience
- Netplay Options (the host's button on the character selection): reward pause, downed state, team meter, co-op challenges, party luck/difficulty mode and the run modifiers. The host's values apply to everyone and are resent when changed. Numeric values (timers, multipliers) are in the config file.
- Team table at the end of a run: damage, kills and gold of each player (defeat screen).
- Player name, key bindings and every party rule in `BepInEx\config\MegaphonkMod.cfg`.
- Automatic check for new versions on GitHub (`CheckForUpdates`) and a changelog window.
- The mod's texts follow the game's language: English and Russian (other languages show English).

## Installation

1. Close the game.
2. If you used **Megabonk Together**, delete the `BepInEx\plugins\MegabonkTogether` folder from the game folder: both mods must not be installed together.
3. Download `MegaphonkMod-1.0.0-full.zip` from the [latest release](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
4. Extract it into the game folder (Steam → Megabonk → Manage → Browse local files), replacing files. The folder must contain `winhttp.dll`, `doorstop_config.ini`, `BepInEx` and `dotnet` next to `Megabonk.exe`.
5. Start the game through Steam. The first launch takes longer while BepInEx prepares the game.

Every player of a room needs the same mod version and must be on the host's Steam friends list. The `MegaphonkMod-X.Y.Z.zip` asset contains only the mod files: the mod uses it to update itself.

## Playing

- Host: MEGAPHONK! → Host, pick a character, adjust Netplay Options if you want, wait for your friends.
- Others: MEGAPHONK! → Join → pick the host from the list.
- Joining is possible only on the character selection, not during a run.

## Limitations

- No Steam overlay invites or "Join Game" button: the game consumes the Steam callbacks they need.
- Random matchmaking is disabled: it relied on the Megabonk Together server, whose players run an incompatible mod.
- Joining through the friends list is new and has been tested on one PC only. If a friend does not show up, send `BepInEx\LogOutput.log` in an issue.

## Credits

Megaphonk Mod is based on [Megabonk Together](https://github.com/Fcornaire/megabonk-together) by Fcornaire, licensed under GPL-2.0. This mod is distributed under the same license.

---

## На русском

Кооп-мод для **Megabonk 1.0.69** до 6 игроков, на BepInEx 6 IL2CPP.

### Что добавляет мод

#### Игра вместе
- Онлайн-кооп до 6 игроков: кнопка **MEGAPHONK!** в главном меню.
- Комнаты через **Steam**, без сервера и без кодов:
  - **Хост**: MEGAPHONK! → Host. Сразу открывается выбор персонажа, друзья могут войти до старта забега.
  - **Вход**: MEGAPHONK! → Join → выбрать друга в списке. В списке друзья из Steam, у которых запущена игра и открыта комната, список обновляется раз в 2 секунды.
- Хост ведёт игру: врагов, урон и оружие клиентов, которое считается с их настоящими статами.
- Общий опыт (включён по умолчанию): один уровень на всю пати, а окно награды ставит игру на паузу у всех.
- Сохранение прогресса работает во время коопа (включено по умолчанию).
- Проверка версии: игрока с другой версией мода не пустят в комнату и объяснят почему.
- Исправления под Megabonk 1.0.69: вылеты при запуске, зависшие паузы, уходящие враги, лаги в длинных забегах (большие пакеты режутся на части), мгновенный подбор монет и опыта.

#### Награды
- Пока кто-то выбирает награду (уровень, сундук, святилище), игра на паузе у всех. `RewardPauseMode = Own` отключает общую паузу: выбираешь без паузы и неуязвим, остальные играют.
- **F8** снимает зависшую паузу у всех.
- Первую секунду после появления окна награды клики игнорируются, чтобы не выбрать случайно.
- Уведомление о том, что взял напарник (оружие, предмет, том), чтобы координировать билды. `ShowTeammatePicks` отключает его.
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
- **Нокдаун**: смертельный урон не убивает, а роняет игрока. Он истекает кровью 25 секунд, неуязвим, но не может атаковать и ходит на 60% медленнее, враги его игнорируют. Напарник в радиусе 4 метров поднимает его за 3 секунды с 30% здоровья. Не успели: обычная смерть и могила. Подсказки на экране показывают таймер и расстояние до упавшего напарника.
- **Пинг**: средняя кнопка мыши (`PingKey`) ставит метку для всей команды: столб света цвета игрока и стрелка на миникарте, на 6 секунд.
- **Командная шкала**: убийство заполняет шкалу, только если за последние 5 секунд убивали минимум двое разных игроков. Полная шкала даёт всем +25% урона и +15% скорости на 10 секунд, следующий порог в 1,5 раза выше.
- **Кооп-испытания** (`ChallengesEnabled`), объявляются в начале уровня, награда: сундук, который падает с неба:
  - уровень без смертей: бонусный сундук на следующем уровне;
  - босс уровня убит при всех живых: сундук на месте босса.
- Враги больше не преследуют погибшего игрока.

#### Модификаторы забега
Хост отмечает любое их число в **Netplay Options** на экране выбора персонажа (или задаёт `RunModifiers` в конфиге). Они действуют на всю пати, в начале забега приходит уведомление.

| Модификатор | Эффект |
| --- | --- |
| Glass Cannon | Урон x2, здоровье x0,5 |
| Horde | Врагов x1,5, опыт +25% |
| Thick Skinned | Здоровье врагов x2, удача +0,25 |
| Frenzy | Каждые 5 минут враги на 10% быстрее |
| Lucky | Удача +0,5, сложность +0,3 |
| Poverty | Золото -50%, опыт +25% |
| Colossus | Здоровье врагов +50%, опыт +50% |
| Brittle Swarm | Здоровье врагов -40%, врагов x1,5 |
| Bloodbath | Урон врагов +50%, лечение -50%, золото +25% |
| Pinball | Отбрасывание x2, скорость +15% |

#### Удобство
- Netplay Options (кнопка хоста на экране выбора персонажа): пауза при выборе награды, нокдаун, командная шкала, кооп-испытания, режим общей удачи и сложности и модификаторы забега. Значения хоста действуют на всех и рассылаются заново при изменении. Числовые параметры (таймеры, множители) только в конфиге.
- Таблица команды в конце забега: урон, убийства и золото каждого игрока (экран поражения).
- Имя игрока, клавиши и все правила пати в `BepInEx\config\MegaphonkMod.cfg`.
- Автоматическая проверка новых версий на GitHub (`CheckForUpdates`) и окно со списком изменений.
- Тексты мода следуют языку игры: английский и русский (в остальных языках английский).

### Установка
1. Закрой игру.
2. Если стоял **Megabonk Together**, удали папку `BepInEx\plugins\MegabonkTogether` в папке игры: оба мода вместе стоять не должны.
3. Скачай `MegaphonkMod-1.0.0-full.zip` из [последнего релиза](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
4. Распакуй в папку игры (Steam → Megabonk → Управление → Просмотреть локальные файлы) с заменой файлов. Рядом с `Megabonk.exe` должны лежать `winhttp.dll`, `doorstop_config.ini`, `BepInEx` и `dotnet`.
5. Запусти игру через Steam. Первый запуск дольше обычного.

У всех в комнате должна быть одна версия мода, а игроки должны быть в списке друзей Steam у хоста. Архив `MegaphonkMod-X.Y.Z.zip` содержит только файлы мода: им мод обновляет сам себя.

### Как играть
- Хост: MEGAPHONK! → Host, выбрать персонажа, при желании настроить Netplay Options и дождаться друзей.
- Остальные: MEGAPHONK! → Join → выбрать хоста в списке.
- Войти можно только на экране выбора персонажа, не во время забега.

### Ограничения
- Нет приглашений через оверлей Steam и кнопки «Присоединиться к игре»: нужные им колбэки Steam перехватывает сама игра.
- Случайный подбор игроков отключён: он работал через сервер Megabonk Together, игроки которого используют несовместимый мод.
- Вход через список друзей новый и проверен только на одном ПК. Если друг не появился в списке, пришли `BepInEx\LogOutput.log` в issue.

### Благодарности
За основу взят мод [Megabonk Together](https://github.com/Fcornaire/megabonk-together) (автор Fcornaire, лицензия GPL-2.0). Megaphonk Mod распространяется под той же лицензией.
