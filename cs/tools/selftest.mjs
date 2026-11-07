// Runs the Godot game's self-test (godot/SelfTest.cs): real input events through the game, 15 checks.
// A window opens for a few seconds. Godot is GODOT (env), else the usual Windows install.
// Run: npm run selftest
import { spawnSync } from 'node:child_process';

const godot = process.env.GODOT ?? 'C:/Program Files/Godot_v4.6.1-stable_mono_win64/Godot_v4.6.1-stable_mono_win64_console.exe';
const build = spawnSync('dotnet', ['build', 'godot/LoonieRoo.csproj', '-v', 'q', '-nologo'], { stdio: 'inherit' });
if (build.status !== 0) process.exit(build.status ?? 1);
const run = spawnSync(godot, ['--path', 'godot', '--', '--selftest'], { encoding: 'utf8', timeout: 180_000 });
const lines = `${run.stdout}\n${run.stderr}`.split('\n').filter((l) => /^(PASS|FAIL|selftest)/.test(l));
console.log(lines.join('\n') || `no self-test output (Godot at ${godot}?)`);
process.exit(run.status ?? 1);
