// How to play: a short guide from the main menu. What the game is, the two ways to play it,
// staying alive, and the keys. Plain text; kept here so the menu stays about menus.

export const HOW_TO_PLAY: readonly { heading: string; lines: readonly string[] }[] = [
  {
    heading: 'What this is',
    lines: [
      'The dead walk. You are one survivor in a dying country: find food and water, stay warm and rested, and keep away from the dead, or fight them when you must.',
      'If you settle, the people you find can make a colony: they take jobs by priority, build, farm, cook and stand guard.',
    ],
  },
  {
    heading: 'Playing one survivor',
    lines: [
      'Select someone and press F to take control (F again lets go). The screen shows only what they would see: G brings out the colony management (build, priorities, overview, neighbours) and puts it away.',
      'WASD walk, Shift run, C sneak. The mouse aims; click to swing or shoot. A swing at a window breaks it (loudly).',
      'E or right-click uses what is in front of you: search a fridge or a wardrobe, eat, drink from a tap, sleep in a bed, cook at an oven while the power lasts, get in a car.',
      'R works the tile in front: repair, barricade a door or board a window with wood, build a wall.',
      'T talks to people nearby. V asks your people to follow you, H to hold where they are, L to loot the room under the mouse.',
      '1-9 use what is in your bag. K shows every key.',
    ],
  },
  {
    heading: 'Running the colony',
    lines: [
      'Left click selects, right click moves. B opens the build bar: walls, doors, furniture, rooms, and areas to scavenge, explore or farm. Shift places instantly where the tool allows.',
      'P shows your people: fold the list away, or open it out to set each job priority (1 first, 4 last, - never). O is the overview of the colony.',
      'Rooms need a purpose and what goes with it (a bedroom needs beds). An incomplete room says what it lacks.',
    ],
  },
  {
    heading: 'Staying alive',
    lines: [
      'Hunger, thirst, rest and warmth all count. Wounds bleed until bandaged; bites can turn.',
      'The taps and the power go off in the first weeks. After that, water is wells, rain collectors, what is left in toilets and baths, or the river (which can make you sick). Fridges go warm and their food rots.',
      'The dead hear noise and see you in the open. Sneak, stay out of their sight, keep doors shut. They batter down what stands between you.',
      'Wolves give a warning bite if you crowd them, and come for you when starving; hurt one and the pack comes. Bears maul anyone close. Rabbits and deer are food.',
      'Neighbouring settlements trade, ask favours, send scavengers and caravans out, and remember how you treated them.',
    ],
  },
  {
    heading: 'The view',
    lines: ['Space pauses (plan freely while paused); 1x, 2x and 4x speed. M is the map (pin notes on it), N the minimap, Home recentres, the wheel zooms. Esc opens this menu.'],
  },
];
