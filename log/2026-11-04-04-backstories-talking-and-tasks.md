- **2026-11-04, backstories, talking and tasks:** every survivor has a `story` (story.ts), made from a hash of their id (no RNG draw): what they did before, what they're proud of, someone they lost (dead or missing), maybe a dog left behind, and a regret tied to a place. Talking (T next to someone while controlling, or the Talk button in the selected survivor's Story panel; once every 2 hours each) tells the next line and warms you both (+5). Once they've told enough they ask for one thing at a time (`state.tasks`, talk.ts), at a spot 20-45 tiles off, generated if need be and marked on the map with a gold diamond:
  - home: go and see their old place (a crate with a few finds); done when they or you get within 2 tiles.
  - bury: their dead (a corpse with the name) carried into a graveyard.
  - pet: their dog, sitting at the house until someone comes within 2 tiles; then it joins the colony's dogs.
  - burn: set a fire at the place of their regret.
  Done: a thought for days (+10 to +20) and +25 opinion of whoever you control. A task fails if the corpse or dog is gone, and is dropped if whoever asked is. Schema 52.
  Not yet: tasks from survivors recognising each other, confessions, medicine for a friend elsewhere, a radio call to a relative's town; commitment (talks and tasks are the obvious way to raise it).
