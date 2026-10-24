// Milestone 1 entry point: shows the ASCII map and installs window.game.
// The canvas view and placement tools arrive in milestone 2.

import { installGameHooks, type GameHandle } from './debug/hooks';
import { dumpText } from './debug/text';
import twoRooms from './scenarios/two-rooms.json';
import { loadScenario, type Scenario } from './sim/scenario';

const mapEl = document.getElementById('map');
if (!mapEl) throw new Error('missing #map element');

const handle: GameHandle = { state: loadScenario(twoRooms as Scenario) };

function render(): void {
  mapEl!.textContent = `tick ${handle.state.tick}  walkability v${handle.state.walkabilityVersion}\n\n${dumpText(handle.state)}`;
}

installGameHooks(handle, render);
render();
