CLICKDUNGEON - PLAYTEST BUILD
=============================

Thanks for playing! We're testing the game, not you. There is no wrong way to play.


1. START
--------
- Unzip the whole folder anywhere (your Desktop is fine). Keep all the files together.
- Double-click ClickDungeon.exe.
- If Windows shows "Windows protected your PC", click "More info", then "Run anyway".
  (This test build isn't signed.)


2. PLAY
-------
Play at least 3 runs. A run ends when your hero falls, or beats Lord Blobert on floor 20.

The dungeon is twenty floors in four acts of five. Every fifth floor is a boss - the Goblin
King, the Bat Swarm Leader, the Curtain Demon, then Lord Blobert. Beating one heals you fully
and opens the way down.

Pick a hero on the title screen: nine of them, each with a rule of its own (the card tells you
what it is). Ironheart the Knight is the steady one to start with. Sir Clickington is the
mascot, and plays as a Knight too.

When you press PLAY, pick a difficulty:
- SQUIRE'S STROLL (easy): start here if this is your first time.
- KNIGHT'S TRIAL (medium): the dungeon as designed.
- BLOBERT'S WRATH (hardcore): for when you've already beaten Lord Blobert.

Goal on each floor: find the KEY, then reach the EXIT. On a boss floor there is no key -
beat the boss and the exit opens. On some floors a goblin is carrying the key: chase it down.

Controls
- Click any tile to go there. Covered tiles hide what is on them until you click them.
- If a monster or a locked door is hiding under the tile you click, your hero
  stays where they are and the tile is uncovered, so you can see what it was.
- Click a monster next to you to SLASH it. The Wizard and the Ranger shoot instead, down
  any straight line they have already uncovered.
- Chests take 2 to 4 clicks to open, and every click is a turn. Click the chest while
  standing on it or next to it. Better chests take more clicks and give more.
  Not everything that looks like treasure is treasure.
- Click your own hero to wait a turn.
- SLASH and DASH buttons: pick the button, then pick a lit tile.
- SHIELD and POTION buttons act immediately.
- Keyboard: WASD or arrow keys to move, Space to wait, 1-5 for the buttons,
  Esc for the menu, H for help.

Want a slower, more careful game? Settings > MOVEMENT switches to STEP BY STEP for
your next run: you move one tile at a time, and tiles near you show clues.

Tips the game will also show you
- Monsters wake up when you uncover them, and always show what they'll do next turn.
- Most monsters must stand next to you to hit you, but not all:
    Fire Imp and Spooky Spellbook shoot along a straight line.
    Armored Boar charges down a whole line - the marked path is where it will run.
    Goblin Bomber lobs a lit bomb onto a tile; it goes off the turn after it lands.
    Cave Spider spits a web: caught, you cannot move or dash for one turn.
    Skeleton Warrior collapses into bones and gets back up if you leave it.
    Bosses slam from anywhere, call in help, and one of them vanishes and reappears.
- Tiles marked -N will be hit next turn. WEB, BOMB and ARRIVES mark what else is coming.
- If a warning and what actually happens ever disagree, that is a bug - please write it down.


3. PLAYTEST LOG
---------------
While you play, the game writes a small log of your moves to help us improve it.
- It stays on your computer. Nothing is sent over the internet.
- It contains no personal information: just moves, turns, what happened and the time of each move (to the second).
- You can switch it off in Settings > PLAYTEST LOG.


4. WHEN YOU'RE DONE
-------------------
Close the game, then double-click collect-logs.bat in this folder.
It creates ClickDungeon-playtest-logs.zip right here, next to the game.
The zip also holds the game's error log (Player.log), with your Windows user folder name removed.
Send that zip back to the person who gave you the game.

Prefer to do it by hand? The logs are in:
  %USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry


5. WATCH THE BOT (optional)
---------------------------
Double-click watch-bot.bat to watch the game's bot play five runs on its own,
at a pace you can follow. It never touches your save or your coins. Press Esc to
stop it. Handy for seeing a system you have not reached yet.


See VERSION.txt for the build version. Thank you!
