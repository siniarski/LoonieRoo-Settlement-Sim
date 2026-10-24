import { describe, expect, it } from 'vitest';
import { dumpRooms } from '../src/debug/text';
import { applyCommand } from '../src/sim/commands';
import { loadScenario, type Scenario } from '../src/sim/scenario';
import { completeStorageRooms, designationStatus, getRoomMap, reconcileRoomDesignations, roomAt } from '../src/sim/rooms';

const HOUSE = [
  '..........',
  '.#####.##.',
  '.#...#.#..',
  '.#...D.##.',
  '.#####....',
  '..........',
];

describe('room flood fill', () => {
  it('finds enclosed rooms; regions touching the edge are outdoors', () => {
    const state = loadScenario({ seed: 0, map: HOUSE });
    // The right-hand shape is open at (9,2) via the map edge... and through the gap at (8,2).
    expect(dumpRooms(state)).toBe(
      ['..........', '.#####.##.', '.#aaa#.#..', '.#aaaD.##.', '.#####....', '..........'].join('\n'),
    );
    const room = roomAt(state, 3, 2)!;
    expect(room.tiles).toHaveLength(6);
    expect(room.doors).toEqual([{ x: 5, y: 3 }]);
    expect([room.minX, room.minY, room.maxX, room.maxY]).toEqual([2, 2, 4, 3]);
  });

  it('doors split rooms', () => {
    const map = [
      '#######',
      '#..D..#',
      '#######',
    ];
    const state = loadScenario({ seed: 0, map });
    expect(dumpRooms(state)).toBe(['#######', '#aaDbb#', '#######'].join('\n'));
  });

  it('a room on the map edge counts as outdoors', () => {
    const state = loadScenario({ seed: 0, map: ['...#', '...#', '####'] });
    expect(getRoomMap(state).rooms).toHaveLength(0);
  });

  it('recomputes only when walls or doors change', () => {
    const state = loadScenario({ seed: 0, map: HOUSE });
    const before = getRoomMap(state);
    applyCommand(state, { type: 'designateBuild', x: 3, y: 2, structure: 'bed' }); // not a wall change
    expect(getRoomMap(state)).toBe(before);
    applyCommand(state, { type: 'placeWall', x: 3, y: 2 });
    const after = getRoomMap(state);
    expect(after).not.toBe(before);
    expect(after.rooms[0]!.tiles).toHaveLength(5);
  });

  it('closing a gap creates a room, opening one removes it', () => {
    const state = loadScenario({ seed: 0, map: HOUSE });
    expect(getRoomMap(state).rooms).toHaveLength(1);
    applyCommand(state, { type: 'placeWall', x: 9, y: 2 });
    expect(getRoomMap(state).rooms).toHaveLength(2);
    applyCommand(state, { type: 'clearTile', x: 1, y: 3 });
    expect(getRoomMap(state).rooms).toHaveLength(1);
  });
});

describe('room designations and requirements', () => {
  const scenario = (map: string[], rooms: NonNullable<Scenario['rooms']>): Scenario => ({ seed: 0, map, rooms });

  it('housing needs a door and a bed, with 4 tiles per bed', () => {
    const map = [
      '#######',
      '#.....#',
      '#.....#',
      '#######',
    ];
    const state = loadScenario(scenario(['.......', ...map, '.......'], [{ x: 2, y: 2, purpose: 'housing' }]));
    const unmet = () => designationStatus(state, state.roomDesignations[0]!).requirements.filter((r) => !r.met).map((r) => r.label);
    expect(unmet()).toEqual(['has a door', 'at least 1 bed']);
    applyCommand(state, { type: 'placeDoor', x: 3, y: 1 });
    applyCommand(state, { type: 'placeBed', x: 2, y: 2 });
    expect(unmet()).toEqual([]);
    expect(designationStatus(state, state.roomDesignations[0]!)).toMatchObject({ complete: true, beds: 1, size: 10 });
    for (const x of [3, 4]) applyCommand(state, { type: 'placeBed', x, y: 2 });
    expect(unmet()).toEqual(['4 tiles per bed']);
  });

  it('storage needs a door and 4 tiles', () => {
    const state = loadScenario(scenario(['.....', '.#D#.', '.#.#.', '.###.', '.....'], [{ x: 2, y: 2, purpose: 'storage' }]));
    const status = designationStatus(state, state.roomDesignations[0]!);
    expect(status.requirements.filter((r) => !r.met).map((r) => r.label)).toEqual(['at least 4 tiles']);
    expect(completeStorageRooms(state).size).toBe(0);
  });

  it('a designation outdoors is kept but reports not enclosed', () => {
    const state = loadScenario({ seed: 0, map: HOUSE, rooms: [{ x: 3, y: 2, purpose: 'storage' }] });
    applyCommand(state, { type: 'clearTile', x: 1, y: 3 });
    reconcileRoomDesignations(state);
    expect(state.roomDesignations).toHaveLength(1);
    const status = designationStatus(state, state.roomDesignations[0]!);
    expect(status.complete).toBe(false);
    expect(status.requirements[0]).toEqual({ label: 'enclosed by walls', met: false });
    // Closing it again restores the room and its purpose.
    applyCommand(state, { type: 'placeWall', x: 1, y: 3 });
    expect(designationStatus(state, state.roomDesignations[0]!).requirements[0]!.met).toBe(true);
  });

  it('when rooms merge, the older designation wins', () => {
    const map = [
      '.........',
      '.#######.',
      '.#..#..#.',
      '.###D###.',
      '.........',
    ];
    const state = loadScenario({
      seed: 0,
      map,
      rooms: [
        { x: 2, y: 2, purpose: 'housing' },
        { x: 5, y: 2, purpose: 'storage' },
      ],
    });
    applyCommand(state, { type: 'clearTile', x: 4, y: 2 });
    reconcileRoomDesignations(state);
    expect(state.roomDesignations.map((d) => d.purpose)).toEqual(['housing']);
  });

  it('an anchor that gets built over slides to a neighbouring floor tile', () => {
    const state = loadScenario({ seed: 0, map: HOUSE, rooms: [{ x: 3, y: 2, purpose: 'housing' }] });
    applyCommand(state, { type: 'placeWall', x: 3, y: 2 });
    reconcileRoomDesignations(state);
    expect(state.roomDesignations[0]).toMatchObject({ x: 4, y: 2, purpose: 'housing' });
  });

  it('designateRoom works inside rooms only, and re-designating changes the purpose', () => {
    const state = loadScenario({ seed: 0, map: HOUSE });
    expect(applyCommand(state, { type: 'designateRoom', x: 0, y: 0, purpose: 'housing' }).ok).toBe(false);
    expect(applyCommand(state, { type: 'designateRoom', x: 1, y: 1, purpose: 'housing' }).ok).toBe(false);
    expect(applyCommand(state, { type: 'designateRoom', x: 2, y: 2, purpose: 'housing' })).toEqual({ ok: true, changed: true });
    expect(applyCommand(state, { type: 'designateRoom', x: 4, y: 3, purpose: 'housing' })).toEqual({ ok: true, changed: false });
    expect(applyCommand(state, { type: 'designateRoom', x: 4, y: 3, purpose: 'storage' })).toEqual({ ok: true, changed: true });
    expect(state.roomDesignations.map((d) => d.purpose)).toEqual(['storage']);
    // Cancel on a room tile (no blueprint there) clears the purpose.
    expect(applyCommand(state, { type: 'cancelDesignation', x: 3, y: 3 })).toEqual({ ok: true, changed: true });
    expect(state.roomDesignations).toEqual([]);
  });
});
