// Translates the TypeScript sim (src/sim/*.ts) into C# (cs/Sim/Generated/*.g.cs), one static class per module,
// keeping the TypeScript's names so the C# reads line for line against the original. The JavaScript library it
// leans on is in cs/Sim/JsCompat.cs (array and string methods as extension members, Math, truthiness...).
// Run: node cs/tools/ts2cs.mjs [module ...]   (no arguments: every module not ported by hand)
//
// Rules that keep behaviour the same:
//   - a / b is always floating-point division (JavaScript has no other);
//   - truthiness goes through Js.T(x), picked by static type (0, NaN, "", null are false);
//   - for...of over a list reads it by index as it goes (as JS does when the loop adds to it);
//   - sort is stable (as Array.prototype.sort); Map and Set keep insertion order (JsMap, JsSet);
//   - number is double, except where a name or a literal makes it a whole-number index (int): any mismatch is
//     a compile error to fix by hand, never a silent change.
// What it can't express is left as /*TODO ...*/ in the output, which won't compile: fixed by hand after.
import { parseAst } from 'rolldown/parseAst';
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { FIXUPS } from './fixups.mjs';

const SIM = 'src/sim';
const OUT = 'cs/Sim/Generated';
/** Modules ported by hand, and their C# class names. */
const HAND = { grid: 'Grids', pathfinding: 'Pathfinding', space: 'Space', rng: 'Rng', hash: 'Hash', time: 'Time', lookup: 'Lookup', savefile: 'Savefile' };
const renames = JSON.parse(readFileSync('cs/tools/typenames.json', 'utf8'));
const stateSrc = readFileSync('cs/Sim/State.g.cs', 'utf8');
/** Classes that are bodies (they implement IBody). */
const bodyClasses = new Set([...stateSrc.matchAll(/public partial class (\w+) : [^\n]*IBody/g)].map((m) => m[1]));

/** A state class by its exact set of field names ("a,b,c" sorted), when only one class has that set. */
const classByFields = new Map();
{
  const seen = new Map();
  for (const m of stateSrc.matchAll(/public partial class (\w+)[^\n]*\n\{([\s\S]*?)\n\}/g)) {
    const names = [...m[2].matchAll(/public [^\n]*? @?(\w+) \{ get; set; \}/g)].map((f) => f[1]).sort().join(',');
    seen.set(names, (seen.get(names) ?? 0) + 1);
    classByFields.set(names, m[1]);
  }
  for (const [k, n] of seen) if (n > 1) classByFields.delete(k);
}

/** State class -> its fields' C# types. */
const classFields = new Map();
const baseClass = new Map();
for (const m of stateSrc.matchAll(/public partial class (\w+)( : (\w+))?[^\n]*\n\{([\s\S]*?)\n\}/g)) {
  const fields = new Map();
  for (const f of m[4].matchAll(/public ([\w<>?, \[\]]+) @?(\w+) \{ get; set; \}/g)) fields.set(f[2], f[1]);
  classFields.set(m[1], fields);
  if (m[3]) baseClass.set(m[1], m[3]);
}
for (const [c, b] of baseClass) for (const [f, ft] of classFields.get(b) ?? []) if (!classFields.get(c).has(f)) classFields.get(c).set(f, ft);

{
  const grid = new Map();
  for (const f of readFileSync(new URL('../Sim/Grid.cs', import.meta.url), 'utf8').matchAll(/^    public ([\w<>?, \[\]]+) (\w+)( = [^;]*)?;/gm)) grid.set(f[2], f[1]);
  classFields.set('Grid', grid);
  classFields.set('Point', new Map([['x', 'int'], ['y', 'int']]));
  classFields.set('PointD', new Map([['x', 'double'], ['y', 'double']]));
}

/** Field name -> the C# types it has across the state classes. */
const fieldTypes = new Map();
for (const m of stateSrc.matchAll(/public ([\w<>?, \[\]]+) @?(\w+) \{ get; set; \}/g)) {
  if (!fieldTypes.has(m[2])) fieldTypes.set(m[2], new Set());
  fieldTypes.get(m[2]).add(m[1].replace(/\?$/, ''));
}
/** How many arguments a function-typed field's functions take (when every class with the field agrees), else null. */
function fieldArity(name) {
  const ts = [...(fieldTypes.get(name) ?? [])];
  const arity = (x) => {
    const m = x.match(/^(Func|Action)<(.*)>$/);
    if (!m) return x === 'Action' ? 0 : null;
    let depth = 0;
    let n = 1;
    for (const ch of m[2]) {
      if (ch === '<') depth++;
      else if (ch === '>') depth--;
      else if (ch === ',' && depth === 0) n++;
    }
    return m[1] === 'Func' ? n - 1 : n;
  };
  const ns = ts.map(arity);
  return ns.length && ns.every((n) => n !== null && n === ns[0]) ? ns[0] : null;
}

/** A field whose value is always a JsRecord. */
const recordField = (name) => fieldTypes.has(name) && [...fieldTypes.get(name)].every((x) => x.startsWith('JsRecord'));
const typeClasses = new Set([...stateSrc.matchAll(/public partial (?:class|interface) (\w+)/g)].map((m) => m[1]));

const files = [...readdirSync(SIM).filter((f) => f.endsWith('.ts')).map((f) => f.replace(/\.ts$/, '')), 'debugText'];
/** Where a module's source is (the sim's, or the ASCII renderer the tests use). */
const sourcePath = (m) => (m === 'debugText' ? 'src/debug/text.ts' : m.startsWith('test:') ? `tests/${m.slice(5)}.test.ts` : m.startsWith('harness:') ? `harness/${m.slice(8)}.harness.ts` : join(SIM, m + '.ts'));
/** Harness tools translated like test files (an `it` each), into cs/Harness: the scripted balance players. */
const harnessFiles = ['harness:colony', 'harness:solo'];
/** Test files left out: they test the browser UI (Godot replaces it), the TypeScript itself, or need a browser. */
const TEST_SKIP = new Set(['architecture', 'camera', 'card', 'interp', 'loop', 'tools']);
const testFiles = readdirSync('tests').filter((f) => f.endsWith('.test.ts')).map((f) => 'test:' + f.replace(/\.test\.ts$/, '')).filter((m) => !TEST_SKIP.has(m.slice(5)));
/** An import path, as a module name: '../src/sim/grid' and './grid' are 'grid'; the ASCII renderer is 'debugText'; anything else (the UI, JSON) is null. */
function moduleOf(path) {
  if (/(^|\/)debug\/text$/.test(path)) return 'debugText';
  const m = path.match(/^(?:\.\.\/src\/sim\/|\.\.\/sim\/|\.\/)([\w]+)$/);
  return m ? m[1] : null;
}
const pascal = (s) => s.replace(/^\w/, (c) => c.toUpperCase());
/** The C# class for a module. */
function modClass(m) {
  if (HAND[m]) return HAND[m];
  if (m === 'debugText') return 'DebugText';
  if (m.startsWith('test:')) return m.slice(5).split(/[^A-Za-z0-9]+/).map(pascal).join('') + 'Test';
  if (m.startsWith('harness:')) return m.slice(8).split(/[^A-Za-z0-9]+/).map(pascal).join('') + 'Harness';
  const p = pascal(m);
  return typeClasses.has(p) ? p + 'Mod' : p === 'State' ? 'States' : p;
}

// --- Every type alias and interface, to map annotations ---------------------------------------
const aliases = new Map(); // name -> node (type alias annotation)
const interfaces = new Set();
const interfaceNodes = new Map(); // name -> interface declaration
const sources = new Map();
for (const m of files) {
  const src = readFileSync(sourcePath(m), 'utf8');
  sources.set(m, src);
  const ast = parseAst(src, { lang: 'ts' }, m + '.ts');
  for (const n of ast.body) {
    const d = n.type === 'ExportNamedDeclaration' ? n.declaration : n;
    if (!d) continue;
    if (d.type === 'TSTypeAliasDeclaration') aliases.set(d.id.name, d.typeAnnotation);
    if (d.type === 'TSInterfaceDeclaration') {
      interfaces.add(d.id.name);
      interfaceNodes.set(d.id.name, d);
    }
  }
}

/** Per module: the types it imports, by local name, and the module each comes from. */
const typeImports = new Map();

/** Classes made for inline object types, by name: [members, module]. */
const inline = new Map();
/** The name to give an inline type met now (set by whoever asks for a type). */
let hint = 'Inline';

function inlineType(lit, mod) {
  const members = (lit.body ?? lit.members ?? []).filter((m) => m.type === 'TSPropertySignature');
  const names = members.map((m) => m.key.name ?? m.key.value).sort().join(',');
  if (names === 'x,y') return 'Point';
  if (names === 'at') return 'IHasAt';
  if (names === 'pos') return 'IHasPos';
  if (names === 'id') return 'IHasId';
  // The same shape written out again in this module: the same class.
  const shape = (ms) => JSON.stringify(ms.map((m) => [m.key.name ?? m.key.value, m.optional, m.typeAnnotation]), (k, v) => (k === 'start' || k === 'end' || k === 'range' ? undefined : v));
  for (const [n, [ms, m, ns]] of inline) {
    if (m === mod && ns === names && ms.every((x) => !x.__cs) && shape(ms) === shape(members)) return n;
    if (m === mod && ms.length && ms.every((x) => x.__cs) && ms.map((x) => String(x.key?.name ?? x.key?.value)).sort().join(',') === names) return n;
  }
  // The same fields as exactly one state class: that class (TypeScript's types are structural).
  if (classByFields.has(names)) return classByFields.get(names);
  let name = hint.replace(/[^A-Za-z0-9]/g, '');
  if (typeClasses.has(name)) name += 'Shape';
  if (inline.has(name) && inline.get(name)[2] !== names) name += inline.size;
  if (!inline.has(name)) {
    inline.set(name, [members, mod, names]);
    // Its fields' types, for the translator's type table (as the inline class will declare them).
    const saved = hint;
    const fields = new Map();
    classFields.set(name, fields);
    for (const m of members) {
      const k = String(m.key.name ?? m.key.value);
      hint = name + k.replace(/^\w/, (c) => c.toUpperCase());
      const ft = ty(m.typeAnnotation?.typeAnnotation, mod, INT_FIELDS.has(k) ? k : undefined);
      if (ft) fields.set(k, ft + (m.optional && !ft.endsWith('?') ? '?' : ''));
    }
    hint = saved;
  }
  return name;
}

/** Fields that are whole numbers in every class (gen-state.mjs INT_FIELDS). */
/** Return types of functions, by "Class.name" and by bare name (filled as modules are translated; two passes). */
const fnRet = new Map(Object.entries({ "Hash.hashRoll": "double", "Hash.idNumber": "double", "Rng.random": "double", "Rng.randomInt": "int", "Grids.inBounds": "bool", "Grids.getTile": "string?", "Grids.isWalkable": "bool", "Grids.tileIndex": "int", "Grids.edgeBetween": "int", "Grids.edgeIndex": "int", "Grids.isOpen": "bool", "Grids.isLocked": "bool", "Space.manhattan": "int", "Space.samePoint": "bool", "Space.nearestTile": "Point?", "Pathfinding.findPathToAny": "List<Point>?", "Pathfinding.findPath": "List<Point>?", "Pathfinding.canReach": "bool", "Pathfinding.isPassable": "bool", "Pathfinding.crossable": "bool", "Pathfinding.openBetween": "bool", "Pathfinding.seesThrough": "bool", "Time.dayOf": "int", "Time.tickOfDay": "int", "Time.hourOf": "double", "Time.isNight": "bool", "Time.slowTick": "bool" }));
/** "Class.fn" -> its parameters as C# writes them: [{ type, name, optional }]. */
const constTypes = new Map();
/** "Class.NAME" -> the C# type of every module-level const (constTypes holds only those C# can make const). */
const moduleConstTypes = new Map();
/** An expression without its as / satisfies. */
const Js_unwrap = (x) => { while (x && (x.type === 'TSAsExpression' || x.type === 'TSSatisfiesExpression')) x = x.expression; return x; };
const fnParams = new Map();
const parseParams = (list) => list.map((x) => {
  const decl = x.replace(/ = .*$/, '').trim().split(/\s+/);
  return { type: decl.slice(0, -1).join(' ').replace(/^params /, ''), name: decl.at(-1), optional: / = /.test(x) };
});

// The hand-ported modules' return types, read from their C# (the first overload of each name).
for (const f of ['Grid', 'Pathfinding', 'Space', 'Rng', 'Hash', 'Time', 'Lookup', 'Savefile']) {
  let cls = null;
  for (const line of readFileSync(new URL(`../Sim/${f}.cs`, import.meta.url), 'utf8').split(/\r?\n/)) {
    const c = line.match(/^public static (?:partial )?class (\w+)/);
    if (c) cls = c[1];
    const m = cls && line.match(/^    public static ([\w<>?,\[\] ]+?) (\w+)(?:<\w+>)?\(/);
    // (Not a generic one's: its T says nothing at the call.)
    if (m && !m[1].includes('class') && !/\b[A-Z]\b/.test(m[1]) && !fnRet.has(`${cls}.${m[2]}`)) fnRet.set(`${cls}.${m[2]}`, m[1]);
    const k = cls && line.match(/^    public (?:const|static readonly) ([\w<>?,\[\] ]+?) ([A-Z_0-9]+) = /);
    if (k) constTypes.set(`${cls}.${k[2]}`, k[1]);
    const pm = cls && line.match(/^    public static [\w<>?,\[\] ]+? (\w+)(?:<\w+>)?\(([^)]*)\)/);
    if (pm && !fnParams.has(`${cls}.${pm[1]}`)) {
      const parts = [];
      let depth = 0;
      let cur = '';
      for (const ch of pm[2]) {
        if ('<(['.includes(ch)) depth++;
        if ('>)]'.includes(ch)) depth--;
        if (ch === ',' && depth === 0) { parts.push(cur); cur = ''; } else cur += ch;
      }
      if (cur.trim()) parts.push(cur);
      fnParams.set(`${cls}.${pm[1]}`, parseParams(parts.map((x) => x.trim())));
    }
  }
}

/** Module constants that are C# consts (literals), by "Class.NAME" -> type: usable as default values. */

/** Re-exports, module -> name -> the module it really lives in. */
const reexports = new Map();
for (const m of files) {
  const ast = parseAst(sources.get(m), { lang: 'ts' }, m + '.ts');
  for (const n of ast.body) {
    if (n.type !== 'ExportNamedDeclaration' || n.declaration || !n.source) continue;
    for (const s of n.specifiers) {
      if (!reexports.has(m)) reexports.set(m, new Map());
      reexports.get(m).set(s.exported.name, moduleOf(n.source.value) ?? n.source.value);
    }
  }
}
/** Where an exported name really lives (following re-exports). */
function home(m, name) {
  for (let k = 0; k < 5 && reexports.get(m)?.has(name); k++) m = reexports.get(m).get(name);
  return m;
}

const INT_FIELDS = new Set(['x', 'y', 'x0', 'y0', 'x1', 'y1', 'w', 'h', 'width', 'height', 'ox', 'oy', 'cx', 'cy', 'u', 'v', 'ax', 'ay', 'bx', 'by']);

const INT_NAMES = new Set(['x', 'y', 'i', 'j', 'k', 'idx', 'index', 'cx', 'cy', 'nx', 'ny', 'x0', 'y0', 'x1', 'y1', 'dx', 'dy', 'ax', 'ay', 'bx', 'by', 'px', 'py', 'qx', 'qy', 'tx', 'ty', 'ox', 'oy', 'gx', 'gy', 'sx', 'sy', 'width', 'height', 'w', 'h', 'tick', 'e']);

/** Kind of a TS type after resolving aliases: string | number | boolean | void | null | array | record | map | set | typed | fn | object | any. */
function kindOf(t, depth = 0) {
  if (!t || depth > 20) return 'any';
  switch (t.type) {
    case 'TSStringKeyword': case 'TSTemplateLiteralType': return 'string';
    case 'TSNumberKeyword': return 'number';
    case 'TSBooleanKeyword': case 'TSTypePredicate': return 'boolean';
    case 'TSVoidKeyword': return 'void';
    case 'TSNullKeyword': case 'TSUndefinedKeyword': return 'null';
    case 'TSLiteralType': return typeof t.literal.value === 'string' ? 'string' : typeof t.literal.value === 'number' ? 'number' : typeof t.literal.value === 'boolean' ? 'boolean' : 'any';
    case 'TSArrayType': case 'TSTupleType': return 'array';
    case 'TSTypeOperator': if (t.operator === 'keyof') return 'string'; return kindOf(t.typeAnnotation, depth + 1);
    case 'TSParenthesizedType': return kindOf(t.typeAnnotation, depth + 1);
    case 'TSIndexedAccessType': {
      const f = fieldType(t);
      return f ? kindOf(f, depth + 1) : 'string';
    }
    case 'TSFunctionType': return 'fn';
    case 'TSTypeLiteral': case 'TSIntersectionType': return 'object';
    case 'TSUnionType': {
      const ks = new Set(t.types.map((u) => kindOf(u, depth + 1)).filter((k) => k !== 'null'));
      return ks.size === 1 ? [...ks][0] : ks.size === 0 ? 'null' : 'any';
    }
    case 'TSTypeReference': {
      const name = t.typeName.type === 'Identifier' ? t.typeName.name : t.typeName.right.name;
      if (name === 'Record') return 'record';
      if (name === 'Array' || name === 'ReadonlyArray') return 'array';
      if (name === 'Map' || name === 'WeakMap') return 'map';
      if (name === 'Set') return 'set';
      if (/Array$/.test(name) && ['Int32Array', 'Uint32Array', 'Float64Array', 'Uint8Array', 'Int8Array', 'Uint16Array', 'Float32Array'].includes(name)) return 'typed';
      if (['Partial', 'Required', 'Readonly', 'Exclude', 'Extract'].includes(name)) return kindOf(t.typeArguments?.params[0], depth + 1);
      if (aliases.has(name)) return kindOf(aliases.get(name), depth + 1);
      return 'object';
    }
    default: return 'any';
  }
}

/** X['field']: the field's type in interface X (null if it isn't one). */
function fieldType(t) {
  if (t.objectType?.type !== 'TSTypeReference' || t.indexType?.type !== 'TSLiteralType') return null;
  const d = interfaceNodes.get(t.objectType.typeName.name);
  const m = d?.body.body.find((x) => (x.key?.name ?? x.key?.value) === t.indexType.literal.value);
  return m?.typeAnnotation?.typeAnnotation ?? null;
}

/** The C# type for a TS annotation. `intName`: the variable's name (whole-number names get int). */
function ty(t, mod, intName) {
  if (t?.type === 'TSTypeReference' && t.typeName?.name === 'RegExp') return 'JsRegExp';
  // Road centre lines (roads.ts): fractional points.
  if (t?.type === 'TSTypeReference' && t.typeName?.name === 'RoadPoint') return 'PointD';
  if (!t) return null;
  const nullable = t.type === 'TSUnionType' && t.types.some((u) => u.type === 'TSNullKeyword' || u.type === 'TSUndefinedKeyword');
  let core = t;
  if (t.type === 'TSUnionType') {
    const rest = t.types.filter((u) => u.type !== 'TSNullKeyword' && u.type !== 'TSUndefinedKeyword');
    core = rest.length === 1 ? rest[0] : { ...t, types: rest };
  }
  const q = nullable ? '?' : '';
  if (core.type === 'TSTypeQuery' && core.exprName.type === 'Identifier') {
    const n = core.exprName.name;
    const from = typeImports.get(mod)?.get(n);
    const key = from ? `${modClass(home(from, n))}.${n}` : `${modClass(mod)}.${n}`;
    const found = moduleConstTypes.get(key) ?? constTypes.get(key);
    if (found) return found.replace(/\?$/, '') + q;
  }
  if (core.type === 'TSTypeReference' && ['Partial', 'Readonly', 'Required'].includes(core.typeName.name) && core.typeArguments?.params[0]?.type === 'TSTypeQuery') return ty(core.typeArguments.params[0], mod, intName);
  // Parameters<typeof f>[i], ReturnType<typeof f>: what the function table says.
  {
    const fnKey = (q) => {
      if (q?.type !== 'TSTypeQuery' || q.exprName.type !== 'Identifier') return null;
      const n = q.exprName.name;
      const from = typeImports.get(mod)?.get(n);
      return from ? `${modClass(home(from, n))}.${n}` : `${modClass(mod)}.${n}`;
    };
    if (core.type === 'TSIndexedAccessType' && core.objectType.type === 'TSTypeReference' && core.objectType.typeName.name === 'Parameters' && core.indexType.type === 'TSLiteralType') {
      const key = fnKey(core.objectType.typeArguments?.params[0]);
      const pt = key ? fnParams.get(key)?.[core.indexType.literal.value]?.type : null;
      if (pt) return pt.replace(/\?$/, '') + q;
    }
    if (core.type === 'TSTypeReference' && core.typeName.name === 'ReturnType') {
      const key = fnKey(core.typeArguments?.params[0]);
      const rt = key ? fnRet.get(key) : null;
      if (rt) return rt.replace(/\?$/, '') + q;
    }
  }
  // NonNullable<X>: X without its null.
  if (core.type === 'TSTypeReference' && core.typeName.name === 'NonNullable' && core.typeArguments) return ty(core.typeArguments.params[0], mod, intName)?.replace(/\?$/, '');
  if (core.type === 'TSIndexedAccessType' && fieldType(core)) {
    // X['field'] written inline in X: gen-state named its class X + Field.
    const named = core.objectType.typeName.name + pascal(String(core.indexType.literal.value));
    let ft = fieldType(core);
    if (ft.type === 'TSUnionType') ft = ft.types.find((u) => u.type === 'TSTypeLiteral') ?? ft;
    if (ft.type === 'TSTypeLiteral' && typeClasses.has(named)) return named + (q || (fieldType(core).type === 'TSUnionType' && fieldType(core).types.some((u) => u.type === 'TSNullKeyword') ? '?' : ''));
    return ty(fieldType(core), mod, intName)?.replace(/\?$/, '') + q;
  }
  const k = kindOf(core);
  switch (k) {
    case 'string': return 'string' + q;
    case 'number': return (intName && INT_NAMES.has(intName) ? 'int' : 'double') + q;
    case 'boolean': return 'bool' + q;
    case 'void': return 'void';
    case 'array': {
      let c = deref(core);
      if (c.type === 'TSTypeOperator') c = c.typeAnnotation;
      if (c.type === 'TSTupleType') {
        const ks = new Set(c.elementTypes.map((e) => kindOf(e)));
        if (ks.size === 1 && ks.has('string')) return 'List<string>' + q;
        if (ks.size === 1 && ks.has('number')) return 'List<double>' + q;
        // [Point, Point] and the like: a list of that type.
        const els = c.elementTypes.map((e) => (ty(e, mod) ?? 'object').replace(/\?$/, ''));
        if (els.every((x) => x === els[0] && !x.includes('TODO'))) return `List<${els[0]}>` + q;
        // [string, number] and the like: a C# tuple (a [key, value] literal is written as one).
        return (els.length === 2 && els[0] === 'string' && els[1] !== 'object' ? `(${els.join(', ')})` : 'List<object>') + q;
      }
      const el = c.type === 'TSArrayType' ? c.elementType : c.typeArguments?.params[0];
      return `List<${(ty(el, mod) ?? 'object').replace(/\?$/, '')}>` + q;
    }
    case 'record': {
      const r = deref(core);
      return `JsRecord<${ty(r.typeArguments?.params[1], mod) ?? 'object'}>` + q;
    }
    case 'map': return `JsMap<${(ty(core.typeArguments?.params[0], mod) ?? 'object').replace(/\?$/, '')}, ${ty(core.typeArguments?.params[1], mod) ?? 'object'}>` + q;
    case 'set': return `JsSet<${(ty(core.typeArguments?.params[0], mod) ?? 'object').replace(/\?$/, '')}>` + q;
    case 'typed': {
      const name = core.typeName.name;
      return name + q; // TypedArrays.cs: JS's typed arrays, by the same names
    }
    case 'fn': {
      core = deref(core);
      if (core.type === 'TSParenthesizedType') core = core.typeAnnotation;
      const params = (core.params ?? []).map((p) => ty(p.typeAnnotation?.typeAnnotation, mod) ?? 'object');
      const ret = ty(core.returnType?.typeAnnotation, mod) ?? 'object';
      return (ret === 'void' ? (params.length ? `Action<${params.join(', ')}>` : 'Action') : `Func<${[...params, ret].join(', ')}>`) + q;
    }
    case 'object': {
      let c = core;
      // A union of bodies (Animal | Survivor): what they share.
      if (c.type === 'TSUnionType' && c.types.every((u) => u.type === 'TSTypeReference' && bodyClasses.has(u.typeName.name))) return 'IBody' + q;
      if (c.type === 'TSTypeReference') {
        let name = c.typeName.type === 'Identifier' ? c.typeName.name : c.typeName.right.name;
        if (['Partial', 'Required', 'Readonly'].includes(name)) return ty(c.typeArguments.params[0], mod) + q;
        if (aliases.has(name) && kindOf(aliases.get(name)) === 'record') return ty(aliases.get(name), mod) + q;
        name = renames[mod + '.ts']?.[name] ?? renames[(typeImports.get(mod)?.get(name) ?? '') + '.ts']?.[name] ?? name;
        if (name === 'Point' || name === 'Grid' || typeClasses.has(name)) return name + q;
        if ((name === 'Pick' || name === 'Omit' || name === 'NonNullable') && c.typeArguments) return ty(c.typeArguments.params[0], mod) + q;
        if (name === 'Extract' && c.typeArguments) return ty(c.typeArguments.params[0], mod) + q;
        if (name.length === 1) return name + q; // a type parameter (T)
        return `/*TODO type ${name}*/object` + q;
      }
      if (c.type === 'TSTypeLiteral') return inlineType(c, mod) + q;
      return '/*TODO inline type*/object' + q;
    }
    default:
      // A union of bodies (Animal | Survivor): what they share.
      if (core.type === 'TSUnionType' && core.types.every((u) => u.type === 'TSTypeReference' && bodyClasses.has(u.typeName.name))) return 'IBody' + q;
      return 'object' + q;
  }
}

function deref(t) {
  for (let k = 0; k < 20 && t.type === 'TSTypeReference'; k++) {
    const name = t.typeName.type === 'Identifier' ? t.typeName.name : t.typeName.right.name;
    if (['Record', 'Array', 'ReadonlyArray'].includes(name)) return t;
    if (['Partial', 'Required', 'Readonly', 'Exclude', 'Extract'].includes(name)) { t = t.typeArguments.params[0]; continue; }
    if (!aliases.has(name)) return t;
    t = aliases.get(name);
  }
  return t;
}

const KEYWORDS = new Set(['base', 'bool', 'byte', 'char', 'checked', 'decimal', 'double', 'event', 'explicit', 'extern', 'fixed', 'float', 'foreach', 'goto', 'implicit', 'int', 'internal', 'is', 'lock', 'long', 'object', 'operator', 'out', 'override', 'params', 'readonly', 'ref', 'sbyte', 'sealed', 'short', 'sizeof', 'stackalloc', 'string', 'struct', 'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'using', 'virtual', 'volatile', 'as', 'in', 'out', 'namespace', 'abstract', 'const', 'delegate', 'enum', 'interface', 'private', 'protected', 'public', 'static', 'this', 'base', 'operator', 'default', 'event', 'decimal', 'lock', 'checked', 'fixed', 'object', 'string']);
const id = (n) => (KEYWORDS.has(n) ? '@' + n : n);

// --- Translating a module ----------------------------------------------------------------------

function translate(mod) {
  const src = sources.get(mod);
  const ast = parseAst(src, { lang: 'ts' }, mod + '.ts');
  const imports = new Map(); // local name -> C# qualified name
  const recordConsts = new Set(); // module consts that became JsRecord: dot access -> indexer
  const out = [];
  const statics = [];
  const ctorBody = [];
  let tmp = 0;
  let curFn = '';
  /** Functions' parameter types (C#), by name, as declared in this module: a spread object passed to one becomes that type. */
  const paramTypes = new Map();
  /** The class an object literal with a spread being written is going to (a parameter's type), if known. */
  let spreadAs = null;
  const typeOfParam = (x) => x.replace(/ = .*$/, '').trim().split(/\s+/).slice(0, -1).join(' ');
  /** The return type of the function whose return statement is being written. */
  let curRet = '';

  /** Scenario files a test imports (JSON), as static fields. */
  const jsonImports = [];
  const jsonLocals = [];
  /** Names imported from outside what's translated (the browser UI): tests using them are left out. */
  const unavailable = new Set();
  for (const n of ast.body) {
    if (n.type !== 'ImportDeclaration') continue;
    if (n.source.value === 'vitest') continue;
    // Node's own modules (fs, inspector): the harness shims them (cs/Harness/Node.cs).
    if (mod.startsWith('harness:') && n.source.value.startsWith('node:')) continue;
    const from = moduleOf(n.source.value);
    const json = n.source.value.match(/^\.\.\/src\/scenarios\/([\w-]+\.json)$/);
    if (!from && json && n.specifiers.length === 1 && n.specifiers[0].type === 'ImportDefaultSpecifier') {
      const local = n.specifiers[0].local.name;
      jsonImports.push(`    public static readonly Scenario ${id(local)} = Js.JsonParse<Scenario>(File.ReadAllText(Js.RepoFile("src/scenarios/${json[1]}")));`);
      jsonLocals.push(local);
      continue;
    }
    if (!from) {
      for (const s of n.specifiers) unavailable.add(s.local.name);
      continue;
    }
    if (!typeImports.has(mod)) typeImports.set(mod, new Map());
    for (const s of n.specifiers) if (s.type === 'ImportSpecifier') typeImports.get(mod).set(s.local.name, from);
    for (const s of n.specifiers) {
      if (s.type !== 'ImportSpecifier' || s.importKind === 'type' || n.importKind === 'type') continue;
      const name = s.imported.name;
      imports.set(s.local.name, `${modClass(home(from, name))}.${name}`);
    }
  }
  const moduleNames = new Set();
  /** A harness file's own interfaces (the solo bot's Bot): classes declared in its output, known to the type table. */
  const ownClasses = [];
  if (mod.startsWith('harness:')) {
    for (const n of ast.body) {
      if (n.type !== 'TSInterfaceDeclaration') continue;
      const name = n.id.name;
      typeClasses.add(name);
      const fields = new Map();
      const lines = [`public partial class ${name}`, '{'];
      for (const m of n.body.body) {
        if (m.type !== 'TSPropertySignature') continue;
        const f = String(m.key.name ?? m.key.value);
        hint = name + pascal(f);
        let c = ty(m.typeAnnotation?.typeAnnotation, mod, f) ?? 'object';
        if (m.optional && !c.endsWith('?')) c += '?';
        fields.set(f, c);
        lines.push(`    public ${c} ${id(f)} { get; set; }${c.startsWith('List<') ? ' = new();' : c === 'string' ? ' = "";' : ''}`);
      }
      lines.push('}', '');
      classFields.set(name, fields);
      ownClasses.push(...lines);
    }
  }

  /** An operand: wrapped in parentheses when it's an operation itself, so precedence can't change. */
  function operand(e, bool = false) {
    const s = ex(e, bool);
    const compound = ['BinaryExpression', 'LogicalExpression', 'ConditionalExpression', 'AssignmentExpression', 'SequenceExpression', 'ArrowFunctionExpression', 'TSAsExpression'].includes(e.type);
    return compound && !wrapped(s) ? `(${s})` : s;
  }

  /** Is the whole of s inside one pair of parentheses? ("(a) ? b : (c)" starts and ends with them, but isn't.) Strings skipped. */
  function wrapped(s) {
    if (!s.startsWith('(') || !s.endsWith(')')) return false;
    let depth = 0;
    let quote = null;
    for (let i = 0; i < s.length; i++) {
      const ch = s[i];
      if (quote) {
        if (ch === '\\') i++;
        else if (ch === quote) quote = null;
        continue;
      }
      if (ch === '"') quote = ch;
      else if (ch === '(') depth++;
      else if (ch === ')') {
        depth--;
        if (depth === 0 && i < s.length - 1) return false;
      }
    }
    return depth === 0;
  }

  // Expressions. `bool`: wanted as a condition.
  function ex(e, bool = false) {
    const r = e?.__toDouble ? `(double)(${ex0(e, bool)})` : e?.__nn ? `Js.NN(${ex0(e, bool)})` : ex0(e, bool);
    return bool && !isBoolish(e) ? `Js.T(${r})` : r;
  }

  function isBoolish(e) {
    if (!e) return false;
    if (e.type === 'BinaryExpression') return ['==', '!=', '===', '!==', '<', '>', '<=', '>=', 'in', 'instanceof'].includes(e.operator);
    if (e.type === 'LogicalExpression') return e.operator === '||' ? isBoolish(e.left) && isBoolish(e.right) : e.operator === '&&' ? isBoolish(e.right) : false;
    if (e.type === 'UnaryExpression') return e.operator === '!';
    if (e.type === 'Literal') return typeof e.value === 'boolean';
    if (e.type === 'ParenthesizedExpression') return isBoolish(e.expression);
    if (e.type === 'CallExpression' && exprType(e) === 'bool') return true;
    if ((e.type === 'Identifier' || (e.type === 'MemberExpression' && !e.computed)) && exprType(e) === 'bool') return true;
    if (e.type === 'CallExpression' && e.callee.type === 'MemberExpression' && !e.callee.computed) return ['some', 'every', 'includes', 'has', 'startsWith', 'endsWith', 'isInteger', 'isFinite', 'isNaN', 'delete'].includes(e.callee.property.name);
    return false;
  }

  /** A C# list type for an array literal of numbers, strings, or arrays of those (null if it isn't one). */
  function literalType(a) {
    while (a && (a.type === 'TSAsExpression' || a.type === 'TSSatisfiesExpression')) a = a.expression;
    if (a?.type !== 'ArrayExpression' || a.elements.length === 0) return null;
    const el = a.elements.map((x) => {
      if (!x) return null;
      if (x.type === 'ArrayExpression') return literalType(x);
      if (x.type === 'Literal' && typeof x.value === 'string') return 'string';
      if (x.type === 'TemplateLiteral') return 'string';
      if (x.type === 'Literal' && typeof x.value === 'number') return 'double';
      if (x.type === 'UnaryExpression' && x.argument.type === 'Literal' && typeof x.argument.value === 'number') return 'double';
      if (x.type === 'BinaryExpression' && ['<', '>', '<=', '>=', '===', '!==', '==', '!='].includes(x.operator)) return 'bool';
      const pointish = (y) => y?.type === 'ObjectExpression' && y.properties.length === 2 && y.properties.every((q) => q.type === 'Property') && y.properties.map((q) => q.key.name).sort().join(',') === 'x,y';
      if (pointish(x) || (x.type === 'ConditionalExpression' && pointish(x.consequent) && pointish(x.alternate))) return 'Point';
      if (x.type === 'Identifier' && exprType(x) && !['int', 'double'].includes(strip(exprType(x)))) return strip(exprType(x));
      if ((x.type === 'BinaryExpression' || x.type === 'CallExpression' || x.type === 'MemberExpression') && exprType(x) && !['int', 'double'].includes(strip(exprType(x)))) return strip(exprType(x));
      if (x.type === 'Identifier' || x.type === 'BinaryExpression') return 'num?';
      return null;
    });
    if (el.some((k) => k === null)) return null;
    if (a.elements.some((x) => x?.type === 'Identifier' && /^(int|double)\?$/.test(exprType(x) ?? ''))) return 'List<double?>';
    const kinds = new Set(el.map((k) => (k === 'num?' ? 'double' : k)));
    if (kinds.size === 2 && kinds.has('int') && kinds.has('double')) return 'List<double>';
    if (kinds.size !== 1) return null;
    return `List<${[...kinds][0]}>`;
  }

  /** A class for an object literal's shape (fields typed from their values, as far as they show). */
  function objectLiteralClass(o, name) {
    const keys = o.properties.map((p) => String(p.key.name ?? p.key.value)).sort().join(',');
    if (classByFields.has(keys)) return classByFields.get(keys);
    if (o.properties.some((q) => (q.key?.name ?? q.key?.value) === 'type') && classFields.get('Command') && o.properties.every((q) => q.type === 'Property' && classFields.get('Command').has(String(q.key.name ?? q.key.value)))) return 'Command';
    // An inline type written out in this module with the same fields (its types are known).
    for (const [n, [ms, m]] of inline) {
      if (m === mod && ms.every((x) => !x.__cs) && ms.map((x) => String(x.key?.name ?? x.key?.value)).sort().join(',') === keys) return n;
    }
    const members = o.properties.map((p) => {
      const k = String(p.key.name ?? p.key.value);
      const v = p.value;
      let tp = 'object';
      const known = exprType(p.shorthand ? p.key : v);
      if (INT_FIELDS.has(k) && (!known || ['int', 'double'].includes(strip(known)))) tp = 'int';
      else if (INT_FIELDS.has(k)) tp = known;
      else if (v.type === 'Literal' && typeof v.value === 'number') tp = 'double';
      else if (v.type === 'Literal' && typeof v.value === 'string') tp = 'string';
      else if (v.type === 'Literal' && typeof v.value === 'boolean') tp = 'bool';
      else if (v.type === 'TemplateLiteral') tp = 'string';
      else if (v.type === 'BinaryExpression' && ['<', '>', '<=', '>=', '==', '===', '!=', '!=='].includes(v.operator)) tp = 'bool';
      else if (v.type === 'BinaryExpression' || (v.type === 'CallExpression' && v.callee.type === 'MemberExpression' && v.callee.object.name === 'Math')) tp = 'double';
      else if (v.type === 'ArrayExpression') tp = literalType(v) ?? 'List<object>';
      if (v.type === 'ArrowFunctionExpression' && v.params.every((q) => q.type === 'Identifier' && q.typeAnnotation)) {
        const ps = v.params.map((q) => (ty(q.typeAnnotation.typeAnnotation, mod) ?? 'object').replace(/^int$/, 'double'));
        const rt = v.returnType ? ty(v.returnType.typeAnnotation, mod) : v.body.type !== 'BlockStatement' ? (isBoolish(v.body) ? 'bool' : strip(exprType(v.body))) : null;
        if (rt === 'void') tp = ps.length ? `Action<${ps.join(', ')}>` : 'Action';
        else if (rt) tp = `Func<${[...ps, rt === 'int' ? 'double' : rt].join(', ')}>`;
      }
      if (tp === 'object' || tp === 'List<object>') {
        const et = exprType(p.shorthand ? p.key : v);
        if (et && !et.includes('TODO') && et !== 'var') tp = et;
      }
      return { key: { name: k }, type: 'TSPropertySignature', typeAnnotation: null, __cs: tp };
    });
    let n = name;
    if (typeClasses.has(n) || inline.has(n)) n += 'Shape' + inline.size;
    inline.set(n, [members, mod, members.map((m) => m.key.name).join(',')]);
    classFields.set(n, new Map(members.map((m) => [m.key.name, m.__cs])));
    return n;
  }

  // Block scopes: JavaScript lets an inner block declare a name an enclosing block also declares (even later);
  // C# doesn't, so such an inner declaration gets a new name (to_1), used for it throughout its block.
  const scopes = [];
  let renameCount = 0;
  function namesIn(stmts) {
    const out = [];
    const pat = (x) => {
      if (!x) return;
      if (x.type === 'Identifier') out.push(x.name);
      else if (x.type === 'ObjectPattern') x.properties.forEach((q) => pat(q.value ?? q.key));
      else if (x.type === 'ArrayPattern') x.elements.forEach(pat);
      else if (x.type === 'AssignmentPattern') pat(x.left);
      else if (x.type === 'RestElement') pat(x.argument);
    };
    for (const s of stmts) {
      if (s.type === 'VariableDeclaration') s.declarations.forEach((d) => pat(d.id));
      if (s.type === 'FunctionDeclaration') out.push(s.id.name);
    }
    return out;
  }
  function pushScope(names) {
    scopes.push({ declared: new Set(names), renames: new Map(), types: new Map() });
  }
  function popScope() {
    scopes.pop();
  }
  /** Local types: what a name declared in this module holds, as far as it's known (C#). */
  const moduleTypes = new Map();
  /** Every type a name was given anywhere in this module (used when it's out of scope: a function's return guessed after its body). */
  const seenTypes = new Map();
  /** Names' TypeScript types (parameters), for what C# types don't say: a key typed 'body' | 'feet'. */
  const tsTypes = new Map();
  function setTsType(n, node) {
    tsTypes.set(n, node);
  }
  /** The string literals a TS type allows ('a' | 'b', through aliases), or null. */
  function literalKeys(node, depth = 0) {
    if (!node || depth > 10) return null;
    if (node.type === 'TSLiteralType' && typeof node.literal.value === 'string') return [node.literal.value];
    if (node.type === 'TSUnionType') {
      const all = node.types.map((u) => literalKeys(u, depth + 1));
      return all.every(Boolean) ? all.flat() : null;
    }
    if (node.type === 'TSTypeReference' && node.typeName.type === 'Identifier' && aliases.has(node.typeName.name)) return literalKeys(aliases.get(node.typeName.name), depth + 1);
    return null;
  }
  /** The const declarations seen, by name: their values tell what strings a key can be. */
  const initOf = new Map();
  /** Module consts whose type comes from their value (new Map<...>() and the like). */
  const moduleInits = new Map();
  for (const l of jsonLocals) moduleTypes.set(l, 'Scenario');
  for (const n of ast.body) {
    const d = n.type === 'ExportNamedDeclaration' ? n.declaration : n;
    if (d?.type !== 'VariableDeclaration') continue;
    for (const v of d.declarations) {
      if (v.id.type !== 'Identifier') continue;
      if (v.id.typeAnnotation) {
        setTsType(v.id.name, v.id.typeAnnotation.typeAnnotation);
        hint = pascal(v.id.name);
        moduleTypes.set(v.id.name, ty(v.id.typeAnnotation.typeAnnotation, mod));
      }
      if (d.kind === 'const' && v.init) initOf.set(v.id.name, v.init);
      if (d.kind === 'const' && v.init && !v.id.typeAnnotation && v.init.type === 'NewExpression') moduleInits.set(v.id.name, v.init);
    }
  }
  /** The strings an expression can be (null and undefined aside), when that can be told. */
  function keysOf(x, depth = 0) {
    if (!x || depth > 8) return null;
    if (x.type === 'Literal' && typeof x.value === 'string') return [x.value];
    if (x.type === 'Literal' && x.value === null) return [];
    if (x.type === 'ConditionalExpression') {
      const a = keysOf(x.consequent, depth + 1);
      const b = keysOf(x.alternate, depth + 1);
      return a && b ? [...a, ...b] : null;
    }
    if (x.type === 'Identifier') return literalKeys(tsTypes.get(x.name)) ?? keysOf(initOf.get(x.name), depth + 1);
    if (x.type === 'MemberExpression' && x.computed && x.object.type === 'Identifier') {
      const rec = tsTypes.get(x.object.name);
      if (rec?.type === 'TSTypeReference' && rec.typeName.name === 'Record') return literalKeys(rec.typeArguments.params[1]);
    }
    return null;
  }
  /** obj[key] where key names fields of obj's class (all of one type): that type, else null. */
  function fieldByKey(e) {
    const cls = strip(exprType(e.object));
    const fields = cls ? classFields.get(cls) : null;
    if (!fields) return null;
    const keys = keysOf(e.property);
    if (!keys) return null;
    const ts = keys.map((k) => fields.get(k));
    return ts.length && ts.every((x) => x && x === ts[0]) ? ts[0] : null;
  }
  /** While a body's dry run is on: its locals' types (they're out of scope by the time the return type is guessed). */
  let collecting = null;
  function setType(n, ty_, isParam = false) {
    if (!ty_ || ty_ === 'var' || (ty_.includes('TODO') && !/^\/\*TODO[^*]*\*\/object\??$/.test(ty_))) return;
    ty_ = ty_.replace(/^\/\*TODO[^*]*\*\//, '');
    (scopes.length ? scopes[scopes.length - 1].types : moduleTypes).set(n, ty_);
    if (collecting) collecting.set(n, ty_);
    if (isParam) return; // (a parameter's type is its function's alone)
    if (!seenTypes.has(n)) seenTypes.set(n, new Set());
    seenTypes.get(n).add(ty_);
  }
  function typeOf(n) {
    if (collecting?.has(n)) return collecting.get(n);
    for (let i = scopes.length - 1; i >= 0; i--) if (scopes[i].types.has(n)) return scopes[i].types.get(n);
    if (moduleTypes.has(n)) return moduleTypes.get(n);
    const seen = seenTypes.get(n);
    return seen?.size === 1 ? [...seen][0] : null;
  }
  const strip = (x) => x?.replace(/\?$/, '') ?? null;
  /** The C# type of an expression, when it can be told (null when not). */
  function exprType(e) {
    if (!e) return null;
    switch (e.type) {
      case 'Identifier': {
        if (e.name === 'undefined') return null;
        const known = typeOf(e.name) ?? constTypes.get(imports.get(e.name) ?? `${modClass(mod)}.${e.name}`) ?? (imports.has(e.name) ? moduleConstTypes.get(imports.get(e.name)) : !scopes.some((s) => s.declared.has(e.name)) ? moduleConstTypes.get(`${modClass(mod)}.${e.name}`) : null);
        if (known) return known;
        // A module const with no type written: its value's (worked out once, when first needed).
        const init = !scopes.some((s) => s.declared.has(e.name)) ? moduleInits.get(e.name) : null;
        if (init && !moduleTypes.has(e.name)) {
          moduleTypes.set(e.name, null);
          moduleTypes.set(e.name, exprType(init));
        }
        return moduleTypes.get(e.name) ?? null;
      }
      case 'Literal': return typeof e.value === 'string' ? 'string' : typeof e.value === 'number' ? 'double' : typeof e.value === 'boolean' ? 'bool' : null;
      case 'TemplateLiteral': return 'string';
      case 'BinaryExpression': {
        const op = e.operator;
        if (['<', '>', '<=', '>=', '==', '===', '!=', '!==', 'in', 'instanceof'].includes(op)) return 'bool';
        const l = strip(exprType(e.left));
        const r = strip(exprType(e.right));
        if (op === '+' && (l === 'string' || r === 'string')) return 'string';
        if (['&', '|', '^', '<<', '>>'].includes(op)) return 'int';
        if (op === '>>>') return 'double';
        const nul = /\?$/.test(exprType(e.left) ?? '') || /\?$/.test(exprType(e.right) ?? '') ? '?' : '';
        // * is a double even of two ints: JS multiplies in doubles, and squared distances overflow 32 bits.
        if (['-', '*', '/', '%', '**'].includes(op) || (op === '+' && l && r)) return (l === 'int' && r === 'int' && op !== '/' && op !== '**' && op !== '*' ? 'int' : 'double') + nul;
        return null;
      }
      case 'UnaryExpression':
        if (e.operator === '!') return 'bool';
        if (e.operator === '-' || e.operator === '+') return e.argument.type === 'Literal' ? 'double' : exprType(e.argument) === 'int' ? 'int' : 'double';
        if (e.operator === 'typeof') return 'string';
        return null;
      case 'TSNonNullExpression': return strip(exprType(e.expression));
      case 'TSAsExpression': if (e.typeAnnotation.type === 'TSTypeLiteral' && e.expression.type === 'CallExpression' && e.expression.callee.object?.name === 'JSON') return 'JsRecord<object?>';
        return e.typeAnnotation.type === 'TSTypeReference' && e.typeAnnotation.typeName.name === 'const' ? exprType(e.expression) : ty(e.typeAnnotation, mod);
      case 'ParenthesizedExpression': return exprType(e.expression);
      case 'ConditionalExpression': {
        const a = exprType(e.consequent);
        const b = exprType(e.alternate);
        const nullA = e.consequent.type === 'Literal' && e.consequent.value === null;
        const nullB = e.alternate.type === 'Literal' && e.alternate.value === null;
        if (nullA && b) return strip(b) + '?';
        if (nullB && a) return strip(a) + '?';
        return a && b && strip(a) === strip(b) ? (a.endsWith('?') || b.endsWith('?') ? strip(a) + '?' : a) : null;
      }
      case 'LogicalExpression': {
        if (e.operator !== '??') return null;
        const a = exprType(e.left);
        const nullB = e.right.type === 'Literal' && e.right.value === null;
        if (nullB) return a ? strip(a) + '?' : null;
        const b = exprType(e.right);
        return a && b && strip(a) === strip(b) ? b : null;
      }
      case 'NewExpression': {
        const c = e.callee.name;
        const targs = e.typeArguments?.params ?? [];
        if ((c === 'Map' || c === 'WeakMap') && targs.length === 2) return `JsMap<${strip(ty(targs[0], mod))}, ${ty(targs[1], mod)}>`;
        if (c === 'Set' && targs.length === 1) return `JsSet<${strip(ty(targs[0], mod))}>`;
        if (c === 'Set' && e.arguments.length === 1) {
          const it = strip(exprType(e.arguments[0]))?.match(/^List<(.+)>$/)?.[1];
          if (it) return `JsSet<${it}>`;
        }
        if (/Array$/.test(c) && c !== 'Array') return c;
        return null;
      }
      case 'MemberExpression': {
        const o = strip(exprType(e.object));
        if (!o) return null;
        if (!e.computed) {
          const f = e.property.name;
          if (f === 'length') return 'int';
          if (/^JsRecord<.+>$/.test(o)) return o.slice(9, -1);
          return classFields.get(o)?.get(f) ?? (o === 'Point' && (f === 'x' || f === 'y') ? 'int' : null);
        }
        const item = o.match(/^(?:List|IReadOnlyList)<(.+)>$/)?.[1] ?? o.match(/^JsRecord<(.+)>$/)?.[1] ?? (/Array$/.test(o) ? 'double' : null);
        return item ?? fieldByKey(e);
      }
      case 'CallExpression': {
        const c = e.callee;
        if (c.type === 'Identifier' && !imports.has(c.name)) {
          const builtin = { Number: 'double', String: 'string', Boolean: 'bool', parseInt: 'double', parseFloat: 'double' }[c.name];
          if (builtin) return builtin;
        }
        if (c.type === 'Identifier') {
          const q = imports.get(c.name);
          return fnRet.get(q ?? `${modClass(mod)}.${c.name}`) ?? fnRet.get(c.name) ?? null;
        }
        if (c.type !== 'MemberExpression' || c.computed) return null;
        if (c.object.type === 'Identifier' && c.object.name === 'Math') return 'double';
        if (c.object.type === 'Identifier' && c.object.name === 'Object' && c.property.name === 'keys') return 'List<string>';
        if (c.object.type === 'Identifier' && c.object.name === 'Array' && c.property.name === 'from' && e.arguments[1]?.type === 'ArrowFunctionExpression' && e.arguments[1].body.type !== 'BlockStatement') {
          const bt = e.arguments[1].body.type === 'Literal' && typeof e.arguments[1].body.value === 'number' ? 'double' : strip(exprType(e.arguments[1].body));
          if (bt) return `List<${bt}>`;
        }
        if (c.object.type === 'Identifier' && imports.has(c.object.name) === false && /^[A-Z]/.test(c.object.name)) return null;
        const o = strip(exprType(c.object));
        if (!o) return null;
        const m = c.property.name;
        const item = o.match(/^List<(.+)>$/)?.[1] ?? o.match(/^(\w+)\[\]$/)?.[1];
        // JsMap<K, V>: split at the top-level comma (K may be generic itself).
        let map = null;
        if (o.startsWith('JsMap<') && o.endsWith('>')) {
          const inner = o.slice(6, -1);
          let depth = 0;
          for (let i = 0; i < inner.length; i++) {
            if (inner[i] === '<' || inner[i] === '(') depth++;
            else if (inner[i] === '>' || inner[i] === ')') depth--;
            else if (inner[i] === ',' && depth === 0) { map = [o, inner.slice(0, i).trim(), inner.slice(i + 1).trim()]; break; }
          }
        }
        if (['includes', 'some', 'every', 'has', 'startsWith', 'endsWith'].includes(m)) return 'bool';
        if (['indexOf', 'findIndex', 'lastIndexOf'].includes(m)) return 'int';
        if (m === 'join' || (o === 'string' && ['slice', 'trim', 'toUpperCase', 'toLowerCase', 'replace', 'padStart', 'padEnd', 'repeat', 'substring'].includes(m))) return 'string';
        if (item && ['find', 'findLast', 'at', 'pop', 'shift'].includes(m)) return strip(item) + '?';
        if (item && ['filter', 'slice', 'concat', 'reverse', 'sort', 'splice'].includes(m)) return o;
        if (map && m === 'get') return strip(map[2]) + '?';
        // list.map(x => expr): a list of what expr is (when that's known without x).
        if (item && m === 'map' && c && e.arguments[0]?.type === 'ArrowFunctionExpression' && e.arguments[0].body.type !== 'BlockStatement') {
          const bt = strip(exprType(e.arguments[0].body));
          if (bt) return `List<${bt === 'int' ? 'double' : bt}>`;
        }
        if (map && m === 'keys') return `List<${map[1]}>`;
        if (map && m === 'values') return `List<${map[2]}>`;
        return null;
      }
      default: return null;
    }
  }

  /** The C# name for a declaration of `n` in the innermost scope. */
  function declName(n) {
    const top = scopes[scopes.length - 1];
    if (!top) return id(n);
    for (let i = 0; i < scopes.length - 1; i++) {
      const s = scopes[i];
      if (s.declared.has(n) || [...s.renames.values()].includes(n)) {
        const nn = `${n}_${++renameCount}`;
        top.renames.set(n, nn);
        return id(nn);
      }
    }
    return id(n);
  }
  /** What a name refers to here (a renamed declaration, or itself). */
  function lookupName(n) {
    for (let i = scopes.length - 1; i >= 0; i--) if (scopes[i].renames.has(n)) return scopes[i].renames.get(n);
    return n;
  }

  /** var (a, b) = x; or, for one name, var a = x[0]; */
  function destructure(elements, value, init) {
    if (init && (init.type === 'ArrayExpression' || init.type === 'ConditionalExpression')) {
      const tuple = (a) => `(${a.elements.map((x) => ex(x)).join(', ')})`;
      const choice = (n) => n.type === 'ArrayExpression' || (n.type === 'ConditionalExpression' && choice(n.consequent) && choice(n.alternate));
      if (init.type === 'ConditionalExpression' && choice(init) && !(init.consequent.type === 'ArrayExpression' && init.alternate.type === 'ArrayExpression')) {
        const pick = (n, i) => (n.type === 'ArrayExpression' ? operand(n.elements[i]) : `(${ex(n.test, true)} ? ${pick(n.consequent, i)} : ${pick(n.alternate, i)})`);
        return elements.map((x, i) => (x ? `var ${declName(x.name)} = ${pick(init, i)};` : '')).filter(Boolean).join(' ');
      }
      if (init.type === 'ConditionalExpression' && init.consequent.elements.length === init.alternate.elements.length && elements.length <= init.consequent.elements.length) {
        const c = `__c${tmp++}`;
        const parts = [`var ${c} = ${ex(init.test, true)};`];
        elements.forEach((x, i) => {
          if (x) parts.push(`var ${declName(x.name)} = ${c} ? ${operand(init.consequent.elements[i])} : ${operand(init.alternate.elements[i])};`);
        });
        return parts.join(' ');
      }
      value = init.type === 'ArrayExpression' ? tuple(init) : `${operand(init.test, true)} ? ${tuple(init.consequent)} : ${tuple(init.alternate)}`;
    }
    const names = elements.map((x) => (x ? declName(x.name) : '_'));
    if (names.length === 1) return `var ${names[0]} = (${value})[0];`;
    return `var (${names.join(', ')}) = ${value};`;
  }

  function num(v, raw) {
    if (Number.isInteger(v) && Math.abs(v) <= 2147483647 && !/[.eE]/.test(raw ?? '')) return raw?.startsWith('0x') ? raw : String(v);
    if (Number.isInteger(v)) return `${v}.0`;
    return raw && !raw.startsWith('.') ? raw.replace(/_/g, '') : String(v);
  }

  function str(s) {
    return '"' + s.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\r/g, '\\r').replace(/\t/g, '\\t') + '"';
  }

  function ex0(e, bool) {
    switch (e.type) {
      case 'Literal':
        if (e.value === null) return 'null';
        if (typeof e.value === 'string') return str(e.value);
        if (typeof e.value === 'number' && e.__double && Number.isInteger(e.value)) return `${e.value}.0`;
        if (typeof e.value === 'number') return num(e.value, e.raw);
        if (typeof e.value === 'boolean') return String(e.value);
        if (e.regex) return `new JsRegExp(${str(e.regex.pattern)}, ${str(e.regex.flags)})`;
        return `/*TODO literal*/null`;
      case 'Identifier': {
        const n = e.name;
        if (n === 'undefined') return 'null';
        if (n === 'Infinity') return 'double.PositiveInfinity';
        if (n === 'NaN') return 'double.NaN';
        const local = lookupName(n);
        if (local !== n) return id(local);
        if (imports.has(n)) return imports.get(n);
        return id(n);
      }
      case 'ThisExpression': return 'this';
      case 'TemplateLiteral': {
        let s = '$"';
        e.quasis.forEach((q, i) => {
          s += q.value.cooked.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\{/g, '{{').replace(/\}/g, '}}');
          if (i < e.expressions.length) {
            // `${cond ? n : '-'}`: each branch to a string, as C#'s ?: needs one type.
            const x = e.expressions[i];
            const asStr = (b) => ({ type: 'CallExpression', callee: { type: 'Identifier', name: 'String' }, arguments: [b] });
            const branches = x.type === 'ConditionalExpression' ? { ...x, consequent: asStr(x.consequent), alternate: asStr(x.alternate) }
              : x.type === 'LogicalExpression' && x.operator === '||' && strip(exprType(x.right)) !== 'string' && (strip(exprType(x.left)) === 'string' || x.right.type === 'Literal') ? { ...x, right: asStr(x.right) }
              : x;
            s += `{Js.Str(${ex(branches)})}`;
          }
        });
        return s + '"';
      }
      case 'SequenceExpression': {
        const xs = e.expressions;
        return `Js.Run(() => { ${xs.slice(0, -1).map((x) => ex(x) + ';').join(' ')} return ${ex(xs.at(-1))}; })`;
      }
      case 'ParenthesizedExpression': return `(${ex(e.expression, bool)})`;
      case 'TSAsExpression': case 'TSSatisfiesExpression': case 'TSTypeAssertion': {
        const t = e.typeAnnotation;
        if (t.type === 'TSTypeReference' && t.typeName.name === 'const' && curRet.startsWith('(') && e.expression.type === 'ArrayExpression') return `(${e.expression.elements.map((x) => ex(x)).join(', ')})`;
        if (t.type === 'TSTypeReference' && t.typeName.name === 'const') return ex(e.expression, bool);
        const k = kindOf(t);
        const src = exprType(e.expression);
        const loose = !src || /^object\??$/.test(src);
        if (k === 'number' && loose) return `Js.Num(${ex(e.expression)})`;
        if (k === 'string' && src && /^object\??$/.test(src)) return `(${ex(e.expression)} as string)`;
        if (k === 'record' && (!src || !/^JsRecord/.test(src))) {
          // As a record of what the type says (unknown: object).
          const rt = ty(t, mod);
          const v = rt?.match(/^JsRecord<(.+)>\??$/)?.[1];
          return v && !v.includes('TODO') && !/^object\??$/.test(v) ? `Js.AsRecord<${v}>(${ex(e.expression)})` : `Js.AsRecord(${ex(e.expression)})`;
        }
        if (e.expression.type === 'CallExpression' && e.expression.callee.type === 'MemberExpression' && e.expression.callee.object.name === 'JSON' && e.expression.callee.property.name === 'parse') {
          const c = t.type === 'TSTypeLiteral' ? 'JsRecord<object?>' : ty(t, mod);
          if (c && !c.includes('TODO')) { e.expression.__parseAs = c.replace(/\?$/, ''); return ex(e.expression); }
        }
        if (t.type === 'TSTypeLiteral') {
          const c = ty(t, mod);
          if (c && !c.includes('TODO')) return `((${c})(${ex(e.expression)}))`;
        }
        if (k === 'object' && t.type === 'TSTypeReference') {
          const c = ty(t, mod);
          if (c && !c.includes('TODO')) return `((${c})(${ex(e.expression)}))`;
        }
        return ex(e.expression, bool);
      }
      case 'TSNonNullExpression': return `Js.NN(${ex(e.expression)})`;
      case 'ChainExpression': return ex(e.expression, bool);
      case 'UnaryExpression': {
        if (e.operator === '!') return `!${operand(e.argument, true)}`;
        if (e.operator === '-') return `-${operand(e.argument)}`;
        if (e.operator === '+') return `(double)(${ex(e.argument)})`;
        if (e.operator === 'typeof') return `/*TODO typeof*/Js.TypeOf(${ex(e.argument)})`;
        if (e.operator === 'void') return 'null';
        if (e.operator === 'delete') {
          const m = e.argument;
          if (!m.computed && classFields.has(strip(exprType(m.object)) ?? '')) return `${ex(m)} = null`;
          return `${ex(m.object)}.Remove(${m.computed ? `Js.Str(${ex(m.property)})` : str(m.property.name)})`;
        }
        if (e.operator === '~') return `Js.BitNot(${ex(e.argument)})`;
        return `/*TODO unary ${e.operator}*/`;
      }
      case 'UpdateExpression': { const a = e.argument.type === 'TSNonNullExpression' ? e.argument.expression : e.argument; a.__lhs = true; return e.prefix ? `${e.operator}${ex(a)}` : `${ex(a)}${e.operator}`; }
      case 'BinaryExpression': {
        const op = e.operator;
        const l = operand(e.left);
        const r = operand(e.right);
        const stringish = (x) => (x.type === 'Literal' && typeof x.value === 'string') || x.type === 'TemplateLiteral' || (x.type === 'MemberExpression' && !x.computed && ['id', 'name', 'kind', 'label', 'key', 'look', 'type'].includes(x.property.name));
        if (['<', '>', '<=', '>='].includes(op) && (stringish(e.left) || stringish(e.right))) return `string.CompareOrdinal(${ex(e.left)}, ${ex(e.right)}) ${op} 0`;
        const isUndef = (x) => x.type === 'Identifier' && x.name === 'undefined';
        if (['===', '!==', '==', '!='].includes(op) && (isUndef(e.left) || isUndef(e.right))) {
          const other = isUndef(e.left) ? e.right : e.left;
          if (other.type === 'MemberExpression' && other.computed) return `${op.startsWith('!') ? '!' : ''}Js.Missing(${ex(other.object)}, ${ex(other.property)})`;
        }
        const looseL = /^object\??$/.test(exprType(e.left) ?? '');
        const looseR = /^object\??$/.test(exprType(e.right) ?? '');
        if ((looseL || looseR) && ['===', '==', '!==', '!='].includes(op)) return `${op.startsWith('!') ? '!' : ''}Js.Eq(${ex(e.left)}, ${ex(e.right)})`;
        if ((looseL || looseR) && ['<', '>', '<=', '>='].includes(op)) return `${looseL ? `Js.Num(${ex(e.left)})` : l} ${op} ${looseR ? `Js.Num(${ex(e.right)})` : r}`;
        if (op === '===' || op === '==') return `${l} == ${r}`;
        if (op === '!==' || op === '!=') return `${l} != ${r}`;
        if (op === '/') return `((double)(${l}) / (${r}))`;
        // In doubles, as JS: int * int overflows 32 bits (squared distances in SUB units). A double's cast is free.
        if (op === '*') return `((double)(${l}) * (${r}))`;
        if (op === '>>>') return `Js.Ushr(${l}, ${r})`;
        const bit = { '>>': 'Shr', '<<': 'Shl', '&': 'And', '|': 'BitOr', '^': 'Xor' }[op];
        if (bit) return `Js.${bit}(${ex(e.left)}, ${ex(e.right)})`;
        if (op === '**') return `JsMath.pow(${l}, ${r})`;
        if (op === 'in') return `/*TODO in*/Js.In(${l}, ${r})`;
        if (op === 'instanceof') return `/*TODO instanceof*/(${l} is ${r})`;
        return `${l} ${op} ${r}`;
      }
      case 'LogicalExpression': {
        if (e.operator === '??' && e.right.type === 'Literal' && typeof e.right.value === 'number') return `Js.Q(${ex(e.left)}, ${ex(e.right)})`;
        if (e.operator === '??' && e.left.type === 'MemberExpression' && e.left.computed) return `Js.At(${ex(e.left.object)}, ${ex(e.left.property)}, ${ex(e.right)})`;
        if (e.operator === '??') return `${operand(e.left)} ?? ${operand(e.right)}`;
        // a && b as a value, a a condition and b not: b, or a's false (as JS gives).
        if (!bool && e.operator === '&&' && !isBoolish(e.right) && isBoolish(e.left) && exprType(e.right) !== 'bool') return `(${ex(e.left, true)} ? (object?)(${ex(e.right)}) : false)`;
        if (!bool && e.operator === '&&' && !isBoolish(e.right) && !isBoolish(e.left) && !['int', 'double', 'string', 'bool'].includes(strip(exprType(e.left)) ?? 'x')) return `(${ex(e.left, true)} ? ${operand(e.right)} : null)`;
        if (!bool && e.operator === '||' && !isBoolish(e.left) && !isBoolish(e.right)) {
          const v = `__or${tmp++}`;
          return `(${operand(e.left)} is var ${v} && Js.T(${v}) ? ${v} : ${operand(e.right)})`;
        }
        return `${operand(e.left, true)} ${e.operator} ${operand(e.right, true)}`;
      }
      case 'ConditionalExpression': {
        const lt = [e.consequent, e.alternate].every((x) => x.type === 'ArrayExpression') ? literalType(e.consequent) ?? literalType(e.alternate) : null;
        if (lt) return `${operand(e.test, true)} ? ((${lt})${ex(e.consequent)}) : ((${lt})${ex(e.alternate)})`;
        return `${operand(e.test, true)} ? ${operand(e.consequent)} : ${operand(e.alternate)}`;
      }
      case 'AssignmentExpression': {
        e.left.__lhs = true;
        if (e.operator === '=' && e.left.type === 'MemberExpression' && e.left.computed && /^(double|int|bool)\?$/.test(exprType(e.right) ?? '') && /^(double|int|bool)$/.test(exprType(e.left) ?? '')) e.right.__nn = true;
        if (e.left.type === 'TSNonNullExpression') e.left.expression.__lhs = true;
        if (e.left.type === 'TSNonNullExpression') return ex({ ...e, left: e.left.expression }, bool);
        if (e.operator === '=' && e.left.type === 'MemberExpression' && !e.left.computed && !e.__intDone) {
          const ft = classFields.get(strip(exprType(e.left.object)) ?? '')?.get(e.left.property.name);
          const rt = exprType(e.right);
          if (ft === 'int' && rt !== 'int') return `${ex(e.left)} = Js.I(${ex(e.right)})`;
          if (ft && /^(double|bool)$/.test(ft) && /^(double|int|bool)\?$/.test(rt ?? '')) return `${ex(e.left)} = Js.NN(${ex(e.right)})`;
        }
        if (['+=', '-='].includes(e.operator) && e.left.type === 'MemberExpression' && !e.left.computed) {
          const ft = classFields.get(strip(exprType(e.left.object)) ?? '')?.get(e.left.property.name);
          if (ft === 'int' && exprType(e.right) !== 'int') return `${ex(e.left)} = Js.I(${ex(e.left)} ${e.operator[0]} ${operand(e.right)})`;
        }
        if (e.operator === '=' && e.right.type === 'ObjectExpression' && e.left.type === 'Identifier' && classFields.has(strip(typeOf(e.left.name)) ?? '')) e.right.__class = strip(typeOf(e.left.name));
        if (e.operator === '=' && e.right.type === 'ObjectExpression' && e.left.type === 'MemberExpression' && !e.left.computed && /^JsRecord</.test(classFields.get(strip(exprType(e.left.object)) ?? '')?.get(e.left.property.name) ?? '')) e.right.__asRecord = true;
        if (e.operator === '=' && e.right.type === 'ObjectExpression' && e.left.type === 'MemberExpression' && !e.left.computed) {
          const ft = strip(classFields.get(strip(exprType(e.left.object)) ?? '')?.get(e.left.property.name) ?? '');
          if (ft && classFields.has(ft)) {
            e.right.__class = ft;
            if (e.right.properties.some((q) => q.type === 'SpreadElement')) spreadAs = ft;
          }
        }
        if (e.operator === '=' && e.left.type === 'MemberExpression' && e.left.computed && fieldByKey(e.left)) return `Js.SetProp(${operand(e.left.object)}, ${ex(e.left.property)}, ${ex(e.right)})`;
        if (e.right.type === 'ArrowFunctionExpression' && e.left.type === 'MemberExpression' && !e.left.computed) e.right.__arity = fieldArity(e.left.property.name);
        if (e.left.type === 'ArrayPattern') for (const x of e.left.elements) if (x) x.__lhs = true;
        if (e.left.type === 'ArrayPattern' && e.left.elements.every((x) => x && x.type !== 'RestElement')) {
          const l = `(${e.left.elements.map((x) => ex(x)).join(', ')})`;
          return e.right.type === 'ArrayExpression' ? `${l} = (${e.right.elements.map((x) => ex(x)).join(', ')})` : `${l} = ${ex(e.right)}`;
        }
        if (e.left.type === 'ObjectPattern' || e.left.type === 'ArrayPattern') return `/*TODO destructuring assignment*/`;
        if (e.operator === '||=') return `${ex(e.left)} = (Js.T(${ex(e.left)}) ? ${ex(e.left)} : ${operand(e.right)})`;
        if (e.operator === '&&=') return `/*TODO &&=*/`;
        if (e.operator === '/=') return `${ex(e.left)} = ((double)(${ex(e.left)}) / (${ex(e.right)}))`;
        if (e.operator === '>>>=') return `${ex(e.left)} = Js.Ushr(${ex(e.left)}, ${ex(e.right)})`;
        if (e.operator === '=' && e.left.type === 'MemberExpression' && !e.left.computed && INT_FIELDS.has(e.left.property.name)) return `${ex(e.left)} = Js.I(${ex(e.right)})`;
        return `${ex(e.left)} ${e.operator} ${ex(e.right)}`;
      }
      case 'SequenceExpression': return `/*TODO sequence*/`;
      case 'MemberExpression': {
        const obj = e.object.type === 'Super' ? 'base' : operand(e.object);
        const q = e.optional ? '?' : '';
        const byKey = e.computed ? fieldByKey(e) : null;
        if (byKey) return `Js.GetProp<${byKey}>(${obj}, ${ex(e.property)})`;
        if (e.computed && e.property.type === 'Literal' && typeof e.property.value === 'number' && /^\(/.test(strip(exprType(e.object)) ?? '')) return `${obj}${q}.Item${e.property.value + 1}`;
        if (e.computed && e.object.type === 'ArrayExpression' && literalType(e.object)) return `new ${literalType(e.object)} { ${e.object.elements.map((x) => ex(x)).join(', ')} }[Js.Idx(${ex(e.property)})]`;
        if (e.computed && e.object.type === 'ObjectExpression' && e.object.properties.every((x) => x.type === 'Property' && !x.computed && (strip(exprType(x.value)) === 'string' || (x.value.type === 'ConditionalExpression' && strip(exprType(x.value.consequent)) === 'string')))) return `new JsRecord<string> { ${e.object.properties.map((x) => `[${str(String(x.key.name ?? x.key.value))}] = ${ex(x.value)}`).join(', ')} }[Js.Str(${ex(e.property)})]`;
        // A read by index: JS's undefined past the end (C# would throw). Writes use the indexer.
        if (e.computed) return e.__lhs || q ? `${obj}${q}[Js.Idx(${ex(e.property)})]` : `Js.Get(${obj}, ${ex(e.property)})`;
        const name = e.property.name;
        // Math.PI and friends.
        if (e.object.type === 'Identifier' && !imports.has(e.object.name)) {
          if (e.object.name === 'Math') return `JsMath.${name}`;
          if (e.object.name === 'Number') return `JsNumber.${name}`;
        }
        if (e.object.type === 'Identifier' && recordConsts.has(e.object.name)) return `${obj}[${str(name)}]`;
        if (/^JsRecord</.test(exprType(e.object) ?? '')) return `${obj}${q}[${str(name)}]`;
        return `${obj}${q}.${id(name)}`;
      }
      case 'CallExpression': return call(e);
      case 'NewExpression': return newEx(e);
      case 'ArrayExpression': {
        if (e.__loose) return `Js.Arr([${e.elements.map((x) => (x?.type === 'SpreadElement' ? `.. ${ex(x.argument)}` : x ? ex(x) : 'null')).join(', ')}])`;
        if (e.__tuple) return `(${e.elements.map((x) => ex(x)).join(', ')})`;
        if (e.elements.length >= 2 && e.elements.length <= 4 && e.elements.every((x) => x && x.type !== 'SpreadElement')) {
          const num = (x) => (['int', 'double'].includes(x) ? 'double' : x);
          const ts = e.elements.map((x) => (x.type === 'Literal' && typeof x.value === 'number' ? 'double' : x.type === 'Literal' && typeof x.value === 'string' ? 'string' : strip(exprType(x))));
          if (ts.length === 2 && ts[0] === 'string' && ts[1] && ts[1] !== 'string') return `(${e.elements.map((x) => ex(x)).join(', ')})`;
        }
        if (e.elements.length === 1 && e.elements[0]?.type === 'SpreadElement') return `(${ex(e.elements[0].argument)}).ToList()`;
        const spreadOf = (a) => {
          while (a.type === 'ParenthesizedExpression') a = a.expression;
          if (a.type !== 'ConditionalExpression') return ex(a);
          const unp = (s) => { while (s.type === 'ParenthesizedExpression' || (s.type === 'TSAsExpression' && s.expression.type !== 'CallExpression')) s = s.expression; return s; };
          a = { ...a, consequent: unp(a.consequent), alternate: unp(a.alternate) };
          const sides = [a.consequent, a.alternate];
          const known = sides.map((s) => (s.type === 'ArrayExpression' ? literalType(s) : strip(exprType(s)))).find((x) => x && /^List</.test(x));
          if (!known) return ex(a);
          const side = (s) => (s.type === 'ArrayExpression' ? `((${known})${ex(s)})` : operand(s));
          return `(${ex(a.test, true)} ? ${side(a.consequent)} : ${side(a.alternate)})`;
        };
        const items = e.elements.map((x) => (x === null ? 'default' : x.type === 'SpreadElement' ? `.. ${spreadOf(x.argument)}` : ex(x)));
        const isPoint = (x) => x?.type === 'ObjectExpression' && x.properties.length === 2 && x.properties.every((q) => q.type === 'Property') && x.properties.map((q) => q.key.name).sort().join(',') === 'x,y';
        if (e.elements.length && e.elements.every(isPoint)) return `new List<Point> { ${items.join(', ')} }`;
        return `[${items.join(', ')}]`;
      }
      case 'ObjectExpression':
        if (e.__loose) return looseObj(e);
        if (e.__asRecord) return recordInit(e);
        return obj(e);
      case 'ArrowFunctionExpression': case 'FunctionExpression': return lambda(e);
      case 'SpreadElement': return `/*TODO spread*/${ex(e.argument)}`;
      case 'AwaitExpression': return '/*TODO await*/';
      default: return `/*TODO ${e.type}*/`;
    }
  }

  /** Marks list and object literals (and those inside them) to be written as loose JS values. */
  function markLoose(n) {
    if (!n || typeof n !== 'object') return;
    if (n.type === 'ArrayExpression' || n.type === 'ObjectExpression') {
      n.__loose = true;
      for (const x of n.type === 'ArrayExpression' ? n.elements : n.properties) markLoose(x?.type === 'Property' ? x.value : x?.type === 'SpreadElement' ? null : x);
    }
  }
  function looseObj(o) {
    const sp = o.properties.filter((q) => q.type === 'SpreadElement');
    if (sp.length === 1 && o.properties[0] === sp[0]) return `Js.ObjFrom(${ex(sp[0].argument)}${o.properties.slice(1).map((q) => `, (${str(String(q.key.name ?? q.key.value))}, (object?)(${q.shorthand ? ex(q.key) : ex(q.value)}))`).join('')})`;
    const parts = o.properties.map((q) => (q.type === 'SpreadElement' ? `/*TODO spread*/` : `(${str(String(q.key.name ?? q.key.value))}, (object?)(${q.shorthand ? ex(q.key) : ex(q.value)}))`));
    return `Js.Obj(${parts.join(', ')})`;
  }
  /** expect(x) or expect(x).not: the start of a vitest assertion. */
  const isExpect = (n) => (n?.type === 'CallExpression' && n.callee.type === 'Identifier' && n.callee.name === 'expect') || (n?.type === 'MemberExpression' && !n.computed && n.property.name === 'not' && isExpect(n.object));
  function call(e) {
    const c = e.callee;
    if (c.type === 'Identifier' && c.name === 'structuredClone' && !imports.has('structuredClone')) return `Js.DeepClone(${ex(e.arguments[0])})`;
    if (isTest && c.type === 'Identifier' && c.name === 'expect' && e.arguments[0]?.type === 'MemberExpression' && e.arguments[0].computed) {
      const m = e.arguments[0];
      return `expect(Js.Missing(${ex(m.object)}, ${ex(m.property)}) ? null : (object?)(${ex(m)})${e.arguments[1] ? `, ${ex(e.arguments[1])}` : ''})`;
    }
    if (isTest && ((c.type === 'MemberExpression' && isExpect(c.object)) || isExpect(e) || (c.type === 'MemberExpression' && c.object.type === 'Identifier' && c.object.name === 'expect'))) e.arguments.forEach(markLoose);
    if (isTest && c.type === 'MemberExpression' && c.object.type === 'Identifier' && c.object.name === 'expect' && !c.computed) return `Vitest.${c.property.name}(${e.arguments.map((a) => ex(a)).join(', ')})`;
    if ((c.type === 'ArrowFunctionExpression' || c.type === 'FunctionExpression') && e.arguments.length === 0) return `Js.Run(${lambda(c)})`;
    if (c.type === 'MemberExpression' && !c.computed && PREDICATE_METHODS.has(c.property.name)) for (const a of e.arguments) if (a.type === 'ArrowFunctionExpression') a.__pred = true;
    // Number and String passed as callbacks (.map(Number)): as lambdas.
    // A function of this module's passed by name: a lambda calling it (C# won't convert a local function taking
    // JS numbers to a delegate taking ints; a lambda converts either way).
    const passed = (a) => {
      const imported = imports.has(a.name) ? fnParams.get(imports.get(a.name)) : null;
      const n = paramTypes.get(a.name)?.length ?? imported?.filter((q) => !q.optional).length;
      if (n === undefined || typeOf(a.name)) return null;
      const vs = Array.from({ length: n }, (_, i) => `__f${i}`);
      return `(${vs.join(', ')}) => ${ex(a)}(${vs.join(', ')})`;
    };
    const arg = (a) => (a.type === 'Identifier' && a.name === 'Boolean' && !imports.has('Boolean') ? '(__v) => Js.T(__v)' : a.type === 'MemberExpression' && !a.computed && ['Number', 'Math'].includes(a.object.name) ? `(__v) => ${ex(a)}(__v)` : a.type === 'Identifier' && a.name === 'Number' && !imports.has('Number') ? '(__v) => Js.Num(__v)' : a.type === 'Identifier' && a.name === 'String' && !imports.has('String') ? '(__v) => Js.Str(__v)' : a.type === 'Identifier' && passed(a) ? passed(a) : ex(a));
    const callbackTakers = ['map', 'filter', 'forEach', 'some', 'every', 'find', 'findIndex', 'findLast', 'flatMap'];
    // A function of this module's (its parameters' types), or a push onto a state field's list (its items' type).
    const listOf = (field) => {
      const ts = [...(fieldTypes.get(field) ?? [])].map((x) => x.match(/^List<(\w+)>$/)?.[1]);
      return ts.length && ts.every((x) => x && x === ts[0]) ? ts[0] : null;
    };
    const pushed = c.type === 'MemberExpression' && !c.computed && ['push', 'unshift'].includes(c.property.name)
      ? (c.object.type === 'MemberExpression' && !c.object.computed ? listOf(c.object.property.name) : null) ?? strip(exprType(c.object))?.match(/^List<(\w+)>$/)?.[1] ?? null
      : null;
    const known = c.type === 'Identifier' ? paramTypes.get(c.name) : pushed ? e.arguments.map(() => pushed) : null;
    const withTarget = (a, i) => {
      const target = known?.[i] ?? params?.[i]?.type?.replace(/\?$/, '');
      if (a.type === 'ObjectExpression' && target && /^\w+$/.test(target) && a.properties.some((q) => q.type === 'SpreadElement')) spreadAs = target;
      return a;
    };
    if (c.type === 'MemberExpression' && !c.computed && callbackTakers.concat(['sort', 'reduce', 'findLast']).includes(c.property.name)) {
      const item = strip(exprType(c.object))?.match(/^List<(.+)>$/)?.[1];
      if (item) for (const a of e.arguments) if (a.type === 'ArrowFunctionExpression') a.__paramTypes = c.property.name === 'sort' ? [item, item] : c.property.name === 'reduce' ? [null, item, 'int'] : [item, 'int'];
    }
    if (c.type === 'MemberExpression' && !c.computed && c.property.name === 'reduce' && e.arguments[1]?.type === 'Literal') e.arguments[1].__double = true;
    if (c.type === 'MemberExpression' && !c.computed && c.property.name === 'reduce' && e.arguments[1] && e.arguments[1].type !== 'Literal' && strip(exprType(e.arguments[1])) === 'int') e.arguments[1].__toDouble = true;
    if (c.type === 'MemberExpression' && !c.computed && ['push', 'unshift'].includes(c.property.name) && /^List<\(/.test(strip(exprType(c.object)) ?? '')) for (const a of e.arguments) if (a.type === 'ArrayExpression') a.__tuple = true;
    const calleeKey = c.type === 'Identifier' ? imports.get(c.name) ?? `${modClass(mod)}.${c.name}` : c.type === 'MemberExpression' && !c.computed && c.object.type === 'Identifier' && /^[A-Z]/.test(c.object.name) ? `${c.object.name}.${c.property.name}` : null;
    const params = calleeKey ? fnParams.get(calleeKey) : null;
    if (params) {
      const isUndef = (a) => a.type === 'Identifier' && a.name === 'undefined';
      let named = false;
      const out = [];
      e.arguments.forEach((a, i) => {
        const pi = params[i];
        if (!pi || a.type === 'SpreadElement') { out.push(a.type === 'SpreadElement' ? `[.. ${ex(a.argument)}]` : arg(a)); return; }
        // An argument left undefined: the parameter's default (C#: name the ones after it).
        if (isUndef(a) && pi.optional) { named = true; return; }
        let text;
        if (a.type === 'ObjectExpression' && classFields.has(strip(pi.type)) && !a.properties.some((q) => q.type === 'SpreadElement')) a.__class = strip(pi.type);
        if (a.type === 'ArrowFunctionExpression') {
          const fm = pi.type.match(/^(Func|Action)<(.*)>\??$/);
          if (fm) {
            let depth = 0;
            let n = 1;
            for (const ch of fm[2]) {
              if (ch === '<' || ch === '(') depth++;
              else if (ch === '>' || ch === ')') depth--;
              else if (ch === ',' && depth === 0) n++;
            }
            a.__arity = fm[1] === 'Func' ? n - 1 : n;
          } else if (pi.type === 'Action') a.__arity = 0;
        }
        if (a.type === 'ObjectExpression' && /^JsRecord</.test(pi.type)) a.__asRecord = true;
        if (a.type === 'NewExpression' && ['Set', 'Map'].includes(a.callee.name) && !a.typeArguments && /^Js(Set|Map)</.test(pi.type)) text = `new ${pi.type.replace(/\?$/, '')}()`;
        else {
          withTarget(a, i);
          text = arg(a);
          const at = exprType(a);
          // A nullable number TypeScript has narrowed (checked for null just before): its value.
          if (at && /^(double|int|bool)\?$/.test(at) && /^(double|int|bool)$/.test(pi.type)) text = `Js.NN(${text})`;
        }
        out.push(named ? `${id(pi.name)}: ${text}` : text);
      });
      if (e.arguments.length && !e.arguments.some((a) => a.type === 'SpreadElement')) {
        const argsText = out.join(', ');
        e.__args = argsText;
      }
    }
    const args = () => e.__args ?? e.arguments.map(withTarget).map((a) => (a.type === 'SpreadElement' ? `[.. ${ex(a.argument)}]` : (a.type === 'ArrowFunctionExpression' && a.params.length === 0 && c.type === 'MemberExpression' && callbackTakers.includes(c.property?.name) ? arg(a).replace(/^\(\)\s*=>/, '_ =>') : arg(a)))).join(', ');
    const q = e.optional ? '?' : '';
    if (c.type === 'MemberExpression' && !c.computed) {
      const o = c.object;
      const name = c.property.name;
      if ((name === 'push' || name === 'unshift') && e.arguments.length > 1 && !e.arguments.some((a) => a.type === 'SpreadElement')) return `${operand(o)}.${name}All([${e.arguments.map((a) => ex(a)).join(', ')}])`;
      if ((name === 'push' || name === 'unshift') && e.arguments.some((a) => a.type === 'SpreadElement')) {
        return e.arguments.map((a) => (a.type === 'SpreadElement' ? `${operand(o)}.${name}All(${ex(a.argument)})` : `${operand(o)}.${name}(${ex(a)})`)).join('; ');
      }
      if (o.type === 'Identifier' && !imports.has(o.name)) {
        if (o.name === 'Math') {
          if (e.arguments.length === 1 && e.arguments[0].type === 'SpreadElement' && (name === 'max' || name === 'min')) return `JsMath.${name}(${ex(e.arguments[0].argument)})`;
          return `JsMath.${name}(${args()})`;
        }
        if (o.name === 'String' && !imports.has('String')) return `JsString.${name}(${args()})`;
        if (o.name === 'Number') return `JsNumber.${name}(${args()})`;
        if (o.name === 'Object') {
          if (name === 'keys') return `Js.ObjectKeys(${args()})`;
          if (name === 'fromEntries') {
            const a0 = e.arguments[0];
            if (a0?.type === 'CallExpression' && a0.callee.type === 'MemberExpression' && a0.callee.property?.name === 'map') {
              const fn = a0.arguments[0];
              const pair = (x) => { if (x?.type === 'ArrayExpression' && x.elements.length === 2) x.__tuple = true; };
              if (fn?.type === 'ArrowFunctionExpression') {
                if (fn.body.type === 'ArrayExpression') pair(fn.body);
                else if (fn.body.type === 'BlockStatement') for (const s of fn.body.body) if (s.type === 'ReturnStatement') pair(s.argument);
              }
            }
            return `Js.Object_fromEntries(${args()})`;
          }
          if (name === 'values') return `Js.ObjectValues(${args()})`;
          if (name === 'entries') return `Js.ObjectEntries(${args()})`;
          // Object.assign(target, { a, b }): the target, with those set.
          if (name === 'assign' && e.arguments.length === 2 && e.arguments[1].type === 'ObjectExpression' && e.arguments[1].properties.every((q) => q.type === 'Property' && !q.computed)) {
            const v = `__a${tmp++}`;
            const sets = e.arguments[1].properties.map((q) => {
              const k = String(q.key.name ?? q.key.value);
              const val = q.shorthand ? ex(q.key) : q.value.type === 'ObjectExpression' && recordField(k) ? recordInit(q.value) : ex(q.value);
              return `${v}.${id(k)} = ${INT_FIELDS.has(k) ? `Js.I(${val})` : val};`;
            });
            return `Js.With(${ex(e.arguments[0])}, ${v} => { ${sets.join(' ')} })`;
          }
          return `/*TODO Object.${name}*/Js.Object_${name}(${args()})`;
        }
        const a0 = e.arguments[0];
        if (o.name === 'JSON' && name === 'parse' && a0?.type === 'CallExpression' && a0.callee.type === 'MemberExpression' && a0.callee.object.name === 'JSON' && a0.callee.property.name === 'stringify' && a0.arguments.length === 1) return `Js.DeepClone(${ex(a0.arguments[0])})`;
        if (o.name === 'JSON' && name === 'stringify') { e.arguments.forEach(markLoose); return `Js.Json_stringify(${args()})`; }
        if (o.name === 'JSON' && name === 'parse') return `Js.JsonParse<${e.__parseAs ?? 'System.Text.Json.Nodes.JsonNode'}>(${args()})`;
        if (o.name === 'Array' && name === 'from') {
          const [src, fn] = e.arguments;
          if (src?.type === 'ObjectExpression' && src.properties[0]?.key?.name === 'length') return `Js.ArrayFrom${e.__targ ? `<${e.__targ}>` : ''}(${ex(src.properties[0].value)}, ${fn ? lambdaIndexOnly(fn) : '_ => default'})`;
          if (fn) return `Js.ArrayFromMap(${ex(src)}, ${arg(fn)})`;
          return `(${ex(src)}).ToList()`;
        }
        if (o.name === 'Array' && name === 'isArray') return `(${args()} is System.Collections.IList)`;
      }
      if (name === 'toString' && e.arguments.length === 0) return `Js.Str(${ex(o)})`;
      if (name === 'fill' && (o.type === 'NewExpression' || o.type === 'CallExpression') && o.callee.name === 'Array' && o.arguments.length === 1) {
        const v = e.arguments[0];
        const as = e.__fillAs ?? (v.type === 'Literal' && typeof v.value === 'number' ? 'double' : strip(exprType(v)));
        return `Js.Filled${as ? `<${as}>` : ''}(${ex(o.arguments[0])}, ${ex(v)})`;
      }
      if (o.type === 'ArrayExpression' && o.elements.length && /^List<List</.test(literalType(o) ?? '') && o.elements.every((x) => x?.type === 'ArrayExpression')) return `new ${literalType(o)} { ${o.elements.map((x) => `new ${literalType(x)} { ${x.elements.map((y) => ex(y)).join(', ')} }`).join(', ')} }.${id(name)}(${args()})`;
      if (o.type === 'ArrayExpression' && o.elements.length && literalType(o) && o.elements.every((x) => x && (x.type === 'Literal' || x.type === 'UnaryExpression' || ['double', 'int', 'string'].includes(strip(exprType(x)))))) return `new ${literalType(o)} { ${o.elements.map((x) => ex(x)).join(', ')} }.${id(name)}(${args()})`;
      if (o.type === 'ArrayExpression' && o.elements.length && !literalType(o) && !o.elements.every((x) => x?.type === 'ObjectExpression')) {
        const parts = [];
        let run = [];
        for (const x of o.elements) {
          if (x?.type === 'SpreadElement') {
            if (run.length) parts.push(`new[] { ${run.join(', ')} }`);
            run = [];
            parts.push(strip(exprType(x.argument)) === 'string' ? `Js.Chars(${ex(x.argument)})` : ex(x.argument));
          } else run.push(x ? ex(x) : 'default');
        }
        if (run.length) parts.push(`new[] { ${run.join(', ')} }`);
        const its = o.elements.map((x) => (x?.type === 'SpreadElement' ? strip(exprType(x.argument))?.match(/^List<(\w+)>$/)?.[1] : strip(exprType(x))));
        const bodies = its.length > 1 && its.every((x) => x && bodyClasses.has(x)) && new Set(its).size > 1;
        if (parts.length === 1 && !o.elements.some((x) => x?.type === 'SpreadElement')) return `${parts[0]}.ToList().${id(name)}(${args()})`;
        return `Js.ListOf${bodies ? '<IBody>' : ''}(${parts.join(', ')}).${id(name)}(${args()})`;
      }
      if (e.__targ && name === 'map') for (const a of e.arguments) if (a.type === 'ArrowFunctionExpression') a.__ret = e.__targ;
      return `${operand(o)}${c.optional ? '?' : ''}.${id(name)}(${args()})`;
    }
    if (c.type === 'Identifier') {
      if (c.name === 'String') return `Js.Str(${args()})`;
      if (c.name === 'Number') return `Js.Num(${args()})`;
      if (c.name === 'Boolean') return `Js.T(${args()})`;
      if (c.name === 'isFinite') return `JsNumber.isFinite(${args()})`;
      if (c.name === 'isNaN') return `JsNumber.isNaN(${args()})`;
    }
    if (e.optional) return `${ex(c)}?.Invoke(${args()})`;
    return `${ex(c)}(${args()})`;
  }

  /** (_, i) => f(i) for Array.from({ length }, ...). */
  function lambdaIndexOnly(fn) {
    const i = fn.params[1]?.name ?? '_i';
    const body = fn.body.type === 'BlockStatement' ? block(fn.body, '        ', { fnRet: null }) : ex(fn.body);
    return `(int ${id(i)}) => ${body}`;
  }

  function newEx(e) {
    if (e.__newAs) return `new ${e.__newAs}()`;
    const c = e.callee.type === 'Identifier' ? e.callee.name : null;
    const targs = e.typeArguments?.params ?? [];
    const args = e.arguments.map((a) => ex(a)).join(', ');
    const typed = { Int32Array: 'int', Uint32Array: 'uint', Float64Array: 'double', Uint8Array: 'byte', Int8Array: 'sbyte', Uint16Array: 'ushort', Float32Array: 'float' };
    if (c && typed[c]) {
      const a = e.arguments[0];
      if (a?.type === 'ArrayExpression') return `new ${c}(new double[] { ${a.elements.map((x) => ex(x)).join(', ')} })`;
      return `new ${c}(${args})`;
    }
    if (c === 'Map' || c === 'WeakMap') {
      // new Map(list.map((x) => [key, value])): typed from the list.
      const src = e.arguments[0];
      if (targs.length !== 2 && src?.type === 'CallExpression' && src.callee.type === 'MemberExpression' && src.callee.property.name === 'map' && src.arguments[0]?.type === 'ArrowFunctionExpression' && src.arguments[0].body.type === 'ArrayExpression' && src.arguments[0].body.elements.length === 2 && src.arguments[0].params[0]?.type === 'Identifier') {
        const fn = src.arguments[0];
        const pn = id(fn.params[0].name);
        const [k, v] = fn.body.elements;
        return `Js.MapOf(${operand(src.callee.object)}, (${pn}) => ${ex(k)}, (${pn}) => ${ex(v)})`;
      }
      if (targs.length === 2) return `new JsMap<${ty(targs[0], mod).replace(/\?$/, '')}, ${ty(targs[1], mod)}>(${args})`;
      return `/*TODO Map type*/new JsMap<object, object>(${args})`;
    }
    if (c === 'Set') {
      if (targs.length === 1) return `new JsSet<${ty(targs[0], mod).replace(/\?$/, '')}>(${args})`;
      if (e.arguments.length === 1) return `Js.Set(${args})`;
      return `/*TODO Set type*/new JsSet<object>()`;
    }
    if (c === 'Array') {
      return `/*TODO new Array*/new List<object>()`;
    }
    if (c === 'Error') return `new Exception(${args})`;
    return `new ${c}(${args})`;
  }

  /** An object literal as a JsRecord: new() { ["k"] = v }. */
  function recordInit(o) {
    for (const q of o.properties) {
      let a = q.type === 'SpreadElement' ? q.argument : null;
      while (a?.type === 'ParenthesizedExpression') a = a.expression;
      if (a?.type === 'ConditionalExpression') for (const s of [a.consequent, a.alternate]) if (s.type === 'ObjectExpression') s.__asRecord = true;
    }
    const spreads = o.properties.filter((q) => q.type === 'SpreadElement');
    const keyOf = (q) => (q.computed ? `Js.Str(${ex(q.key)})` : str(String(q.key.name ?? q.key.value)));
    if (spreads.length > 1 && o.properties[0].type === 'SpreadElement') {
      // { ...a, ...b, k: v }: a copy of a, then the rest in order.
      const v = `__r${tmp++}`;
      const steps = o.properties.slice(1).map((q) => (q.type === 'SpreadElement' ? `Js.MergeInto(${v}, ${ex(q.argument)});` : `${v}[${keyOf(q)}] = ${ex(q.value)};`));
      return `Js.RecordFrom(${ex(o.properties[0].argument)}, ${v} => { ${steps.join(' ')} })`;
    }
    if (spreads.length === 1 && o.properties[0].type === 'SpreadElement') return `new(${ex(spreads[0].argument)}) { ${o.properties.slice(1).map((q) => `[${str(String(q.key.name ?? q.key.value))}] = ${ex(q.value)}`).join(', ')} }`;
    return `new() { ${o.properties.map((p) => (p.type === 'Property' ? `[${keyOf(p)}] = ${ex(p.value)}` : '[Js.TODO_SPREAD] = default!')).join(', ')} }`;
  }

  function obj(e) {
    const sp = e.properties.find((q) => q.type === 'SpreadElement');
    if (sp && /^(JsRecord|Dictionary)</.test(strip(exprType(sp.argument)) ?? '')) return recordInit(e);
    e.__spreadAs = spreadAs;
    spreadAs = null;
    const r = obj0(e);
    return e.__class && r.startsWith('new() ') ? `new ${e.__class} ${r.slice(6)}` : r;
  }

  function obj0(e) {
    const parts = [];
    let spread = null;
    const pointLit = e.properties.length === 2 && e.properties.every((q) => q.type === 'Property' && !q.computed) && e.properties.map((q) => q.key.name).sort().join(',') === 'x,y';
    const fields = e.__class ? classFields.get(e.__class) : pointLit ? classFields.get('Point') : null;
    for (const p of e.properties) {
      if (fields && p.type === 'Property' && !p.computed) {
        const ft = fields.get(String(p.key.name ?? p.key.value));
        const filled = ft?.match(/^List<(.+)>\??$/)?.[1];
        if (filled && p.value.type === 'CallExpression' && p.value.callee.type === 'MemberExpression' && p.value.callee.property?.name === 'fill' && p.value.callee.object.type === 'NewExpression') p.value.__fillAs = filled;
        const item = ft?.match(/^List<(\w+)>\??$/)?.[1];
        if (item && p.value.type === 'CallExpression' && p.value.callee.type === 'MemberExpression' && p.value.callee.property?.name === 'map') p.value.__targ = item;
        if (ft && classFields.has(strip(ft)) && p.value.type === 'ObjectExpression') p.value.__class = strip(ft);
        if (ft && /^(double|int|bool)$/.test(ft) && /^(double|int|bool)\?$/.test(exprType(p.value) ?? '')) { p.value.__nn = true; p.key.__nn = true; }
        if (ft && /^Js(Map|Set)</.test(ft) && p.value.type === 'NewExpression' && ['Map', 'Set', 'WeakMap'].includes(p.value.callee.name) && !p.value.typeArguments && !p.value.arguments.length) p.value.__newAs = strip(ft);
      }
      if (p.type === 'SpreadElement') {
        spread = p.argument;
        continue;
      }
      if (p.computed) return `/*TODO computed key*/new()`;
      const key = p.key.type === 'Identifier' ? p.key.name : p.key.value;
      if (p.value.type === 'ArrowFunctionExpression') p.value.__arity = fieldArity(String(key));
      const value = p.shorthand ? ex(p.key) : p.value.type === 'ArrowFunctionExpression' || p.value.type === 'FunctionExpression' ? lambda(p.value) : p.value.type === 'ObjectExpression' && recordField(String(key)) ? recordInit(p.value) : ex(p.value);
      parts.push(`${id(String(key))} = ${INT_FIELDS.has(String(key)) && e.__class !== 'PointD' ? `Js.I(${value})` : value}`);
    }
    if (spread) {
      const first = e.properties[0];
      const spreads = e.properties.filter((p) => p.type === 'SpreadElement').length;
      // { ...(test ? { a, b } : {}), c }: c, and a and b when test holds (into the class it's going to).
      let cond = spread;
      while (cond.type === 'ParenthesizedExpression') cond = cond.expression;
      const target = e.__spreadAs ?? e.__class;
      const plainObj = (o) => o.type === 'ObjectExpression' && o.properties.every((q) => q.type === 'Property' && !q.computed);
      if (spreads === 1 && target && cond.type === 'ConditionalExpression' && plainObj(cond.consequent) && plainObj(cond.alternate)) {
        const v = `__s${tmp++}`;
        const sets = (o) => o.properties.map((q) => { const k = String(q.key.name ?? q.key.value); const val = q.shorthand ? ex(q.key) : ex(q.value); return `${v}.${id(k)} = ${INT_FIELDS.has(k) ? `Js.I(${val})` : val};`; }).join(' ');
        return `Js.With(new ${target} { ${parts.join(', ')} }, ${v} => { if (${ex(cond.test, true)}) { ${sets(cond.consequent)} } else { ${sets(cond.alternate)} } })`;
      }
      if (spreads === 1 && spread.type === 'CallExpression' && spread.callee.name === 'placedAt') {
        const at = `Motion.placedAt(${spread.arguments.map((a) => ex(a)).join(', ')})`;
        return `new() { ${[...parts, `at = ${at}.at`, `was = ${at}.was`].join(', ')} }`;
      }
      // { ...s, a } and { a, ...s }: a copy of s with a set. (For { a, ...s } that assumes s has no `a` of its
      // own, as with an Omit<T, 'a'>: a field s did carry would win in JS.)
      if (spreads === 1 && (first.type === 'SpreadElement' || e.properties.at(-1).type === 'SpreadElement')) {
        const as = e.__spreadAs;
        const copy = as ? `Js.CopyAs<${as}>(${ex(spread)})` : `Js.Clone(${ex(spread)})`;
        if (!parts.length) return copy;
        const v = `__s${tmp++}`;
        // { a, b, ...t } (the spread last) of t's own class: a, b, then whatever t has set over them, as in JS.
        const target = as ?? e.__class ?? strip(exprType(spread));
        if (first.type !== 'SpreadElement' && target && classFields.has(target) && strip(exprType(spread)) === target) return `Js.With(new ${target} { ${parts.join(', ')} }, ${v} => Js.AssignSet(${v}, ${ex(spread)}))`;
        return `Js.With(${copy}, ${v} => { ${parts.map((x) => `${v}.${x};`).join(' ')} })`;
      }
      return `/*TODO object spread*/Js.Spread(${ex(spread)}, new() { ${parts.join(', ')} })`;
    }
    const keys = e.properties.map((p) => String(p.key?.name ?? p.key?.value)).sort().join(',');
    if (keys === 'x,y') return `new Point { ${parts.join(', ')} }`;
    return parts.length ? `new() { ${parts.join(', ')} }` : 'new()';
  }

  function param(p, defaults, local = false) {
    const pname = (n) => (scopes.length ? declName(n) : id(n));
    let name;
    let t;
    let def = null;
    if (p.type === 'AssignmentPattern') {
      name = p.left.name;
      t = p.left.typeAnnotation?.typeAnnotation;
      def = p.right;
    } else if (p.type === 'RestElement') {
      return `params ${ty(p.typeAnnotation?.typeAnnotation, mod) ?? 'object[]'} ${id(p.argument.name)}`.replace(/List<(\w+)>/, '$1[]');
    } else if (p.type === 'Identifier') {
      name = p.name;
      t = p.typeAnnotation?.typeAnnotation;
    } else if (p.type === 'ObjectPattern') {
      const v = `__o${tmp++}`;
      for (const q of p.properties) defaults.push(`var ${id(q.value?.name ?? q.key.name)} = ${v}.${id(q.key.name)};`);
      return `${ty(p.typeAnnotation?.typeAnnotation, mod) ?? 'object'} ${v}`;
    } else return `/*TODO param pattern*/object _p${tmp++}`;
    hint = pascal(curFn) + pascal(name);
    let cs = t ? ty(t, mod, name) : def ? (def.type === 'Literal' && typeof def.value === 'number' ? (Number.isInteger(def.value) && INT_NAMES.has(name) ? 'int' : 'double') : def.type === 'Literal' && typeof def.value === 'string' ? 'string' : def.type === 'Literal' && typeof def.value === 'boolean' ? 'bool' : null) : null;
    if (!cs && def) {
      const dt = strip(exprType(def));
      if (dt && !dt.includes('TODO') && !/^object/.test(dt)) cs = dt === 'int' ? 'double' : dt;
    }
    if (!cs && def?.type === 'Identifier' && constTypes.has(imports.get(def.name) ?? `${modClass(mod)}.${def.name}`)) cs = 'double';
    if (!cs && def && ((def.type === 'Identifier' && /^[A-Z_0-9]+$/.test(def.name)) || def.type === 'BinaryExpression' || (def.type === 'MemberExpression' && /^[A-Z_0-9]+$/.test(def.property?.name ?? '')))) cs = 'double';
    if (!cs) cs = '/*TODO param type*/object';
    // A local function's numbers are doubles: C# can't give it a companion overload taking JS numbers.
    if (local) cs = cs.replace(/^int(?=\??$)/, 'double');
    const nm = pname(name);
    if (p.optional && !cs.endsWith('?')) cs += '?';
    if (def) {
      // A C# const (a module constant that's a literal) can be a default value too.
      const constName = def.type === 'Identifier' ? (imports.get(def.name) ?? `${modClass(mod)}.${def.name}`) : null;
      const isConstDefault = !!constName && constTypes.has(constName);
      if (isConstDefault && cs.includes('TODO')) cs = constTypes.get(constName);
      const literal = def.type === 'Literal' || (def.type === 'Identifier' && def.name === 'Infinity') || (def.type === 'UnaryExpression' && def.argument.type === 'Literal') || isConstDefault;
      if (literal) return `${cs} ${nm} = ${ex(def)}`;
      if (def.type === 'ObjectExpression' && /^JsRecord</.test(cs)) def.__asRecord = true;
      if (def.type === 'ArrayExpression' && /^List</.test(cs) && !def.elements.length) { defaults.push(`${nm} ??= new();`); return `${cs.endsWith('?') ? cs : cs + '?'} ${nm} = null`; }
      defaults.push(`${nm} ??= ${ex(def)};`);
      setType(name, cs.endsWith('?') ? cs : cs + '?', true); // (nullable in C#: a value after its default is set, NN where one is needed)
      return `${cs.endsWith('?') ? cs : cs + '?'} ${nm} = null`;
    }
    setType(name, cs, true);
    if (t) setTsType(name, t);
    if (p.optional) return `${cs} ${nm} = default`;
    return `${cs} ${nm}`;
  }

  function lambda(fn) {
    pushScope(namesIn([{ type: 'VariableDeclaration', declarations: fn.params.map((x) => ({ id: x })) }]));
    try {
      return lambda0(fn);
    } finally {
      popScope();
    }
  }

  function lambda0(fn) {
    const defaults = [];
    const typed = fn.params.every((p) => p.type === 'Identifier' && p.typeAnnotation);
    const unpack = [];
    const ps = fn.params.map((p) => {
      if (typed) return param(p, defaults, true);
      if (p.type === 'Identifier') {
        const k = fn.params.indexOf(p);
        setType(p.name, fn.__paramTypes?.[k] ?? null);
        return declName(p.name);
      }
      if (p.type === 'AssignmentPattern') return declName(p.left.name);
      const v = `__p${tmp++}`;
      if (p.type === 'ArrayPattern') unpack.push(destructure(p.elements, v));
      else if (p.type === 'ObjectPattern') for (const q of p.properties) unpack.push(`var ${id(q.value?.name ?? q.key.name)} = ${v}.${id(q.key.name)};`);
      return v;
    });
    while (fn.__arity && ps.length < fn.__arity) ps.push('_');
    const head = `(${ps.join(', ')})`;
    if (unpack.length) {
      const inner = fn.body.type === 'BlockStatement' ? block(fn.body, '            ', { fnRet: fn.__ret ?? null }).split('\n').slice(1, -1).join('\n') : `            return ${fn.__ret && fn.body.type === 'ArrayExpression' ? `(${fn.__ret})` : ''}${ex(fn.body, !!fn.__pred)};`;
      return `${head} =>\n        {\n${unpack.map((u) => '            ' + u).join('\n')}\n${inner}\n        }`;
    }
    // A predicate's body is a condition.
    if (fn.body.type !== 'BlockStatement' && fn.__ret && fn.body.type === 'ArrayExpression') return `${head} => (${fn.__ret})${ex(fn.body)}`;
    if (isTest && fn.body.type === 'ArrayExpression' && !fn.__ret && !literalType(fn.body)) markLoose(fn.body);
    if (fn.body.type === 'ObjectExpression' && fn.__ret && classFields.has(fn.__ret)) fn.body.__class = fn.__ret;
    if (fn.body.type !== 'BlockStatement') {
      const parent = fn.__pred;
      return `${head} => ${ex(fn.body, !!parent)}`;
    }
    return `${head} =>\n${block(fn.body, '            ', { fnRet: fn.returnType ? ty(fn.returnType.typeAnnotation, mod) : null })}`;
  }

  // Statements.
  function block(b, ind, ctx) {
    pushScope(namesIn(b.body));
    if (!b.__assigns) {
      const assigns = new Map();
      const walk = (n) => {
        if (!n || typeof n !== 'object') return;
        if (Array.isArray(n)) return n.forEach(walk);
        if (n.type === 'AssignmentExpression' && n.left.type === 'Identifier') {
          if (!assigns.has(n.left.name)) assigns.set(n.left.name, []);
          assigns.get(n.left.name).push(n);
        }
        for (const k in n) if (k !== 'type' && !k.startsWith('__') && typeof n[k] === 'object') walk(n[k]);
      };
      walk(b.body);
      b.__assigns = assigns;
    }
    scopes[scopes.length - 1].assigns = b.__assigns;
    const lines = [`${ind.slice(4)}{`];
    for (const s of b.body) lines.push(...stmt(s, ind, ctx));
    lines.push(`${ind.slice(4)}}`);
    popScope();
    return lines.join('\n');
  }

  function body(s, ind, ctx) {
    if (s.type === 'BlockStatement') return block(s, ind + '    ', ctx);
    return `${ind}{\n${stmt(s, ind + '    ', ctx).join('\n')}\n${ind}}`;
  }

  function declType(d, kind) {
    const name = d.id.name;
    hint = pascal(curFn) + pascal(name);
    if (d.id.typeAnnotation) return ty(d.id.typeAnnotation.typeAnnotation, mod, name);
    const init = d.init;
    if (!init) return '/*TODO untyped let*/object';
    if (init.type === 'Literal' && init.value === null) return '/*TODO null-initialised*/object';
    if (init.type === 'ArrayExpression' && init.elements.length === 0) return '/*TODO empty array*/List<object>';
    if (literalType(init)) return literalType(init);
    if (init.type === 'ArrayExpression' && init.elements.length && init.elements.every((x) => x?.type === 'ObjectExpression' && x.properties.every((q) => q.type === 'Property' && !q.computed))) {
      const keyset = (o) => o.properties.map((q) => String(q.key.name ?? q.key.value)).sort().join(',');
      if (init.elements.every((x) => keyset(x) === keyset(init.elements[0]))) {
        const cls = keyset(init.elements[0]) === 'x,y' ? 'Point' : objectLiteralClass(init.elements[0], pascal(curFn) + pascal(name) + 'Item');
        for (const x of init.elements) x.__class = cls;
        return `List<${cls}>`;
      }
    }
    if (init.type === 'ArrayExpression' && init.elements.length && init.elements.every((x) => x?.type === 'SpreadElement')) {
      const ts = init.elements.map((x) => strip(exprType(x.argument)));
      if (ts.every((x) => x && x === ts[0] && /^List</.test(x))) return ts[0];
    }
    if (init.type === 'ConditionalExpression' && literalType(init.consequent) && literalType(init.consequent) === literalType(init.alternate)) return literalType(init.consequent);
    let lit = init;
    while (lit.type === 'TSAsExpression') lit = lit.expression;
    const constNum = (v) => (v.type === 'Literal' && typeof v.value === 'number') || (v.type === 'UnaryExpression' && v.operator === '-' && v.argument.type === 'Literal') || (v.type === 'MemberExpression' && !v.computed && /^[A-Z][A-Z_0-9]+$/.test(v.property.name)) || (v.type === 'Identifier' && /^[A-Z][A-Z_0-9]+$/.test(v.name));
    if (lit.type === 'ObjectExpression' && lit.properties.length > 1 && lit.properties.every((q) => q.type === 'Property' && !q.computed)) {
      const vts = lit.properties.map((q) => strip(exprType(q.value)));
      if (vts.every((x) => x && x === vts[0] && ['bool', 'string'].includes(x))) {
        recordConsts.add(name);
        d.__record = true;
        return `JsRecord<${vts[0]}>`;
      }
    }
    for (const [k, test] of [['double', constNum], ['string', function strish(v) { return (v.type === 'Literal' && typeof v.value === 'string') || v.type === 'TemplateLiteral' || (v.type === 'ConditionalExpression' && strish(v.consequent) && strish(v.alternate)); }], ['bool', (v) => v.type === 'Literal' && typeof v.value === 'boolean']]) {
      if (lit.type === 'ObjectExpression' && lit.properties.length && lit.properties.every((p) => p.type === 'Property' && !p.computed && test(p.value))) {
        recordConsts.add(name);
        d.__record = true;
        return `JsRecord<${k}>`;
      }
    }
    if (init.type === 'ObjectExpression' && init.properties.length && init.properties.every((p) => p.type === 'Property' && !p.computed)) {
      const keys = init.properties.map((p) => String(p.key.name ?? p.key.value));
      if (keys.slice().sort().join(',') === 'x,y') return 'Point';
      return objectLiteralClass(init, pascal(curFn) + pascal(name));
    }
    const plain = (o) => o?.type === 'ObjectExpression' && o.properties.length && o.properties.every((p) => p.type === 'Property' && !p.computed);
    if (init.type === 'ConditionalExpression' && plain(init.consequent) && plain(init.alternate)) {
      const props = [...init.consequent.properties, ...init.alternate.properties].filter((p, i, arr) => arr.findIndex((q) => (q.key.name ?? q.key.value) === (p.key.name ?? p.key.value)) === i);
      if (props.map((p) => String(p.key.name ?? p.key.value)).sort().join(',') === 'x,y') return 'Point';
      return objectLiteralClass({ ...init.consequent, properties: props }, pascal(curFn) + pascal(name));
    }
    if (init.type === 'Literal' && typeof init.value === 'number' && kind === 'let' && (!Number.isInteger(init.value) || !INT_NAMES.has(name))) return 'double';
    // A let that starts as a whole number from a field (x, w...) and isn't named like an index: a JS number, which may not stay whole.
    const intish = (x) => (x.type === 'Identifier' && exprType(x) === 'int') || (x.type === 'Literal' && Number.isInteger(x.value)) || (x.type === 'MemberExpression' && !x.computed && INT_FIELDS.has(x.property.name)) || (x.type === 'ConditionalExpression' && intish(x.consequent) && intish(x.alternate)) || (x.type === 'BinaryExpression' && ['+', '-', '*'].includes(x.operator) && intish(x.left) && intish(x.right));
    if (kind === 'let' && !INT_NAMES.has(name) && intish(init)) return 'double';
    if (kind === 'let' && !INT_NAMES.has(name) && ['int', 'double'].includes(exprType(init) ?? '')) return 'double';
    // A whole-number let (a counter, say) later given something that may not be whole: a JS number.
    if (kind === 'let' && (exprType(init) === 'int' || (init.type === 'Literal' && Number.isInteger(init.value)) || (init.type === 'CallExpression' && init.callee.type === 'MemberExpression' && init.callee.object.name === 'Math'))) {
      const later = scopes[scopes.length - 1]?.assigns?.get(name) ?? [];
      const whole = (x) => x.type === 'Literal' ? Number.isInteger(x.value) : exprType(x) === 'int' || (x.type === 'BinaryExpression' && ['+', '-', '*', '%'].includes(x.operator) && whole(x.left) && whole(x.right)) || (x.type === 'Identifier' && x.name === name);
      if (later.some((a) => !['+=', '-=', '='].includes(a.operator) ? true : !whole(a.right))) return 'double';
    }
    return 'var';
  }

  /** Whether a node mentions an identifier (harness mode: V8 profiling through node:inspector has no C# equivalent). */
  const mentions = (n, name) => {
    if (!n || typeof n !== 'object') return false;
    if (Array.isArray(n)) return n.some((x) => mentions(x, name));
    if (n.type === 'Identifier' && n.name === name) return true;
    for (const k in n) if (k !== 'type' && !k.startsWith('__') && typeof n[k] === 'object' && mentions(n[k], name)) return true;
    return false;
  };
  function stmt(s, ind, ctx) {
    // Only the profiling statements themselves: `const session = ... new Session()`, `if (session) {...}`, `session.x()`.
    const profiling = (s.type === 'VariableDeclaration' && mentions(s, 'Session')) || (s.type === 'IfStatement' && mentions(s.test, 'session')) || (s.type === 'ExpressionStatement' && mentions(s, 'session'));
    if (mod.startsWith('harness:') && profiling) return [`${ind}// (left out: V8 profiling, node:inspector)`];
    switch (s.type) {
      case 'ExpressionStatement': {
        const x = s.expression;
        if (x.type === 'UnaryExpression' && x.operator === 'void') return [];
        // a, b = c... sequence statements, and calls like list.forEach(...)
        return [`${ind}${ex(x)};`];
      }
      case 'VariableDeclaration': {
        const lines = [];
        for (const d of s.declarations) {
          if (d.id.type === 'ObjectPattern' && d.init?.type === 'AwaitExpression' && d.init.argument.type === 'ImportExpression') {
            const from = moduleOf(d.init.argument.source.value);
            for (const q of d.id.properties) if (from && q.type === 'Property') imports.set(q.value.name, `${modClass(home(from, q.key.name))}.${q.key.name}`);
            continue;
          }
          if (d.id.type === 'ObjectPattern') {
            const t = `__d${tmp++}`;
            lines.push(`${ind}var ${t} = ${ex(d.init)};`);
            for (const p of d.id.properties) {
              if (p.type === 'RestElement') { lines.push(`${ind}/*TODO rest*/`); continue; }
              const key = p.key.name;
              if (p.value.type === 'AssignmentPattern') lines.push(`${ind}var ${declName(p.value.left.name)} = ${t}.${id(key)} ?? ${ex(p.value.right)};`);
              else lines.push(`${ind}var ${declName(p.value.name)} = ${t}.${id(key)};`);
              if (p.value.type === 'Identifier') setType(p.value.name, classFields.get(strip(exprType(d.init)) ?? '')?.get(key) ?? null);
            }
            continue;
          }
          if (d.id.type === 'ArrayPattern') {
            lines.push(ind + destructure(d.id.elements, ex(d.init), d.init));
            continue;
          }
          const t = declType(d, s.kind);
          if (d.init?.type === 'ArrowFunctionExpression' || d.init?.type === 'FunctionExpression') {
            const fn = d.init;
            const typed = fn.params.every((p) => (p.type === 'Identifier' || p.type === 'AssignmentPattern') && (p.typeAnnotation || p.left?.typeAnnotation));
            if (typed) {
              const defaults = [];
              const ps = fn.params.map((p) => param(p, defaults, true));
              paramTypes.set(d.id.name, ps.map(typeOfParam));
              fnParams.set(`${modClass(mod)}.${d.id.name}`, parseParams(ps));
              let ret = fn.returnType ? ty(fn.returnType.typeAnnotation, mod) : guessReturn(fn);
              if (ret.includes('TODO') && fn.body.type === 'BlockStatement') {
                const saved = tmp;
                const outer = collecting;
                collecting = new Map();
                block(fn.body, ind + '    ', { fnRet: null });
                tmp = saved;
                ret = guessReturn(fn);
                collecting = outer;
              }
              if (!ret.includes('TODO') && !/\b[A-Z]\b/.test(ret)) fnRet.set(d.id.name, ret);
              const bodyText = fn.body.type === 'BlockStatement' ? block(fn.body, ind + '    ', { fnRet: ret }) : `${ind}{\n${ind}    ${ret === 'void' ? '' : 'return '}${ex(fn.body, ret === 'bool')};\n${ind}}`;
              lines.push(`${ind}${ret} ${declName(d.id.name)}(${ps.join(', ')})`);
              lines.push(bodyText.split('\n').map((l, i) => (i === 0 && fn.body.type === 'BlockStatement' ? ind + l.trimStart() : l)).join('\n'));
              continue;
            }
          }
          let lit = d.init;
          while (lit?.type === 'TSAsExpression') lit = lit.expression;
          if (lit?.type === 'ObjectExpression' && classFields.has(strip(t))) lit.__class = strip(t);
          const itemType = t.match(/^List<(.+)>$/)?.[1];
          if (itemType && lit?.type === 'CallExpression' && lit.callee.type === 'MemberExpression' && ['map', 'from', 'flatMap'].includes(lit.callee.property?.name)) lit.__targ = itemType;
          const init = d.__record || (t.startsWith('JsRecord') && lit?.type === 'ObjectExpression')
            ? ` = new() { ${lit.properties.map((p) => (p.type === 'Property' ? `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}` : '[Js.TODO_SPREAD] = default!')).join(', ')} }`
            : d.init ? ` = ${ex(d.init, t === 'bool')}` : t.endsWith('?') ? ' = null' : ' = default!';
          lines.push(`${ind}${t} ${declName(d.id.name)}${init};`);
          if (d.id.typeAnnotation) setTsType(d.id.name, d.id.typeAnnotation.typeAnnotation);
          if (s.kind === 'const' && d.init) initOf.set(d.id.name, d.init);
          setType(d.id.name, t !== 'var' ? t : exprType(d.init));
        }
        return lines;
      }
      case 'ReturnStatement':
        if (s.argument?.type === 'ArrayExpression' && /^List</.test(ctx.fnRet ?? '')) return [`${ind}return (${ctx.fnRet})${ex(s.argument)};`];
        if (s.argument?.type === 'CallExpression' && s.argument.callee.type === 'MemberExpression' && s.argument.callee.property?.name === 'map' && /^List<\w+>\??$/.test(ctx.fnRet ?? '')) s.argument.__targ = ctx.fnRet.match(/^List<(\w+)>/)[1];
        if (s.argument?.type === 'ObjectExpression' && classFields.has(strip(ctx.fnRet ?? '') ?? '')) s.argument.__class = strip(ctx.fnRet);
        if (s.argument?.type === 'ObjectExpression' && s.argument.properties.some((q) => q.type === 'SpreadElement') && classFields.has(strip(ctx.fnRet ?? '') ?? '')) spreadAs = strip(ctx.fnRet);
        curRet = ctx.fnRet ?? '';
        if (s.argument?.type === 'ConditionalExpression' && ctx.fnRet === 'void') return [`${ind}{ if (${ex(s.argument.test, true)}) { ${ex(s.argument.consequent)}; } else { ${ex(s.argument.alternate)}; } return; }`];
        if (s.argument && ctx.fnRet === 'void') return [`${ind}{ ${ex(s.argument)}; return; }`];
        return [`${ind}return${s.argument ? ' ' + ex(s.argument, ctx.fnRet === 'bool') : ''};`];
      case 'IfStatement': {
        const lines = [`${ind}if (${ex(s.test, true)})`, body(s.consequent, ind, ctx)];
        if (s.alternate) {
          if (s.alternate.type === 'IfStatement') {
            const rest = stmt(s.alternate, ind, ctx);
            rest[0] = `${ind}else ${rest[0].trimStart()}`;
            lines.push(...rest);
          } else lines.push(`${ind}else`, body(s.alternate, ind, ctx));
        }
        return lines;
      }
      case 'BlockStatement': return [block(s, ind + '    ', ctx)];
      case 'ForStatement': {
        pushScope(s.init?.type === 'VariableDeclaration' ? namesIn([s.init]) : []);
        let init = '';
        if (s.init?.type === 'VariableDeclaration') {
          init = s.init.declarations.map((d, i) => {
            const t = d.id.typeAnnotation ? ty(d.id.typeAnnotation.typeAnnotation, mod, d.id.name) : 'var';
            const v = ex(d.init);
            return `${i === 0 ? t + ' ' : ''}${declName(d.id.name)} = ${v}`;
          }).join(', ');
        } else if (s.init) init = ex(s.init);
        const update = s.update ? (s.update.type === 'SequenceExpression' ? s.update.expressions.map((x) => ex(x)).join(', ') : ex(s.update)) : '';
        const out = [`${ind}for (${init}; ${s.test ? ex(s.test, true) : ''}; ${update})`, body(s.body, ind, ctx)];
        popScope();
        return out;
      }
      case 'ForOfStatement': {
        const left = s.left.type === 'VariableDeclaration' ? s.left.declarations[0].id : s.left;
        const lt = literalType(s.right);
        const right = s.right.type === 'TSAsExpression' ? s.right.expression : s.right;
        if (left.type === 'ArrayPattern' && right.type === 'ArrayExpression' && right.elements.length && right.elements.every((x) => x?.type === 'ArrayExpression' && x.elements.length === right.elements[0].elements.length)) {
          const colType = (k) => {
            const ts = right.elements.map((x) => { const y = x.elements[k]; return y?.type === 'Literal' && typeof y.value === 'number' ? 'double' : strip(exprType(y)); });
            return ts.every((x) => x && x === ts[0]) ? (ts[0] === 'int' ? 'double' : ts[0]) : null;
          };
          const cols = right.elements[0].elements.map((_, k) => colType(k));
          const tupleType = cols.every(Boolean) ? `(${cols.join(', ')})` : '';
          const items = right.elements.map((x) => `(${x.elements.map((y) => ex(y)).join(', ')})`).join(', ');
          pushScope(s.left.type === 'VariableDeclaration' ? namesIn([s.left]) : []);
          const lines = [`${ind}foreach (var (${left.elements.map((x) => (x ? declName(x.name) : '_')).join(', ')}) in new ${tupleType}[] { ${items} })`];
          lines.push(body(s.body, ind, ctx));
          popScope();
          return lines;
        }
        const iter = ex(s.right);
        pushScope(s.left.type === 'VariableDeclaration' ? namesIn([s.left]) : []);
        const v = left.type === 'ArrayPattern' ? `var (${left.elements.map((x) => (x ? declName(x.name) : '_')).join(', ')})` : left.type === 'ObjectPattern' ? `var __o${tmp}` : `var ${declName(left.name)}`;
        if (left.type === 'Identifier') { const lt2 = strip(exprType(s.right)); const it = lt2?.match(/^(?:List|IReadOnlyList|JsSet)<(.+)>$/)?.[1] ?? (lt2 === 'string' ? 'string' : null); if (it) setType(left.name, it); }
        const lines = [`${ind}foreach (${v} in Js.Iter(${lt ? `(${lt})` : ''}${iter}))`];
        if (left.type === 'ObjectPattern') {
          const t = `__o${tmp++}`;
          const inner = left.properties.map((p) => `${ind}    var ${declName(p.value.name ?? p.key.name)} = ${t}.${id(p.key.name)};`);
          const b = body(s.body, ind, ctx).split('\n');
          b.splice(1, 0, ...inner);
          lines.push(b.join('\n'));
          popScope();
          return lines;
        }
        lines.push(body(s.body, ind, ctx));
        popScope();
        return lines;
      }
      case 'ForInStatement': {
        const left = s.left.type === 'VariableDeclaration' ? s.left.declarations[0].id.name : s.left.name;
        return [`${ind}foreach (var ${id(left)} in Js.ObjectKeys(${ex(s.right)}))`, body(s.body, ind, ctx)];
      }
      case 'WhileStatement': return [`${ind}while (${ex(s.test, true)})`, body(s.body, ind, ctx)];
      case 'DoWhileStatement': return [`${ind}do`, body(s.body, ind, ctx), `${ind}while (${ex(s.test, true)});`];
      case 'BreakStatement': return [`${ind}break;`];
      case 'ContinueStatement': return [`${ind}continue;`];
      case 'ThrowStatement': return [`${ind}throw ${ex(s.argument)};`];
      case 'EmptyStatement': return [];
      case 'SwitchStatement': {
        const constant = (c) => !c.test || c.test.type === 'Literal' || (c.test.type === 'UnaryExpression' && c.test.argument.type === 'Literal');
        if (!s.cases.every(constant)) {
          const v = `__sw${tmp++}`;
          const lines = [`${ind}var ${v} = ${ex(s.discriminant)};`];
          let pending = [];
          let first = true;
          for (const c of s.cases) {
            if (c.test) pending.push(`${v} == ${operand(c.test)}`);
            if (c.consequent.length === 0) continue;
            const body_ = c.consequent.filter((x) => x.type !== 'BreakStatement').flatMap((x) => stmt(x, ind + '    ', ctx));
            const cond = c.test ? pending.join(' || ') : null;
            lines.push(cond ? `${ind}${first ? '' : 'else '}if (${cond})` : `${ind}else`, `${ind}{`, ...body_, `${ind}}`);
            first = false;
            pending = [];
          }
          return lines;
        }
        const lines = [`${ind}switch (${ex(s.discriminant)})`, `${ind}{`];
        for (const c of s.cases) {
          lines.push(`${ind}    ${c.test ? `case ${ex(c.test)}:` : 'default:'}`);
          if (c.consequent.length === 0) continue;
          const inner = c.consequent.flatMap((x) => stmt(x, ind + '        ', ctx));
          lines.push(`${ind}        {`, ...inner.map((l) => '    ' + l), `${ind}        }`);
          const last = c.consequent[c.consequent.length - 1];
          if (!['BreakStatement', 'ReturnStatement', 'ThrowStatement', 'ContinueStatement'].includes(last.type) && !(last.type === 'BlockStatement' && ['BreakStatement', 'ReturnStatement', 'ThrowStatement', 'ContinueStatement'].includes(last.body.at(-1)?.type))) lines.push(`${ind}        break;`);
        }
        lines.push(`${ind}}`);
        return lines;
      }
      case 'TryStatement': {
        const lines = [`${ind}try`, block(s.block, ind + '    ', ctx)];
        if (s.handler) lines.push(`${ind}catch${s.handler.param ? ` (Exception ${id(s.handler.param.name)})` : ''}`, block(s.handler.body, ind + '    ', ctx));
        if (s.finalizer) lines.push(`${ind}finally`, block(s.finalizer, ind + '    ', ctx));
        return lines;
      }
      case 'FunctionDeclaration': return fnDecl(s, ind, false);
      case 'LabeledStatement': return [`${ind}/*TODO label ${s.label.name}*/`, ...stmt(s.body, ind, ctx)];
      case 'TSTypeAliasDeclaration': case 'TSInterfaceDeclaration': return [];
      default: return [`${ind}/*TODO statement ${s.type}*/`];
    }
  }

  /** A return type from what a function returns, when it isn't written. */
  function guessReturn(fn) {
    if (fn.body.type !== 'BlockStatement') {
      const b = fn.body;
      if (b.type === 'CallExpression' && b.callee.type === 'MemberExpression' && isExpect(b.callee.object)) return 'void';
      if (isBoolish(b)) return 'bool';
      if (b.type === 'TemplateLiteral' || (b.type === 'Literal' && typeof b.value === 'string')) return 'string';
      return fromReturns([b], fn) ?? '/*TODO return type*/object';
    }
    const rets = [];
    const walk = (n) => {
      if (!n || typeof n !== 'object') return;
      if (Array.isArray(n)) return n.forEach(walk);
      if (n.type === 'FunctionDeclaration' || n.type === 'ArrowFunctionExpression' || n.type === 'FunctionExpression') return;
      if (n.type === 'ReturnStatement') rets.push(n.argument);
      for (const k in n) if (k !== 'type' && typeof n[k] === 'object') walk(n[k]);
    };
    walk(fn.body.body);
    if (rets.every((r) => !r)) return 'void';
    if (rets.every((r) => r?.type === 'CallExpression' && r.callee.type === 'MemberExpression' && isExpect(r.callee.object))) return 'void';
    if (rets.every((r) => r && isBoolish(r))) return 'bool';
    if (rets.every((r) => r && (r.type === 'TemplateLiteral' || (r.type === 'Literal' && typeof r.value === 'string')))) return 'string';
    return fromReturns(rets, fn) ?? '/*TODO return type*/object';
  }

  /** A return type from what's returned: numbers (arithmetic, Math, literals), an object literal's class, a literal list. */
  function fromReturns(rets, fn) {
    const isNull = (r) => r.type === 'Literal' && r.value === null || (r.type === 'Identifier' && r.name === 'undefined');
    const numeric = (r) => (r.type === 'Identifier' && r.name === 'Infinity') || (r.type === 'Literal' && typeof r.value === 'number') || (r.type === 'BinaryExpression' && ['+', '-', '*', '/', '%'].includes(r.operator) && !(r.left.type === 'Literal' && typeof r.left.value === 'string') && !(r.right.type === 'TemplateLiteral') && !(r.left.type === 'TemplateLiteral')) || (r.type === 'CallExpression' && r.callee.type === 'MemberExpression' && r.callee.object.name === 'Math') || (r.type === 'UnaryExpression' && r.operator === '-') || r.type === 'UpdateExpression';
    const sides = (r) => (r && r.type === 'ConditionalExpression' ? [...sides(r.consequent), ...sides(r.alternate)] : [r]);
    rets = rets.flatMap(sides);
    const real = rets.filter((r) => r && !isNull(r));
    // A call to a function whose return type is known.
    const callType = (r) => {
      if (r.type !== 'CallExpression') return null;
      if (r.callee.type === 'Identifier') {
        const q = imports.get(r.callee.name);
        return fnRet.get(q ?? `${modClass(mod)}.${r.callee.name}`) ?? fnRet.get(r.callee.name) ?? null;
      }
      if (r.callee.type === 'MemberExpression' && r.callee.object.name === 'Math') return 'double';
      return null;
    };
    const nul = rets.some((r) => !r || isNull(r));
    let ets = real.map(exprType);
    if (ets.length > 1 && ets.every((x) => x && ['int', 'double'].includes(strip(x))) && ets.some((x) => strip(x) === 'double')) ets = ets.map((x) => x.replace(/^int/, 'double'));
    if (real.length && ets.every((x) => x && strip(x) === strip(ets[0]))) return strip(ets[0]) + (nul || ets.some((x) => x.endsWith('?')) ? '?' : '');
    const cts = real.map(callType);
    if (real.length && cts.every((c) => c && c === cts[0] && !c.includes('TODO'))) return cts[0].replace(/\?$/, '') + (nul || cts[0].endsWith('?') ? '?' : '');
    if (real.length && real.every(numeric)) return 'double' + (nul ? '?' : '');
    const objs = real.filter((r) => r.type === 'ObjectExpression' && r.properties.every((p) => p.type === 'Property' && !p.computed));
    if (real.length && objs.length === real.length) {
      const keys = objs[0].properties.map((p) => String(p.key.name ?? p.key.value)).sort().join(',');
      if (keys === 'x,y') return 'Point' + (nul ? '?' : '');
      // Fields whose types aren't known yet (locals of the body): wait for the body's dry run.
      const knownField = (q) => q.value.type === 'Literal' || q.value.type === 'TemplateLiteral' || exprType(q.shorthand ? q.key : q.value);
      if (!collecting && !objs.every((o) => o.properties.every(knownField))) return null;
      const all = { ...objs[0], properties: objs.flatMap((o) => o.properties).filter((p, i, arr) => arr.findIndex((q) => (q.key.name ?? q.key.value) === (p.key.name ?? p.key.value)) === i) };
      return objectLiteralClass(all, pascal(curFn || fn.id?.name || 'Fn') + 'Result') + (nul ? '?' : '');
    }
    // [a, b] as const of numbers: a tuple.
    const asConst = (r) => r.type === 'TSAsExpression' && r.typeAnnotation.type === 'TSTypeReference' && r.typeAnnotation.typeName.name === 'const' && r.expression.type === 'ArrayExpression';
    if (real.length && real.every(asConst) && real.every((r) => r.expression.elements.length === real[0].expression.elements.length && r.expression.elements.every((x) => x && numeric(x)))) return `(${real[0].expression.elements.map(() => 'double').join(', ')})`;
    if (real.length === 1 && literalType(real[0])) return literalType(real[0]);
    if (real.length && real.every((r) => r.type === 'ArrowFunctionExpression' && r.params.every((q) => q.type === 'Identifier' && q.typeAnnotation))) {
      const r0 = real[0];
      const ps = r0.params.map((q) => (ty(q.typeAnnotation.typeAnnotation, mod) ?? 'object').replace(/^int$/, 'double'));
      const rt = r0.returnType ? ty(r0.returnType.typeAnnotation, mod) : r0.body.type !== 'BlockStatement' ? (isBoolish(r0.body) ? 'bool' : r0.body.type === 'Literal' && typeof r0.body.value === 'number' ? 'double' : strip(exprType(r0.body))) : null;
      if (rt && rt !== 'void' && ps.every((x) => !x.includes('TODO'))) return `Func<${[...ps, rt === 'int' ? 'double' : rt].join(', ')}>`;
      if (rt === 'void') return ps.length ? `Action<${ps.join(', ')}>` : 'Action';
    }
    // A list literal whose items show their type: points (x.pos, {x, y}) or the items of a spread list.
    if (real.length && real.every((r) => r.type === 'ArrayExpression' && r.elements.length)) {
      const item = (x) => {
        if (!x) return null;
        if (x.type === 'SpreadElement') {
          const c = callType(x.argument);
          return c?.match(/^(?:List|IReadOnlyList)<(\w+)>\??$/)?.[1] ?? null;
        }
        if (x.type === 'MemberExpression' && !x.computed && ['pos', 'at', 'was'].includes(x.property.name)) return 'Point';
        if (x.type === 'ObjectExpression' && x.properties.map((q) => q.key?.name).sort().join(',') === 'x,y') return 'Point';
        return null;
      };
      const items = real.flatMap((r) => r.elements.map(item));
      if (items.every((x) => x && x === items[0])) return `List<${items[0]}>` + (nul ? '?' : '');
    }
    return null;
  }

  function fnDecl(f, ind, top) {
    const name = f.id.name;
    const outerFn = curFn;
    curFn = name;
    const defaults = [];
    const tparams = f.typeParameters ? `<${f.typeParameters.params.map((p) => p.name.name ?? p.name).join(', ')}>` : '';
    const where = (f.typeParameters?.params ?? []).filter((p) => p.constraint).map((p) => {
      hint = pascal(name) + 'Constraint';
      const c = ty(p.constraint, mod);
      return c && !c.includes('TODO') && c !== 'object' ? ` where ${p.name.name ?? p.name} : class, ${c}` : ` where ${p.name.name ?? p.name} : class`;
    }).join('');
    pushScope(f.params.map((p) => (p.type === 'AssignmentPattern' ? p.left.name : p.name)).filter(Boolean));
    const ps = f.params.map((p) => param(p, defaults, !top));
    paramTypes.set(name, ps.map(typeOfParam));
    if (top) fnParams.set(`${modClass(mod)}.${name}`, parseParams(ps));
    hint = pascal(name) + 'Result';
    let ret = f.returnType ? ty(f.returnType.typeAnnotation, mod) : guessReturn(f);
    if (ret.includes('TODO')) {
      // Not told by what's returned at a glance: translate the body once to learn its locals' types, and look again.
      const saved = tmp;
      collecting = new Map();
      block(f.body, ind + '    ', { fnRet: null });
      tmp = saved;
      ret = guessReturn(f);
      collecting = null;
    }
    // (Not a generic function's T: it says nothing at a call.)
    if (ret && !ret.includes('TODO') && !/\b[A-Z]\b/.test(ret)) {
      fnRet.set(top ? `${modClass(mod)}.${name}` : name, ret);
      if (!top) fnRet.set(name, ret);
    }
    const head = `${ind}${top ? 'public static ' : ''}${ret} ${id(name)}${tparams}(${ps.join(', ')})${where}`;
    const b = block(f.body, ind + '    ', { fnRet: ret }).split('\n');
    if (ret !== 'void' && f.body.body.at(-1)?.type === 'SwitchStatement' && !f.body.body.at(-1).cases.some((c) => !c.test)) b.splice(b.length - 1, 0, `${ind}    throw new InvalidOperationException("unreachable: every case returns");`);
    popScope();
    b[0] = ind + b[0].trimStart();
    if (defaults.length) b.splice(1, 0, ...defaults.map((d) => `${ind}    ${d}`));
    b[b.length - 1] = ind + b[b.length - 1].trimStart();
    curFn = outerFn;
    // Whole-number parameters: a companion taking JavaScript numbers (values from Math.floor and the like).
    const intParams = ps.map((x) => /^int\??\s/.test(x));
    if (top && intParams.some(Boolean) && !tparams) {
      // (Its first JS-number parameter is required, so a call leaving the numbers out isn't ambiguous.)
      const firstNum = intParams.indexOf(true);
      const dps = ps.map((x, i) => (intParams[i] ? x.replace(/^int/, 'double') : x)).map((x, i) => (i === firstNum ? x.replace(/ = .*$/, '') : x));
      // (The parameters' C# names, as declared: a scope may have renamed one.)
      const names = ps.map((x) => x.replace(/ = .*$/, '').trim().split(/\s+/).pop());
      // Js.I: one place to check that a JS number reaching an int parameter is whole (a fraction would truncate).
      const call = names.map((n, i) => (intParams[i] ? `Js.I(${n})` : n)).join(', ');
      return [head, ...b, `${ind}public static ${ret} ${id(name)}(${dps.join(', ')})${where} => ${id(name)}(${call});`];
    }
    return [head, ...b];
  }

  const isHarness = mod.startsWith('harness:');
  const isTest = mod.startsWith('test:') || isHarness;
  let factCount = 0;
  const skipped = [];
  /** Names a node uses that weren't translated (the UI). */
  const usesUnavailable = (n) => {
    let found = null;
    const walk = (x) => {
      if (found || !x || typeof x !== 'object') return;
      if (Array.isArray(x)) return x.forEach(walk);
      if (x.type === 'Identifier' && unavailable.has(x.name)) found = x.name;
      if (x.type === 'MetaProperty') found = 'import.meta (Vite)';
      for (const k in x) if (k !== 'type' && !k.startsWith('__') && typeof x[k] === 'object') walk(x[k]);
    };
    walk(n);
    return found;
  };
  const factName = (s) => 'T' + String(++factCount).padStart(3, '0') + '_' + s.replace(/[^A-Za-z0-9]+/g, '_').replace(/^_|_$/g, '').slice(0, 60);
  /** A describe or it call's name and function, or null. */
  const testCall = (n) => {
    const x = n.type === 'ExpressionStatement' ? n.expression : n;
    if (x?.type !== 'CallExpression' || x.callee.type !== 'Identifier' || !['describe', 'it', 'test', 'afterEach'].includes(x.callee.name)) return null;
    const fn = x.arguments.find((a) => a.type === 'ArrowFunctionExpression' || a.type === 'FunctionExpression');
    const nameNode = x.arguments[0];
    const name = nameNode?.type === 'Literal' ? String(nameNode.value) : nameNode?.type === 'TemplateLiteral' ? nameNode.quasis.map((q) => q.value.cooked).join('...') : '';
    return { kind: x.callee.name === 'test' ? 'it' : x.callee.name, name, fn };
  };
  /** The C# for a describe's (or the file's) tests and helpers: facts, nested classes, members. */
  function testBody(nodes, path, ind, afterEach) {
    const out_ = [];
    const after = [...afterEach];
    for (const n of nodes) {
      const tc = testCall(n);
      if (tc?.kind === 'afterEach') after.push(tc.fn);
    }
    for (const n of nodes) {
      const tc = testCall(n);
      // for (const seed of [1, 2, 3]) it(...): a theory, one case per value.
      if (!tc && n.type === 'ForOfStatement' && n.left.type === 'VariableDeclaration' && n.left.declarations[0].id.type === 'Identifier' && n.right.type === 'ArrayExpression' && n.right.elements.every((x) => x?.type === 'Literal')) {
        const inner = n.body.type === 'BlockStatement' ? n.body.body : [n.body];
        const itc = inner.length === 1 ? testCall(inner[0]) : null;
        if (itc?.kind === 'it') {
          const v = n.left.declarations[0].id.name;
          const vt = typeof n.right.elements[0].value === 'string' ? 'string' : 'double';
          const label = [...path, itc.name].join(' > ');
          const fbody = itc.fn.body.type === 'BlockStatement' ? itc.fn.body : { type: 'BlockStatement', body: [{ type: 'ExpressionStatement', expression: itc.fn.body }] };
          pushScope([v]);
          setType(v, vt, true);
          curFn = 'test';
          const b = block(fbody, ind + '    ', { fnRet: 'void' }).split('\n');
          popScope();
          b[0] = ind + b[0].trimStart();
          b[b.length - 1] = ind + b[b.length - 1].trimStart();
          out_.push('', `${ind}[Theory(DisplayName = ${str(label)})]`, ...n.right.elements.map((x) => `${ind}[InlineData(${ex(x)})]`), `${ind}public void ${factName(itc.name)}(${vt} ${id(v)})`, ...b);
          continue;
        }
      }
      if (!tc) {
        out_.push(...declare([n], ind));
        continue;
      }
      if (tc.kind === 'afterEach') continue;
      const label = [...path, tc.name].join(' > ');
      if (tc.kind === 'describe') {
        const cls = 'D' + String(++factCount).padStart(3, '0') + '_' + tc.name.replace(/[^A-Za-z0-9]+/g, '_').replace(/^_|_$/g, '').slice(0, 50);
        const inner = testBody(tc.fn.body.body, [...path, tc.name], ind + '    ', after);
        out_.push('', `${ind}/// <summary>${label.replace(/&/g, '&amp;').replace(/</g, '&lt;')}</summary>`, `${ind}public class ${cls}${after.length ? ' : IDisposable' : ''}`, `${ind}{`, ...inner, `${ind}}`);
        continue;
      }
      const missing = usesUnavailable(tc.fn);
      if (missing) {
        skipped.push(`${label} (uses ${missing})`);
        out_.push('', `${ind}// Left out: "${label}" uses ${missing}, from the browser UI.`);
        continue;
      }
      const fbody = tc.fn.body.type === 'BlockStatement' ? tc.fn.body : { type: 'BlockStatement', body: [{ type: 'ExpressionStatement', expression: tc.fn.body }] };
      curFn = 'test';
      const b = block(fbody, ind + '    ', { fnRet: 'void' }).split('\n');
      b[0] = ind + b[0].trimStart();
      b[b.length - 1] = ind + b[b.length - 1].trimStart();
      out_.push('', `${ind}[Fact(DisplayName = ${str(label)})]`, `${ind}public void ${factName(tc.name)}()`, ...b);
    }
    if (after.length) {
      const lines_ = after.flatMap((fn) => (fn.body.type === 'BlockStatement' ? fn.body.body : [{ type: 'ExpressionStatement', expression: fn.body }]).flatMap((s) => stmt(s, ind + '        ', { fnRet: 'void' })));
      out_.push('', `${ind}public void Dispose()`, `${ind}{`, ...lines_, `${ind}}`);
    }
    return out_;
  }

  // Module-level declarations.
  const members = [];
  if (isTest) {
    members.push(...testBody(ast.body.filter((n) => n.type !== 'ImportDeclaration'), [], '    ', []));
  }
  function declare(nodes, ind) {
    const saved = members.length;
    declareInto(nodes);
    return members.splice(saved).map((l) => (l.startsWith('    ') ? ind + l.slice(4) : l));
  }
  function declareInto(nodes) {
  for (const n of nodes) {
    const d = n.type === 'ExportNamedDeclaration' ? n.declaration : n.type === 'ExportDefaultDeclaration' ? n.declaration : n;
    if (!d || n.type === 'ImportDeclaration') continue;
    if (d.type === 'FunctionDeclaration') {
      moduleNames.add(d.id.name);
      members.push('', ...docFor(n, '    '), ...fnDecl(d, '    ', true));
      continue;
    }
    if (d.type === 'VariableDeclaration') {
      for (const v of d.declarations) {
        if (v.id.type !== 'Identifier') { members.push(`    /*TODO module-level pattern*/`); continue; }
        const name = v.id.name;
        moduleNames.add(name);
        const init = v.init;
        // A const arrow function: a method.
        if ((init?.type === 'ArrowFunctionExpression' || init?.type === 'FunctionExpression') && d.kind === 'const') {
          members.push('', ...docFor(n, '    '), ...fnDecl({ id: v.id, params: init.params, returnType: init.returnType, typeParameters: init.typeParameters, body: init.body.type === 'BlockStatement' ? init.body : { type: 'BlockStatement', body: [{ type: 'ReturnStatement', argument: init.body }] } }, '    ', true));
          continue;
        }
        hint = pascal(name.toLowerCase().replace(/_(\w)/g, (_, c) => c.toUpperCase())) + 'Entry';
        let t = v.id.typeAnnotation ? ty(v.id.typeAnnotation.typeAnnotation, mod, name) : null;
        // const X = ... as Record<K, V>: typed as that.
        if (!t && init?.type === 'TSAsExpression' && kindOf(init.typeAnnotation) === 'record') {
          const rt = ty(init.typeAnnotation, mod, name);
          if (rt && !rt.includes('TODO')) t = rt;
        }
        // const X = { ... } as const satisfies T: typed as T.
        if (!t) {
          let s = init;
          while (s?.type === 'TSAsExpression') s = s.expression;
          if (s?.type === 'TSSatisfiesExpression') {
            const st = ty(s.typeAnnotation, mod, name);
            if (st && !st.includes('TODO') && st !== 'object') {
              t = st;
              if (/^JsRecord</.test(st)) recordConsts.add(name);
            }
          }
        }
        if (t && /^List<\(/.test(t)) {
          let a = init;
          while (a?.type === 'TSAsExpression') a = a.expression;
          if (a?.type === 'ArrayExpression') for (const x of a.elements) if (x?.type === 'ArrayExpression') x.__tuple = true;
        }
        let initText = init ? ex(init, t === 'bool') : null;
        if (!t) {
          if (init?.type === 'Literal' && typeof init.value === 'number') t = Number.isInteger(init.value) && Math.abs(init.value) <= 2147483647 && !/[.eE]/.test(init.raw) && d.kind === 'const' ? 'int' : 'double';
          else if (init?.type === 'Literal' && typeof init.value === 'string') t = 'string';
          else if (init?.type === 'Literal' && typeof init.value === 'boolean') t = 'bool';
          else if (init?.type === 'TemplateLiteral') t = 'string';
          else if (init?.type === 'UnaryExpression' && init.argument.type === 'Literal') t = 'double';
          else if (init?.type === 'BinaryExpression' || (init?.type === 'TSAsExpression' && init.expression.type === 'BinaryExpression')) t = 'double';
          else if (init && (init.type === 'ArrayExpression' || (init.type === 'TSAsExpression' && init.expression.type === 'ArrayExpression'))) {
            const arr = init.type === 'ArrayExpression' ? init : init.expression;
            const kinds = new Set(arr.elements.map((x) => (x?.type === 'Literal' ? typeof x.value : 'other')));
            t = kinds.size === 1 && kinds.has('string') ? 'List<string>' : kinds.size === 1 && kinds.has('number') ? 'List<double>' : literalType(arr) ?? '/*TODO array type*/List<object>';
          } else if (init && (init.type === 'ObjectExpression' || (init.type === 'TSAsExpression' && init.expression.type === 'ObjectExpression'))) {
            const o = init.type === 'ObjectExpression' ? init : init.expression;
            const allNum = o.properties.every((p) => p.type === 'Property' && p.value.type === 'Literal' && typeof p.value.value === 'number');
            const allStr = o.properties.every((p) => p.type === 'Property' && ((p.value.type === 'Literal' && typeof p.value.value === 'string') || strip(exprType(p.value)) === 'string'));
            if (allNum || allStr) {
              t = allNum ? 'JsRecord<double>' : 'JsRecord<string>';
              recordConsts.add(name);
              initText = `new() { ${o.properties.map((p) => `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}`).join(', ')} }`;
            } else if (o.properties.every((q) => q.type === 'Property' && !q.computed)) {
              const keys = o.properties.map((q) => String(q.key.name ?? q.key.value)).sort().join(',');
              t = keys === 'x,y' ? 'Point' : objectLiteralClass(o, pascal(name.toLowerCase().replace(/_(\w)/g, (_, c) => c.toUpperCase())));
              o.__class = t;
              initText = ex(init);
            } else t = '/*TODO object const type*/object';
          } else if (init?.type === 'NewExpression' && /^new [\w<>, ]+\(/.test(initText)) t = initText.match(/^new ([\w<>, ]+)\(/)[1];
          else if (init?.type === 'NewExpression' && /^new \w+\[/.test(initText)) t = initText.match(/^new (\w+)\[/)[1] + '[]';
          else if (/^Js\.ObjectKeys/.test(initText)) t = 'List<string>';
          else if ((init?.type === 'MemberExpression' || init?.type === 'Identifier') && /^[A-Z_0-9]+$/.test(init.type === 'Identifier' ? init.name : init.property.name ?? '')) t = 'double';
          else if (/^(Time\.DAY_LENGTH|Time\.TICKS_PER_HOUR)/.test(initText)) t = 'double';
          else t = (() => { const et = strip(exprType(init)); return et && !/^object/.test(et) ? et : '/*TODO const type*/object'; })();
        } else if (t.startsWith('JsRecord') && Js_unwrap(init)?.type === 'ObjectExpression') {
          recordConsts.add(name);
          initText = `new() { ${Js_unwrap(init).properties.map((p) => (p.type === 'Property' ? `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}` : '[Js.TODO_SPREAD] = default!')).join(', ')} }`;
        }
        if (t === 'var') t = '/*TODO const type*/object';
        const isConst = d.kind === 'const' && ['int', 'double', 'string', 'bool'].includes(t) && init && (init.type === 'Literal' || init.type === 'TemplateLiteral' && init.expressions.length === 0 || (init.type === 'UnaryExpression' && init.argument.type === 'Literal'));
        if (isConst) constTypes.set(`${modClass(mod)}.${name}`, t);
        if (t && !t.includes('TODO')) moduleConstTypes.set(`${modClass(mod)}.${name}`, t);
        members.push('', ...docFor(n, '    '), `    public ${isConst ? 'const' : d.kind === 'const' ? 'static readonly' : 'static'} ${t} ${id(name)}${initText !== null ? ` = ${initText}` : ''};`);
      }
      continue;
    }
    if (d.type === 'TSTypeAliasDeclaration' || d.type === 'TSInterfaceDeclaration' || d.type === 'ClassDeclaration' || d.type === 'TSEnumDeclaration') {
      if (d.type === 'ClassDeclaration') members.push(`    /*TODO class ${d.id.name}*/`);
      continue;
    }
    if (n.type === 'ExportNamedDeclaration' && !n.declaration) continue; // export { a } from ...
    if (n.type === 'ExpressionStatement') {
      ctorBody.push(...stmt(n, '        ', { fnRet: null }));
      continue;
    }
    members.push(`    /*TODO top-level ${n.type}*/`);
  }
  }
  if (!isTest) declareInto(ast.body);

  function docFor(n, ind) {
    const before = src.slice(0, n.start).match(/\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*$/);
    if (!before) return [];
    const text = before[1].split('\n').map((l) => l.replace(/^\s*\*\s?/, '').trim()).filter(Boolean).join(' ');
    return [`${ind}/// <summary>${text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')}</summary>`];
  }

  // The module's header comment, kept.
  const header = src.match(/^(\s*\/\/.*\n)+/);
  const cls = modClass(mod);
  if (isHarness) {
    const tl = [
      `// <auto-generated> by cs/tools/ts2cs.mjs from harness/${mod.slice(8)}.harness.ts, then fixed by hand where it says so.`,
      '#nullable disable warnings',
      'using LoonieRoo.Sim;',
      'using static LoonieRoo.Harness.Node;',
      'namespace LoonieRoo.Harness;',
      '',
      `public partial class ${cls}`,
      '{',
    ];
    if (ctorBody.length) tl.push(`    static ${cls}()`, '    {', ...ctorBody, '    }');
    tl.push(...members, '}', '', ...ownClasses);
    return tl.join('\n');
  }
  if (isTest) {
    const tl = [
      `// <auto-generated> by cs/tools/ts2cs.mjs from tests/${mod.slice(5)}.test.ts, then fixed by hand where it says so.`,
      ...(skipped.length ? ['// Left out (they use the browser UI):', ...skipped.map((s) => `//   ${s}`)] : []),
      '#nullable disable warnings',
      'using LoonieRoo.Sim;',
      'using static LoonieRoo.Sim.Tests.Vitest;',
      'namespace LoonieRoo.Sim.Tests.Ported;',
      '',
      `public partial class ${cls}`,
      '{',
    ];
    if (ctorBody.length) tl.push(`    static ${cls}()`, '    {', ...ctorBody, '    }');
    tl.push(...jsonImports, ...members, '}', '');
    return tl.join('\n');
  }
  const lines = [
    `// <auto-generated> by cs/tools/ts2cs.mjs from ${mod === 'debugText' ? 'src/debug/text' : 'src/sim/' + mod}.ts, then fixed by hand where it says so.`,
    ...(header ? header[0].trimEnd().split('\n').map((l) => l.replace(/^\s*/, '')) : []),
    '#nullable disable warnings',
    'namespace LoonieRoo.Sim;',
    '',
    `public static partial class ${cls}`,
    '{',
  ];
  if (ctorBody.length) lines.push(`    static ${cls}()`, '    {', ...ctorBody, '    }');
  lines.push(...members, '}', '');
  return lines.join('\n');
}

// Predicates (filter, some, every, find, findIndex): their arrow bodies are conditions. Marked on the AST
// before translating (a pass over each module's calls).
const PREDICATE_METHODS = new Set(['filter', 'some', 'every', 'find', 'findIndex', 'findLast']);
const origParse = parseAst;

const only = process.argv.slice(2);
// A first pass to learn every function's return type (for calls to functions in modules translated later).
for (const m of files) if (!HAND[m]) translate(m);
inline.clear();
mkdirSync(OUT, { recursive: true });
let todos = 0;
for (const m of files) {
  if (HAND[m]) continue;
  if (only.length && !only.includes(m)) continue;
  const text = translate(m);
  todos += (text.match(/\/\*TODO/g) ?? []).length;
  writeFileSync(join(OUT, modClass(m) + '.g.cs'), text);
}
const TEST_OUT = 'cs/Sim.Tests/Generated';
mkdirSync(TEST_OUT, { recursive: true });
let testCount = 0;
for (const m of testFiles) {
  if (only.length && !only.includes(m)) continue;
  sources.set(m, readFileSync(sourcePath(m), 'utf8'));
  const text = translate(m);
  testCount++;
  writeFileSync(join(TEST_OUT, modClass(m) + '.g.cs'), text);
}
const HARNESS_OUT = 'cs/Harness/Generated';
mkdirSync(HARNESS_OUT, { recursive: true });
for (const m of harnessFiles) {
  if (only.length && !only.includes(m)) continue;
  sources.set(m, readFileSync(sourcePath(m), 'utf8'));
  writeFileSync(join(HARNESS_OUT, modClass(m) + '.g.cs'), translate(m));
}
const inl = ['// <auto-generated> by cs/tools/ts2cs.mjs: classes for object types written inline in the TypeScript.', '#nullable disable warnings', 'namespace LoonieRoo.Sim;', ''];
for (const [name, [members, mod]] of inline) {
  const isRng = members.some((m) => (m.key.name ?? m.key.value) === 'rng');
  inl.push(`public partial class ${name}${isRng ? ' : IRngHolder' : ''}`, '{');
  const has = (f, tp) => members.some((m) => (m.key.name ?? m.key.value) === f) && tp;
  for (const m of members) {
    const f = m.key.name ?? m.key.value;
    hint = name + pascal(String(f));
    let c = m.__cs ?? ty(m.typeAnnotation?.typeAnnotation, mod, f) ?? 'object';
    if (m.optional && !c.endsWith('?')) c += '?';
    inl.push(`    public ${c} ${id(String(f))} { get; set; }`);
  }
  inl.push('}', '');
}
writeFileSync(join(OUT, 'Inline.g.cs'), inl.join('\n'));
// Hand fixes (fixups.mjs), applied to the files just written.
let applied = 0;
for (const [file, from, to] of FIXUPS) {
  const path = file.startsWith('Tests/') ? join(TEST_OUT, file.slice(6) + '.g.cs') : file.startsWith('Harness/') ? join(HARNESS_OUT, file.slice(8) + '.g.cs') : join(OUT, file + '.g.cs');
  const text = readFileSync(path, 'utf8');
  if (!text.includes(from)) {
    console.log(`fixup not found in ${file}: ${from.slice(0, 80)}`);
    continue;
  }
  writeFileSync(path, text.split(from).join(to));
  applied++;
}
if (FIXUPS.length) console.log(`${applied} of ${FIXUPS.length} hand fixes applied`);
console.log(`translated ${only.length || files.length - Object.keys(HAND).length} modules; ${todos} TODO markers`);
