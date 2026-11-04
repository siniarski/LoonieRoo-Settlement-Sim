// Entry point and hot-reload glue.
//
// main.ts accepts its own hot updates, and every other module is imported
// from here, so an edit anywhere (sim, render, ui) re-runs this file. On the
// way out we stash the sim state and view state in hot.data, so the map you
// were building survives the reload and only the code changes.

import { createApp, type App } from './app';
import { WORLD_SIZE, WORLD_SIZES, createWorld } from './sim/worldgen';
import { SCHEMA_VERSION, type SimState } from './sim/state';
import type { ViewState } from './ui/view';

interface HotData {
  state?: SimState;
  view?: ViewState;
}

const canvas = document.getElementById('view');
const hud = document.getElementById('hud');
if (!(canvas instanceof HTMLCanvasElement) || !hud) throw new Error('missing #view canvas or #hud');

const hot = import.meta.hot?.data as HotData | undefined;
// State saved by code with a different SimState shape is discarded, not migrated.
const kept = hot?.state?.schemaVersion === SCHEMA_VERSION ? hot.state : undefined;
if (hot?.state && !kept) console.info('[hmr] sim state shape changed; reloading the scenario');
// A fresh start opens on the menu, over a world to look at; a hot reload keeps you in your game.
const app: App = createApp(canvas, hud, kept ?? createWorld(worldSeed(), worldSize()), kept ? hot?.view : { menu: 'main', inGame: false, paused: true });

/** ?seed=123 in the URL picks the world; otherwise a random one (the UI may use Math.random; the sim never does). */
function worldSeed(): number {
  const fromUrl = Number(new URLSearchParams(location.search).get('seed'));
  return Number.isInteger(fromUrl) && fromUrl > 0 ? fromUrl : Math.floor(Math.random() * 1e9);
}

/** ?size=256 picks the world size (one of WORLD_SIZES); otherwise WORLD_SIZE. */
function worldSize(): number {
  const fromUrl = Number(new URLSearchParams(location.search).get('size'));
  return (WORLD_SIZES as readonly number[]).includes(fromUrl) ? fromUrl : WORLD_SIZE;
}

if (import.meta.hot) {
  import.meta.hot.accept();
  import.meta.hot.dispose((data: HotData) => {
    data.state = app.handle.state;
    data.view = app.view;
    app.dispose();
  });
}
