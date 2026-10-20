# Megaphonk Mod

Co-op mod for **Megabonk 1.0.69** (up to 6 players), built on BepInEx 6 IL2CPP.

[Русская версия ниже](#на-русском)

## What the mod adds

### Co-op
- Online co-op for up to 6 players: **MEGAPHONK!** button in the main menu.
- Friendlies rooms through **Steam**: no matchmaking server, the room code is the host's Steam account (5–7 characters, use the Copy Code button).
- The host runs the game: enemies, damage and the clients' weapons, computed with each client's real stats.
- Optional shared experience: one level for the whole party.
- Mod version check: a player with another build cannot join and is told why.

### Rewards
- While anyone picks a reward (level up, chest, shrine), the game pauses for everyone. `RewardPauseMode = Own` turns the shared pause off.
- **F8** unlocks a stuck pause for everyone.
- Clicks are ignored for the first second after a reward window appears, so nobody picks by accident.
- A notification shows what a teammate took.

### Chests and gold
- A chest bought by one player opens for everyone, only the buyer pays.
- Chest price grows with the number of players.
- Gold is per player.

### Stats
- Character passive bonuses (the ones that grow with the character's level) apply to the whole party across the whole map, and the same characters stack.
- Party luck and difficulty: sum, highest + half of the others, or average of all players.
- Damage and kills count for the player who dealt them; item effects trigger for their owner.

### New mechanics
- **Downed state**: lethal damage downs a player, a teammate standing next to them revives them.
- **Ping**: the middle mouse button marks a spot for the whole team (beam and minimap arrow).
- **Team meter**: kills made together fill a meter, a full meter gives damage and speed to everyone.
- **Run modifiers**: Glass Cannon, Horde, Thick Skinned, Frenzy, Lucky, Poverty.
- **Co-op challenges** with reward chests: "stage without deaths", "boss with everyone alive".
- Enemy HP scales with the number of players.

### Convenience
- Team table at the end of a run: damage, kills and gold of each player.
- Party rules in the menu (MEGAPHONK! → Netplay Options), all values in the config file.
- Fixes for Megabonk 1.0.69: startup crashes, stuck pauses, enemies wandering off.

## Installation

1. Close the game.
2. If you used **Megabonk Together**, delete the `BepInEx\plugins\MegabonkTogether` folder from the game folder: both mods must not be installed together.
3. Download `MegaphonkMod-1.0.0-full.zip` from the [latest release](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
4. Extract it into the game folder (Steam → Megabonk → Manage → Browse local files), replacing files. The folder must contain `winhttp.dll`, `doorstop_config.ini`, `BepInEx` and `dotnet` next to `Megabonk.exe`.
5. Start the game through Steam. The first launch takes longer while BepInEx prepares the game.

Every player of a room needs the same mod version. The `MegaphonkMod-X.Y.Z.zip` asset contains only the mod files: the mod uses it to update itself.

Settings: `BepInEx\config\MegaphonkMod.cfg` (created on the first launch). The host's party settings apply to everyone.

## Playing

- Host: MEGAPHONK! → Friendlies → Host, then share the room code.
- Others: MEGAPHONK! → Friendlies → enter the code → Join.

Random matchmaking is disabled: it relies on the Megabonk Together server, whose players run an incompatible mod.

## Credits

Megaphonk Mod is based on [Megabonk Together](https://github.com/Fcornaire/megabonk-together) by Fcornaire, licensed under GPL-2.0. This mod is distributed under the same license.

---

## На русском

Кооп-мод для **Megabonk 1.0.69** до 6 игроков, на BepInEx 6 IL2CPP.

### Что добавляет мод
- **Кооп** до 6 игроков: кнопка **MEGAPHONK!** в главном меню. Комнаты Friendlies идут через Steam, без сервера: код комнаты — Steam-аккаунт хоста. Хост ведёт игру и считает оружие клиентов с их настоящими статами. Общий опыт по желанию. Игрока с другой версией мода не пустят в комнату и объяснят почему.
- **Награды:** пока кто-то выбирает награду, пауза у всех (`RewardPauseMode = Own` отключает общую паузу). F8 снимает зависшую паузу. Первую секунду клики в окне награды игнорируются. Уведомление о том, что взял напарник.
- **Сундуки и золото:** купленный сундук открывается у всех, платит покупатель. Цена растёт с числом игроков. Золото у каждого своё.
- **Статы:** пассивки персонажей действуют на всю пати по всей карте. Общие удача и сложность. Урон, убийства и эффекты предметов засчитываются их владельцу.
- **Механики:** нокдаун и поднятие напарником, пинг средней кнопкой мыши, командная шкала, модификаторы забега, кооп-испытания с сундуками, здоровье врагов растёт с числом игроков.
- **Удобство:** таблица команды в конце забега, правила пати в меню (MEGAPHONK! → Netplay Options), исправления под Megabonk 1.0.69.

### Установка
1. Закрой игру.
2. Если стоял **Megabonk Together**, удали папку `BepInEx\plugins\MegabonkTogether` в папке игры: оба мода вместе стоять не должны.
3. Скачай `MegaphonkMod-1.0.0-full.zip` из [последнего релиза](https://github.com/coffeammaretto/megaphonk-mod/releases/latest).
4. Распакуй в папку игры (Steam → Megabonk → Управление → Просмотреть локальные файлы) с заменой файлов.
5. Запусти игру через Steam. Первый запуск дольше обычного.

У всех в комнате должна быть одна версия мода. Настройки: `BepInEx\config\MegaphonkMod.cfg`.

### Как играть
- Хост: MEGAPHONK! → Friendlies → Host, передать код комнаты.
- Остальные: MEGAPHONK! → Friendlies → ввести код → Join.

### Благодарности
За основу взят мод [Megabonk Together](https://github.com/Fcornaire/megabonk-together) (автор Fcornaire, лицензия GPL-2.0). Megaphonk Mod распространяется под той же лицензией.
