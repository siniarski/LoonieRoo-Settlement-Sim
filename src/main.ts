// Entry point and hot-reload glue.
//
// main.ts accepts its own hot updates, and every other module is imported
// from here, so an edit anywhere (sim, render, ui) re-runs this file. On the
// way out we stash the sim state and view state in hot.data, so the map you
// were building survives the reload and only the code changes.

import { createApp, type App } from './app';
import sandbox from './scenarios/sandbox.json';
import { loadScenario, type Scenario } from './sim/scenario';
import type { SimState } from './sim/state';
import type { ViewState } from './ui/view';

interface HotData {
  state?: SimState;
  view?: ViewState;
}

const canvas = document.getElementById('view');
const hud = document.getElementById('hud');
if (!(canvas instanceof HTMLCanvasElement) || !hud) throw new Error('missing #view canvas or #hud');

const hot = import.meta.hot?.data as HotData | undefined;
const app: App = createApp(canvas, hud, hot?.state ?? loadScenario(sandbox as Scenario), hot?.view);

if (import.meta.hot) {
  import.meta.hot.accept();
  import.meta.hot.dispose((data: HotData) => {
    data.state = app.handle.state;
    data.view = app.view;
    app.dispose();
  });
}
