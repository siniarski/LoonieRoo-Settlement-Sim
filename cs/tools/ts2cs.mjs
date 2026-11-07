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
const HAND = { grid: 'Grids', pathfinding: 'Pathfinding', space: 'Space', rng: 'Rng', hash: 'Hash', time: 'Time', lookup: 'Lookup' };
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

/** Field name -> the C# types it has across the state classes. */
const fieldTypes = new Map();
for (const m of stateSrc.matchAll(/public ([\w<>?, \[\]]+) @?(\w+) \{ get; set; \}/g)) {
  if (!fieldTypes.has(m[2])) fieldTypes.set(m[2], new Set());
  fieldTypes.get(m[2]).add(m[1].replace(/\?$/, ''));
}
/** A field whose value is always a JsRecord. */
const recordField = (name) => fieldTypes.has(name) && [...fieldTypes.get(name)].every((x) => x.startsWith('JsRecord'));
const typeClasses = new Set([...stateSrc.matchAll(/public partial (?:class|interface) (\w+)/g)].map((m) => m[1]));

const files = readdirSync(SIM).filter((f) => f.endsWith('.ts')).map((f) => f.replace(/\.ts$/, ''));
const pascal = (s) => s.replace(/^\w/, (c) => c.toUpperCase());
/** The C# class for a module. */
function modClass(m) {
  if (HAND[m]) return HAND[m];
  const p = pascal(m);
  return typeClasses.has(p) ? p + 'Mod' : p === 'State' ? 'States' : p;
}

// --- Every type alias and interface, to map annotations ---------------------------------------
const aliases = new Map(); // name -> node (type alias annotation)
const interfaces = new Set();
const interfaceNodes = new Map(); // name -> interface declaration
const sources = new Map();
for (const m of files) {
  const src = readFileSync(join(SIM, m + '.ts'), 'utf8');
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
  let name = hint.replace(/[^A-Za-z0-9]/g, '');
  if (typeClasses.has(name)) name += 'Shape';
  if (inline.has(name) && inline.get(name)[2] !== names) name += inline.size;
  if (!inline.has(name)) inline.set(name, [members, mod, names]);
  return name;
}

/** Fields that are whole numbers in every class (gen-state.mjs INT_FIELDS). */
/** Return types of functions, by "Class.name" and by bare name (filled as modules are translated; two passes). */
const fnRet = new Map(Object.entries({ "Hash.hashRoll": "double", "Hash.idNumber": "double", "Rng.random": "double", "Rng.randomInt": "int", "Grids.inBounds": "bool", "Grids.getTile": "string?", "Grids.isWalkable": "bool", "Grids.tileIndex": "int", "Grids.edgeBetween": "int", "Grids.edgeIndex": "int", "Grids.isOpen": "bool", "Grids.isLocked": "bool", "Space.manhattan": "int", "Space.samePoint": "bool", "Space.nearestTile": "Point?", "Pathfinding.findPathToAny": "List<Point>?", "Pathfinding.findPath": "List<Point>?", "Pathfinding.canReach": "bool", "Pathfinding.isPassable": "bool", "Pathfinding.crossable": "bool", "Pathfinding.openBetween": "bool", "Pathfinding.seesThrough": "bool", "Time.dayOf": "int", "Time.tickOfDay": "int", "Time.hourOf": "double", "Time.isNight": "bool", "Time.slowTick": "bool" }));

/** Module constants that are C# consts (literals), by "Class.NAME" -> type: usable as default values. */
const constTypes = new Map();

/** Re-exports, module -> name -> the module it really lives in. */
const reexports = new Map();
for (const m of files) {
  const ast = parseAst(sources.get(m), { lang: 'ts' }, m + '.ts');
  for (const n of ast.body) {
    if (n.type !== 'ExportNamedDeclaration' || n.declaration || !n.source) continue;
    for (const s of n.specifiers) {
      if (!reexports.has(m)) reexports.set(m, new Map());
      reexports.get(m).set(s.exported.name, n.source.value.replace(/^\.\//, ''));
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
    case 'TSTypeOperator': case 'TSParenthesizedType': return kindOf(t.typeAnnotation, depth + 1);
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
  if (!t) return null;
  const nullable = t.type === 'TSUnionType' && t.types.some((u) => u.type === 'TSNullKeyword' || u.type === 'TSUndefinedKeyword');
  let core = t;
  if (t.type === 'TSUnionType') {
    const rest = t.types.filter((u) => u.type !== 'TSNullKeyword' && u.type !== 'TSUndefinedKeyword');
    core = rest.length === 1 ? rest[0] : { ...t, types: rest };
  }
  const q = nullable ? '?' : '';
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
        return (ks.size === 1 && ks.has('string') ? 'List<string>' : ks.size === 1 && ks.has('number') ? 'List<double>' : 'List<object>') + q;
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
      return { Int32Array: 'int[]', Uint32Array: 'uint[]', Float64Array: 'double[]', Uint8Array: 'byte[]', Int8Array: 'sbyte[]', Uint16Array: 'ushort[]', Float32Array: 'float[]' }[name] + q;
    }
    case 'fn': {
      core = deref(core);
      if (core.type === 'TSParenthesizedType') core = core.typeAnnotation;
      const params = (core.params ?? []).map((p) => ty(p.typeAnnotation?.typeAnnotation, mod, p.name) ?? 'object');
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
        name = renames[mod + '.ts']?.[name] ?? name;
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

  for (const n of ast.body) {
    if (n.type !== 'ImportDeclaration') continue;
    const from = n.source.value.replace(/^\.\//, '');
    for (const s of n.specifiers) {
      if (s.type !== 'ImportSpecifier' || s.importKind === 'type' || n.importKind === 'type') continue;
      const name = s.imported.name;
      imports.set(s.local.name, `${modClass(home(from, name))}.${name}`);
    }
  }
  const moduleNames = new Set();

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
    const r = ex0(e, bool);
    return bool && !isBoolish(e) ? `Js.T(${r})` : r;
  }

  function isBoolish(e) {
    if (!e) return false;
    if (e.type === 'BinaryExpression') return ['==', '!=', '===', '!==', '<', '>', '<=', '>=', 'in', 'instanceof'].includes(e.operator);
    if (e.type === 'LogicalExpression') return e.operator !== '??';
    if (e.type === 'UnaryExpression') return e.operator === '!';
    if (e.type === 'Literal') return typeof e.value === 'boolean';
    if (e.type === 'ParenthesizedExpression') return isBoolish(e.expression);
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
      if (x.type === 'Literal' && typeof x.value === 'number') return Number.isInteger(x.value) ? 'int' : 'double';
      if (x.type === 'UnaryExpression' && x.argument.type === 'Literal' && typeof x.argument.value === 'number') return Number.isInteger(x.argument.value) ? 'int' : 'double';
      if (x.type === 'Identifier' || x.type === 'BinaryExpression') return 'num?';
      return null;
    });
    if (el.some((k) => k === null)) return null;
    const kinds = new Set(el.map((k) => (k === 'num?' ? 'int' : k)));
    if (kinds.size === 2 && kinds.has('int') && kinds.has('double')) return 'List<double>';
    if (kinds.size !== 1) return null;
    return `List<${[...kinds][0]}>`;
  }

  /** A class for an object literal's shape (fields typed from their values, as far as they show). */
  function objectLiteralClass(o, name) {
    const keys = o.properties.map((p) => String(p.key.name ?? p.key.value)).sort().join(',');
    if (classByFields.has(keys)) return classByFields.get(keys);
    const members = o.properties.map((p) => {
      const k = String(p.key.name ?? p.key.value);
      const v = p.value;
      let tp = 'object';
      if (INT_FIELDS.has(k)) tp = 'int';
      else if (v.type === 'Literal' && typeof v.value === 'number') tp = 'double';
      else if (v.type === 'Literal' && typeof v.value === 'string') tp = 'string';
      else if (v.type === 'Literal' && typeof v.value === 'boolean') tp = 'bool';
      else if (v.type === 'TemplateLiteral') tp = 'string';
      else if (v.type === 'BinaryExpression' && ['<', '>', '<=', '>=', '==', '===', '!=', '!=='].includes(v.operator)) tp = 'bool';
      else if (v.type === 'BinaryExpression' || (v.type === 'CallExpression' && v.callee.type === 'MemberExpression' && v.callee.object.name === 'Math')) tp = 'double';
      else if (v.type === 'ArrayExpression') tp = literalType(v) ?? 'List<object>';
      return { key: { name: k }, type: 'TSPropertySignature', typeAnnotation: null, __cs: tp };
    });
    let n = name;
    if (typeClasses.has(n) || inline.has(n)) n += 'Shape' + inline.size;
    inline.set(n, [members, mod, members.map((m) => m.key.name).join(',')]);
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
    scopes.push({ declared: new Set(names), renames: new Map() });
  }
  function popScope() {
    scopes.pop();
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
    if (init && (init.type === 'ArrayExpression' || (init.type === 'ConditionalExpression' && init.consequent.type === 'ArrayExpression' && init.alternate.type === 'ArrayExpression'))) {
      const tuple = (a) => `(${a.elements.map((x) => ex(x)).join(', ')})`;
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
        if (typeof e.value === 'number') return num(e.value, e.raw);
        if (typeof e.value === 'boolean') return String(e.value);
        if (e.regex) return `/*TODO regex ${e.raw}*/null`;
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
          if (i < e.expressions.length) s += `{Js.Str(${ex(e.expressions[i])})}`;
        });
        return s + '"';
      }
      case 'ParenthesizedExpression': return `(${ex(e.expression, bool)})`;
      case 'TSAsExpression': case 'TSSatisfiesExpression': case 'TSTypeAssertion': {
        const t = e.typeAnnotation;
        if (t.type === 'TSTypeReference' && t.typeName.name === 'const') return ex(e.expression, bool);
        const k = kindOf(t);
        if (k === 'object' && t.type === 'TSTypeReference') {
          const c = ty(t, mod);
          if (c && !c.includes('TODO')) return `((${c})(${ex(e.expression)}))`;
        }
        return ex(e.expression, bool);
      }
      case 'TSNonNullExpression': return `${ex(e.expression)}!`;
      case 'ChainExpression': return ex(e.expression, bool);
      case 'UnaryExpression': {
        if (e.operator === '!') return `!${operand(e.argument, true)}`;
        if (e.operator === '-') return `-${operand(e.argument)}`;
        if (e.operator === '+') return `(double)(${ex(e.argument)})`;
        if (e.operator === 'typeof') return `/*TODO typeof*/Js.TypeOf(${ex(e.argument)})`;
        if (e.operator === 'void') return 'null';
        if (e.operator === 'delete') {
          const m = e.argument;
          return `${ex(m.object)}.Remove(${m.computed ? `Js.Str(${ex(m.property)})` : str(m.property.name)})`;
        }
        if (e.operator === '~') return `~${ex(e.argument)}`;
        return `/*TODO unary ${e.operator}*/`;
      }
      case 'UpdateExpression': return e.prefix ? `${e.operator}${ex(e.argument)}` : `${ex(e.argument)}${e.operator}`;
      case 'BinaryExpression': {
        const op = e.operator;
        const l = operand(e.left);
        const r = operand(e.right);
        const stringish = (x) => (x.type === 'Literal' && typeof x.value === 'string') || x.type === 'TemplateLiteral' || (x.type === 'MemberExpression' && !x.computed && ['id', 'name', 'kind', 'label', 'key', 'look', 'type'].includes(x.property.name));
        if (['<', '>', '<=', '>='].includes(op) && (stringish(e.left) || stringish(e.right))) return `string.CompareOrdinal(${ex(e.left)}, ${ex(e.right)}) ${op} 0`;
        if (op === '===' || op === '==') return `${l} == ${r}`;
        if (op === '!==' || op === '!=') return `${l} != ${r}`;
        if (op === '/') return `((double)(${l}) / (${r}))`;
        if (op === '>>>') return `Js.Ushr(${l}, ${r})`;
        if (op === '**') return `JsMath.pow(${l}, ${r})`;
        if (op === 'in') return `/*TODO in*/Js.In(${l}, ${r})`;
        if (op === 'instanceof') return `/*TODO instanceof*/(${l} is ${r})`;
        return `${l} ${op} ${r}`;
      }
      case 'LogicalExpression': {
        if (e.operator === '??' && e.right.type === 'Literal' && typeof e.right.value === 'number') return `Js.Q(${ex(e.left)}, ${ex(e.right)})`;
        if (e.operator === '??') return `${operand(e.left)} ?? ${operand(e.right)}`;
        if (!bool && e.operator === '||' && !isBoolish(e.left) && !isBoolish(e.right)) return `Js.Or(${ex(e.left)}, ${ex(e.right)})`;
        return `${operand(e.left, true)} ${e.operator} ${operand(e.right, true)}`;
      }
      case 'ConditionalExpression': return `${operand(e.test, true)} ? ${operand(e.consequent)} : ${operand(e.alternate)}`;
      case 'AssignmentExpression': {
        if (e.left.type === 'ObjectPattern' || e.left.type === 'ArrayPattern') return `/*TODO destructuring assignment*/`;
        if (e.operator === '||=') return `${ex(e.left)} = Js.Or(${ex(e.left)}, ${ex(e.right)})`;
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
        if (e.computed) return `${obj}${q}[Js.Idx(${ex(e.property)})]`;
        const name = e.property.name;
        // Math.PI and friends.
        if (e.object.type === 'Identifier' && !imports.has(e.object.name)) {
          if (e.object.name === 'Math') return `JsMath.${name}`;
          if (e.object.name === 'Number') return `JsNumber.${name}`;
        }
        if (e.object.type === 'Identifier' && recordConsts.has(e.object.name)) return `${obj}[${str(name)}]`;
        return `${obj}${q}.${id(name)}`;
      }
      case 'CallExpression': return call(e);
      case 'NewExpression': return newEx(e);
      case 'ArrayExpression': {
        if (e.elements.length === 1 && e.elements[0]?.type === 'SpreadElement') return `(${ex(e.elements[0].argument)}).ToList()`;
        const items = e.elements.map((x) => (x === null ? 'default' : x.type === 'SpreadElement' ? `.. ${ex(x.argument)}` : ex(x)));
        return `[${items.join(', ')}]`;
      }
      case 'ObjectExpression': return obj(e);
      case 'ArrowFunctionExpression': case 'FunctionExpression': return lambda(e);
      case 'SpreadElement': return `/*TODO spread*/${ex(e.argument)}`;
      case 'AwaitExpression': return '/*TODO await*/';
      default: return `/*TODO ${e.type}*/`;
    }
  }

  function call(e) {
    const c = e.callee;
    if (c.type === 'MemberExpression' && !c.computed && PREDICATE_METHODS.has(c.property.name)) for (const a of e.arguments) if (a.type === 'ArrowFunctionExpression') a.__pred = true;
    // Number and String passed as callbacks (.map(Number)): as lambdas.
    const arg = (a) => (a.type === 'Identifier' && a.name === 'Number' && !imports.has('Number') ? '(__v) => Js.Num(__v)' : a.type === 'Identifier' && a.name === 'String' && !imports.has('String') ? '(__v) => Js.Str(__v)' : ex(a));
    const args = () => e.arguments.map((a) => (a.type === 'SpreadElement' ? `[.. ${ex(a.argument)}]` : arg(a))).join(', ');
    const q = e.optional ? '?' : '';
    if (c.type === 'MemberExpression' && !c.computed) {
      const o = c.object;
      const name = c.property.name;
      if ((name === 'push' || name === 'unshift') && e.arguments.some((a) => a.type === 'SpreadElement')) {
        return e.arguments.map((a) => (a.type === 'SpreadElement' ? `${operand(o)}.${name}All(${ex(a.argument)})` : `${operand(o)}.${name}(${ex(a)})`)).join('; ');
      }
      if (o.type === 'Identifier' && !imports.has(o.name)) {
        if (o.name === 'Math') {
          if (e.arguments.length === 1 && e.arguments[0].type === 'SpreadElement' && (name === 'max' || name === 'min')) return `JsMath.${name}(${ex(e.arguments[0].argument)})`;
          return `JsMath.${name}(${args()})`;
        }
        if (o.name === 'Number') return `JsNumber.${name}(${args()})`;
        if (o.name === 'Object') {
          if (name === 'keys') return `Js.ObjectKeys(${args()})`;
          if (name === 'values') return `Js.ObjectValues(${args()})`;
          if (name === 'entries') return `Js.ObjectEntries(${args()})`;
          return `/*TODO Object.${name}*/Js.Object_${name}(${args()})`;
        }
        if (o.name === 'JSON') return `/*TODO JSON.${name}*/Js.Json_${name}(${args()})`;
        if (o.name === 'Array' && name === 'from') {
          const [src, fn] = e.arguments;
          if (src?.type === 'ObjectExpression' && src.properties[0]?.key?.name === 'length') return `Js.ArrayFrom(${ex(src.properties[0].value)}, ${fn ? lambdaIndexOnly(fn) : '_ => default'})`;
          return `(${ex(src)}).ToList()`;
        }
        if (o.name === 'Array' && name === 'isArray') return `(${args()} is System.Collections.IList)`;
      }
      if (name === 'toString' && e.arguments.length === 0) return `Js.Str(${ex(o)})`;
      return `${operand(o)}${c.optional ? '?' : ''}.${id(name)}${q ? '' : ''}(${args()})`;
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
    const c = e.callee.type === 'Identifier' ? e.callee.name : null;
    const targs = e.typeArguments?.params ?? [];
    const args = e.arguments.map((a) => ex(a)).join(', ');
    const typed = { Int32Array: 'int', Uint32Array: 'uint', Float64Array: 'double', Uint8Array: 'byte', Int8Array: 'sbyte', Uint16Array: 'ushort', Float32Array: 'float' };
    if (c && typed[c]) {
      const a = e.arguments[0];
      if (a && a.type !== 'ArrayExpression' && !(a.type === 'Identifier' && /list|arr|items/i.test(a.name))) return `new ${typed[c]}[${ex(a)}]`;
      return `/*TODO typed array from*/new ${typed[c]}[] { }`;
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
    return `new() { ${o.properties.map((p) => (p.type === 'Property' && !p.computed ? `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}` : '[Js.TODO_SPREAD] = default!')).join(', ')} }`;
  }

  function obj(e) {
    const parts = [];
    let spread = null;
    for (const p of e.properties) {
      if (p.type === 'SpreadElement') {
        spread = p.argument;
        continue;
      }
      if (p.computed) return `/*TODO computed key*/new()`;
      const key = p.key.type === 'Identifier' ? p.key.name : p.key.value;
      const value = p.shorthand ? ex(p.key) : p.value.type === 'ArrowFunctionExpression' || p.value.type === 'FunctionExpression' ? lambda(p.value) : p.value.type === 'ObjectExpression' && recordField(String(key)) ? recordInit(p.value) : ex(p.value);
      parts.push(`${id(String(key))} = ${INT_FIELDS.has(String(key)) ? `Js.I(${value})` : value}`);
    }
    if (spread) {
      const first = e.properties[0];
      const spreads = e.properties.filter((p) => p.type === 'SpreadElement').length;
      if (first.type === 'SpreadElement' && spreads === 1) {
        if (!parts.length) return `Js.Clone(${ex(first.argument)})`;
        const v = `__s${tmp++}`;
        return `Js.With(Js.Clone(${ex(first.argument)}), ${v} => { ${parts.map((x) => `${v}.${x};`).join(' ')} })`;
      }
      return `/*TODO object spread*/Js.Spread(${ex(spread)}, new() { ${parts.join(', ')} })`;
    }
    const keys = e.properties.map((p) => String(p.key?.name ?? p.key?.value)).sort().join(',');
    if (keys === 'x,y') return `new Point { ${parts.join(', ')} }`;
    return parts.length ? `new() { ${parts.join(', ')} }` : 'new()';
  }

  function param(p, defaults) {
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
    if (!cs && def?.type === 'Identifier' && constTypes.has(imports.get(def.name) ?? `${modClass(mod)}.${def.name}`)) cs = 'double';
    if (!cs && def && ((def.type === 'Identifier' && /^[A-Z_0-9]+$/.test(def.name)) || def.type === 'BinaryExpression' || (def.type === 'MemberExpression' && /^[A-Z_0-9]+$/.test(def.property?.name ?? '')))) cs = 'double';
    if (!cs) cs = '/*TODO param type*/object';
    const nm = pname(name);
    if (p.optional && !cs.endsWith('?')) cs += '?';
    if (def) {
      // A C# const (a module constant that's a literal) can be a default value too.
      const constName = def.type === 'Identifier' ? (imports.get(def.name) ?? `${modClass(mod)}.${def.name}`) : null;
      const isConstDefault = !!constName && constTypes.has(constName);
      if (isConstDefault && cs.includes('TODO')) cs = constTypes.get(constName);
      const literal = def.type === 'Literal' || (def.type === 'Identifier' && def.name === 'Infinity') || (def.type === 'UnaryExpression' && def.argument.type === 'Literal') || isConstDefault;
      if (literal) return `${cs} ${nm} = ${ex(def)}`;
      defaults.push(`${nm} ??= ${ex(def)};`);
      return `${cs.endsWith('?') ? cs : cs + '?'} ${nm} = null`;
    }
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
      if (typed) return param(p, defaults);
      if (p.type === 'Identifier') return declName(p.name);
      if (p.type === 'AssignmentPattern') return declName(p.left.name);
      const v = `__p${tmp++}`;
      if (p.type === 'ArrayPattern') unpack.push(destructure(p.elements, v));
      else if (p.type === 'ObjectPattern') for (const q of p.properties) unpack.push(`var ${id(q.value?.name ?? q.key.name)} = ${v}.${id(q.key.name)};`);
      return v;
    });
    const head = `(${ps.join(', ')})`;
    if (unpack.length) {
      const inner = fn.body.type === 'BlockStatement' ? block(fn.body, '            ', { fnRet: null }).split('\n').slice(1, -1).join('\n') : `            return ${ex(fn.body, !!fn.__pred)};`;
      return `${head} =>\n        {\n${unpack.map((u) => '            ' + u).join('\n')}\n${inner}\n        }`;
    }
    // A predicate's body is a condition.
    if (fn.body.type !== 'BlockStatement') {
      const parent = fn.__pred;
      return `${head} => ${ex(fn.body, !!parent)}`;
    }
    return `${head} =>\n${block(fn.body, '            ', { fnRet: fn.returnType ? ty(fn.returnType.typeAnnotation, mod) : null })}`;
  }

  // Statements.
  function block(b, ind, ctx) {
    pushScope(namesIn(b.body));
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
    let lit = init;
    while (lit.type === 'TSAsExpression') lit = lit.expression;
    if (lit.type === 'ObjectExpression' && lit.properties.length && lit.properties.every((p) => p.type === 'Property' && p.value.type === 'Literal' && typeof p.value.value === 'number')) {
      recordConsts.add(name);
      d.__record = true;
      return 'JsRecord<double>';
    }
    if (init.type === 'ObjectExpression' && init.properties.length && init.properties.every((p) => p.type === 'Property' && !p.computed)) {
      const keys = init.properties.map((p) => String(p.key.name ?? p.key.value));
      if (keys.slice().sort().join(',') === 'x,y') return 'Point';
      return objectLiteralClass(init, pascal(curFn) + pascal(name));
    }
    if (init.type === 'Literal' && typeof init.value === 'number' && kind === 'let' && !Number.isInteger(init.value)) return 'double';
    return 'var';
  }

  function stmt(s, ind, ctx) {
    switch (s.type) {
      case 'ExpressionStatement': {
        const x = s.expression;
        // a, b = c... sequence statements, and calls like list.forEach(...)
        return [`${ind}${ex(x)};`];
      }
      case 'VariableDeclaration': {
        const lines = [];
        for (const d of s.declarations) {
          if (d.id.type === 'ObjectPattern') {
            const t = `__d${tmp++}`;
            lines.push(`${ind}var ${t} = ${ex(d.init)};`);
            for (const p of d.id.properties) {
              if (p.type === 'RestElement') { lines.push(`${ind}/*TODO rest*/`); continue; }
              const key = p.key.name;
              if (p.value.type === 'AssignmentPattern') lines.push(`${ind}var ${id(p.value.left.name)} = ${t}.${id(key)} ?? ${ex(p.value.right)};`);
              else lines.push(`${ind}var ${id(p.value.name)} = ${t}.${id(key)};`);
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
              const ps = fn.params.map((p) => param(p, defaults));
              const ret = fn.returnType ? ty(fn.returnType.typeAnnotation, mod) : guessReturn(fn);
              const bodyText = fn.body.type === 'BlockStatement' ? block(fn.body, ind + '    ', { fnRet: ret }) : `${ind}{\n${ind}    ${ret === 'void' ? '' : 'return '}${ex(fn.body, ret === 'bool')};\n${ind}}`;
              lines.push(`${ind}${ret} ${declName(d.id.name)}(${ps.join(', ')})`);
              lines.push(bodyText.split('\n').map((l, i) => (i === 0 && fn.body.type === 'BlockStatement' ? ind + l.trimStart() : l)).join('\n'));
              continue;
            }
          }
          let lit = d.init;
          while (lit?.type === 'TSAsExpression') lit = lit.expression;
          const init = d.__record || (t.startsWith('JsRecord') && lit?.type === 'ObjectExpression')
            ? ` = new() { ${lit.properties.map((p) => (p.type === 'Property' ? `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}` : '[Js.TODO_SPREAD] = default!')).join(', ')} }`
            : d.init ? ` = ${ex(d.init, t === 'bool')}` : t.endsWith('?') ? ' = null' : ' = default!';
          lines.push(`${ind}${t} ${declName(d.id.name)}${init};`);
        }
        return lines;
      }
      case 'ReturnStatement':
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
        const iter = ex(s.right);
        pushScope(s.left.type === 'VariableDeclaration' ? namesIn([s.left]) : []);
        const v = left.type === 'ArrayPattern' ? `var (${left.elements.map((x) => (x ? declName(x.name) : '_')).join(', ')})` : left.type === 'ObjectPattern' ? `var __o${tmp}` : `var ${declName(left.name)}`;
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
    if (rets.every((r) => r && isBoolish(r))) return 'bool';
    if (rets.every((r) => r && (r.type === 'TemplateLiteral' || (r.type === 'Literal' && typeof r.value === 'string')))) return 'string';
    return fromReturns(rets, fn) ?? '/*TODO return type*/object';
  }

  /** A return type from what's returned: numbers (arithmetic, Math, literals), an object literal's class, a literal list. */
  function fromReturns(rets, fn) {
    const isNull = (r) => r.type === 'Literal' && r.value === null || (r.type === 'Identifier' && r.name === 'undefined');
    const numeric = (r) => (r.type === 'Literal' && typeof r.value === 'number') || (r.type === 'BinaryExpression' && ['+', '-', '*', '/', '%'].includes(r.operator) && !(r.left.type === 'Literal' && typeof r.left.value === 'string') && !(r.right.type === 'TemplateLiteral') && !(r.left.type === 'TemplateLiteral')) || (r.type === 'CallExpression' && r.callee.type === 'MemberExpression' && r.callee.object.name === 'Math') || (r.type === 'UnaryExpression' && r.operator === '-') || r.type === 'UpdateExpression';
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
    const cts = real.map(callType);
    if (real.length && cts.every((c) => c && c === cts[0] && !c.includes('TODO'))) return cts[0].replace(/\?$/, '') + (nul || cts[0].endsWith('?') ? '?' : '');
    if (real.length && real.every(numeric)) return 'double' + (nul ? '?' : '');
    const objs = real.filter((r) => r.type === 'ObjectExpression' && r.properties.every((p) => p.type === 'Property' && !p.computed));
    if (real.length && objs.length === real.length) {
      const keys = objs[0].properties.map((p) => String(p.key.name ?? p.key.value)).sort().join(',');
      if (keys === 'x,y') return 'Point' + (nul ? '?' : '');
      const all = { ...objs[0], properties: objs.flatMap((o) => o.properties).filter((p, i, arr) => arr.findIndex((q) => (q.key.name ?? q.key.value) === (p.key.name ?? p.key.value)) === i) };
      return objectLiteralClass(all, pascal(curFn || fn.id?.name || 'Fn') + 'Result') + (nul ? '?' : '');
    }
    if (real.length === 1 && literalType(real[0])) return literalType(real[0]);
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
    const ps = f.params.map((p) => param(p, defaults));
    hint = pascal(name) + 'Result';
    const ret = f.returnType ? ty(f.returnType.typeAnnotation, mod) : guessReturn(f);
    if (ret && !ret.includes('TODO')) {
      fnRet.set(top ? `${modClass(mod)}.${name}` : name, ret);
      if (!top) fnRet.set(name, ret);
    }
    const head = `${ind}${top ? 'public static ' : ''}${ret} ${id(name)}${tparams}(${ps.join(', ')})${where}`;
    const b = block(f.body, ind + '    ', { fnRet: ret }).split('\n');
    popScope();
    b[0] = ind + b[0].trimStart();
    if (defaults.length) b.splice(1, 0, ...defaults.map((d) => `${ind}    ${d}`));
    b[b.length - 1] = ind + b[b.length - 1].trimStart();
    curFn = outerFn;
    // Whole-number parameters: a companion taking JavaScript numbers (values from Math.floor and the like).
    const intParams = ps.map((x) => /^int\??\s/.test(x));
    if (top && intParams.some(Boolean) && !tparams) {
      const dps = ps.map((x, i) => (intParams[i] ? x.replace(/^int/, 'double') : x));
      // (The parameters' C# names, as declared: a scope may have renamed one.)
      const names = ps.map((x) => x.replace(/ = .*$/, '').trim().split(/\s+/).pop());
      const call = names.map((n, i) => (intParams[i] ? (ps[i].startsWith('int?') ? `Js.I(${n})` : `(int)${n}`) : n)).join(', ');
      return [head, ...b, `${ind}public static ${ret} ${id(name)}(${dps.join(', ')})${where} => ${id(name)}(${call});`];
    }
    return [head, ...b];
  }

  // Module-level declarations.
  const members = [];
  for (const n of ast.body) {
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
            const allStr = o.properties.every((p) => p.type === 'Property' && p.value.type === 'Literal' && typeof p.value.value === 'string');
            if (allNum || allStr) {
              t = allNum ? 'JsRecord<double>' : 'JsRecord<string>';
              recordConsts.add(name);
              initText = `new() { ${o.properties.map((p) => `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}`).join(', ')} }`;
            } else t = '/*TODO object const type*/object';
          } else if (init?.type === 'NewExpression' && /^new [\w<>, ]+\(/.test(initText)) t = initText.match(/^new ([\w<>, ]+)\(/)[1];
          else if (init?.type === 'NewExpression' && /^new \w+\[/.test(initText)) t = initText.match(/^new (\w+)\[/)[1] + '[]';
          else if (/^Js\.ObjectKeys/.test(initText)) t = 'List<string>';
          else if ((init?.type === 'MemberExpression' || init?.type === 'Identifier') && /^[A-Z_0-9]+$/.test(init.type === 'Identifier' ? init.name : init.property.name ?? '')) t = 'double';
          else if (/^(Time\.DAY_LENGTH|Time\.TICKS_PER_HOUR)/.test(initText)) t = 'double';
          else t = '/*TODO const type*/object';
        } else if (t.startsWith('JsRecord') && init?.type === 'ObjectExpression') {
          recordConsts.add(name);
          initText = `new() { ${init.properties.map((p) => (p.type === 'Property' ? `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}` : '[Js.TODO_SPREAD] = default!')).join(', ')} }`;
        }
        if (t === 'var') t = '/*TODO const type*/object';
        const isConst = d.kind === 'const' && ['int', 'double', 'string', 'bool'].includes(t) && init && (init.type === 'Literal' || init.type === 'TemplateLiteral' && init.expressions.length === 0 || (init.type === 'UnaryExpression' && init.argument.type === 'Literal'));
        if (isConst) constTypes.set(`${modClass(mod)}.${name}`, t);
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

  function docFor(n, ind) {
    const before = src.slice(0, n.start).match(/\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*$/);
    if (!before) return [];
    const text = before[1].split('\n').map((l) => l.replace(/^\s*\*\s?/, '').trim()).filter(Boolean).join(' ');
    return [`${ind}/// <summary>${text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')}</summary>`];
  }

  // The module's header comment, kept.
  const header = src.match(/^(\s*\/\/.*\n)+/);
  const cls = modClass(mod);
  const lines = [
    `// <auto-generated> by cs/tools/ts2cs.mjs from src/sim/${mod}.ts, then fixed by hand where it says so.`,
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
const inl = ['// <auto-generated> by cs/tools/ts2cs.mjs: classes for object types written inline in the TypeScript.', '#nullable disable warnings', 'namespace LoonieRoo.Sim;', ''];
for (const [name, [members, mod]] of inline) {
  inl.push(`public partial class ${name}`, '{');
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
  const path = join(OUT, file + '.g.cs');
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
