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

const SIM = 'src/sim';
const OUT = 'cs/Sim/Generated';
/** Modules ported by hand, and their C# class names. */
const HAND = { grid: 'Grids', pathfinding: 'Pathfinding', space: 'Space', rng: 'Rng', hash: 'Hash', time: 'Time', lookup: 'Lookup' };
const renames = JSON.parse(readFileSync('cs/tools/typenames.json', 'utf8'));
const stateSrc = readFileSync('cs/Sim/State.g.cs', 'utf8');
const typeClasses = new Set([...stateSrc.matchAll(/public partial class (\w+)/g)].map((m) => m[1]));

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
  if (core.type === 'TSIndexedAccessType' && fieldType(core)) return ty(fieldType(core), mod, intName)?.replace(/\?$/, '') + q;
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
    default: return 'object' + q;
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
      imports.set(s.local.name, `${modClass(from)}.${name}`);
    }
  }
  const moduleNames = new Set();

  /** An operand: wrapped in parentheses when it's an operation itself, so precedence can't change. */
  function operand(e, bool = false) {
    const s = ex(e, bool);
    const compound = ['BinaryExpression', 'LogicalExpression', 'ConditionalExpression', 'AssignmentExpression', 'SequenceExpression', 'ArrowFunctionExpression', 'TSAsExpression'].includes(e.type);
    return compound && !(s.startsWith('(') && s.endsWith(')')) ? `(${s})` : s;
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
    const args = () => e.arguments.map((a) => (a.type === 'SpreadElement' ? `[.. ${ex(a.argument)}]` : ex(a))).join(', ');
    const q = e.optional ? '?' : '';
    if (c.type === 'MemberExpression' && !c.computed) {
      const o = c.object;
      const name = c.property.name;
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
      const value = p.shorthand ? ex(p.key) : p.value.type === 'ArrowFunctionExpression' || p.value.type === 'FunctionExpression' ? lambda(p.value) : ex(p.value);
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
    return parts.length ? `new() { ${parts.join(', ')} }` : 'new()';
  }

  function param(p, defaults) {
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
    } else return `/*TODO param pattern*/object _p${tmp++}`;
    hint = pascal(curFn) + pascal(name);
    let cs = t ? ty(t, mod, name) : def ? (def.type === 'Literal' && typeof def.value === 'number' ? (Number.isInteger(def.value) && INT_NAMES.has(name) ? 'int' : 'double') : def.type === 'Literal' && typeof def.value === 'string' ? 'string' : def.type === 'Literal' && typeof def.value === 'boolean' ? 'bool' : null) : null;
    if (!cs && def && ((def.type === 'Identifier' && /^[A-Z_0-9]+$/.test(def.name)) || def.type === 'BinaryExpression' || (def.type === 'MemberExpression' && /^[A-Z_0-9]+$/.test(def.property?.name ?? '')))) cs = 'double';
    if (!cs) cs = '/*TODO param type*/object';
    if (p.optional && !cs.endsWith('?')) cs += '?';
    if (def) {
      const literal = def.type === 'Literal' || (def.type === 'Identifier' && def.name === 'Infinity') || (def.type === 'UnaryExpression' && def.argument.type === 'Literal');
      if (literal) return `${cs} ${id(name)} = ${ex(def)}`;
      defaults.push(`${id(name)} ??= ${ex(def)};`);
      return `${cs.endsWith('?') ? cs : cs + '?'} ${id(name)} = null`;
    }
    if (p.optional) return `${cs} ${id(name)} = default`;
    return `${cs} ${id(name)}`;
  }

  function lambda(fn) {
    const defaults = [];
    const typed = fn.params.every((p) => p.type === 'Identifier' && p.typeAnnotation);
    const ps = fn.params.map((p) => (typed ? param(p, defaults) : p.type === 'Identifier' ? id(p.name) : p.type === 'AssignmentPattern' ? id(p.left.name) : `/*TODO pattern*/_${tmp++}`));
    const head = `(${ps.join(', ')})`;
    // A predicate's body is a condition.
    if (fn.body.type !== 'BlockStatement') {
      const parent = fn.__pred;
      return `${head} => ${ex(fn.body, !!parent)}`;
    }
    return `${head} =>\n${block(fn.body, '            ', { fnRet: fn.returnType ? ty(fn.returnType.typeAnnotation, mod) : null })}`;
  }

  // Statements.
  function block(b, ind, ctx) {
    const lines = [`${ind.slice(4)}{`];
    for (const s of b.body) lines.push(...stmt(s, ind, ctx));
    lines.push(`${ind.slice(4)}}`);
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
            const names = d.id.elements.map((x) => (x ? id(x.name) : '_'));
            lines.push(`${ind}var (${names.join(', ')}) = ${ex(d.init)};`);
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
              lines.push(`${ind}${ret} ${id(d.id.name)}(${ps.join(', ')})`);
              lines.push(bodyText.split('\n').map((l, i) => (i === 0 && fn.body.type === 'BlockStatement' ? ind + l.trimStart() : l)).join('\n'));
              continue;
            }
          }
          const init = d.init ? ` = ${ex(d.init, t === 'bool')}` : t.endsWith('?') ? ' = null' : ' = default!';
          lines.push(`${ind}${t} ${id(d.id.name)}${init};`);
        }
        return lines;
      }
      case 'ReturnStatement': return [`${ind}return${s.argument ? ' ' + ex(s.argument, ctx.fnRet === 'bool') : ''};`];
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
        let init = '';
        if (s.init?.type === 'VariableDeclaration') {
          init = s.init.declarations.map((d, i) => {
            const t = d.id.typeAnnotation ? ty(d.id.typeAnnotation.typeAnnotation, mod, d.id.name) : 'var';
            return `${i === 0 ? t + ' ' : ''}${id(d.id.name)} = ${ex(d.init)}`;
          }).join(', ');
        } else if (s.init) init = ex(s.init);
        const update = s.update ? (s.update.type === 'SequenceExpression' ? s.update.expressions.map((x) => ex(x)).join(', ') : ex(s.update)) : '';
        return [`${ind}for (${init}; ${s.test ? ex(s.test, true) : ''}; ${update})`, body(s.body, ind, ctx)];
      }
      case 'ForOfStatement': {
        const left = s.left.type === 'VariableDeclaration' ? s.left.declarations[0].id : s.left;
        const v = left.type === 'ArrayPattern' ? `var (${left.elements.map((x) => (x ? id(x.name) : '_')).join(', ')})` : left.type === 'ObjectPattern' ? `var __o${tmp}` : `var ${id(left.name)}`;
        const lines = [`${ind}foreach (${v} in Js.Iter(${ex(s.right)}))`];
        if (left.type === 'ObjectPattern') {
          const t = `__o${tmp++}`;
          const inner = left.properties.map((p) => `${ind}    var ${id(p.value.name ?? p.key.name)} = ${t}.${id(p.key.name)};`);
          const b = body(s.body, ind, ctx).split('\n');
          b.splice(1, 0, ...inner);
          lines.push(b.join('\n'));
          return lines;
        }
        lines.push(body(s.body, ind, ctx));
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
      return '/*TODO return type*/object';
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
    return '/*TODO return type*/object';
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
    const ps = f.params.map((p) => param(p, defaults));
    hint = pascal(name) + 'Result';
    const ret = f.returnType ? ty(f.returnType.typeAnnotation, mod) : guessReturn(f);
    const head = `${ind}${top ? 'public static ' : ''}${ret} ${id(name)}${tparams}(${ps.join(', ')})${where}`;
    const b = block(f.body, ind + '    ', { fnRet: ret }).split('\n');
    b[0] = ind + b[0].trimStart();
    if (defaults.length) b.splice(1, 0, ...defaults.map((d) => `${ind}    ${d}`));
    b[b.length - 1] = ind + b[b.length - 1].trimStart();
    curFn = outerFn;
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
            t = kinds.size === 1 && kinds.has('string') ? 'List<string>' : kinds.size === 1 && kinds.has('number') ? 'List<double>' : '/*TODO array type*/List<object>';
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
          initText = `new() { ${init.properties.map((p) => (p.type === 'Property' ? `[${str(String(p.key.name ?? p.key.value))}] = ${ex(p.value)}` : '/*TODO spread*/')).join(', ')} }`;
        }
        if (t === 'var') t = '/*TODO const type*/object';
        const isConst = d.kind === 'const' && ['int', 'double', 'string', 'bool'].includes(t) && init && (init.type === 'Literal' || init.type === 'TemplateLiteral' && init.expressions.length === 0 || (init.type === 'UnaryExpression' && init.argument.type === 'Literal'));
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
    let c = ty(m.typeAnnotation?.typeAnnotation, mod, f) ?? 'object';
    if (m.optional && !c.endsWith('?')) c += '?';
    inl.push(`    public ${c} ${id(String(f))} { get; set; }`);
  }
  inl.push('}', '');
}
writeFileSync(join(OUT, 'Inline.g.cs'), inl.join('\n'));
console.log(`translated ${only.length || files.length - Object.keys(HAND).length} modules; ${todos} TODO markers`);
