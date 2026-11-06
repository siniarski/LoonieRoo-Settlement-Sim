// Generates cs/Sim/State.g.cs: C# classes for the sim state's types, read from the TypeScript (src/sim), so
// the C# state has exactly the TypeScript's fields and reads and writes the same JSON.
// Run: node cs/tools/gen-state.mjs   (again whenever a state type changes)
//
// Parses with rolldown's parser (syntax only; this project's TypeScript 7 has no JavaScript API) and
// resolves type names itself over every interface and type alias in src/sim.
// Mapping: interfaces and object types -> classes with public fields (names as in TypeScript); a union of
// string literals -> string; a union of object shapes (Command, Order...) -> one class with every member's
// fields (all optional); Record<K, V> -> Dictionary<string, V>; T[] -> List<T>; tuples -> List<double>;
// optional or `| null` -> nullable; number -> double (JavaScript's number) except the counters in INTS.
import { parseAst } from 'rolldown/parseAst';
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const SIM = 'src/sim';
/** Hand-written in C# (Grid.cs): not generated. */
const HAND = new Set(['Point', 'Grid']);
/** Whole-number fields kept as int (Class.field): counters and the tick. Everything else numeric is a double. */
const INTS = new Set(['SimState.tick', 'SimState.walkabilityVersion', 'SimState.nextId', 'SimState.namesGiven', 'SimState.schemaVersion', 'LoggedCommand.tick']);

// Every interface and type alias in the sim, by name.
const decls = new Map();
for (const f of readdirSync(SIM).filter((f) => f.endsWith('.ts'))) {
  const src = readFileSync(join(SIM, f), 'utf8');
  const ast = parseAst(src, { lang: 'ts' }, f);
  const visit = (n) => {
    if (!n) return;
    const d = n.type === 'ExportNamedDeclaration' ? n.declaration : n;
    if (!d) return;
    if (d.type === 'TSInterfaceDeclaration' || d.type === 'TSTypeAliasDeclaration') {
      const leading = src.slice(0, d.start).match(/\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*(export\s+)?$/);
      decls.set(d.id.name, { node: d, file: f, src, doc: leading ? clean(leading[1]) : '' });
    }
  };
  for (const n of ast.body) visit(n);
}

function clean(doc) {
  return doc.split('\n').map((l) => l.replace(/^\s*\*\s?/, '').trim()).filter(Boolean).join(' ');
}

const classes = new Map(); // name -> Map(field -> { type, doc })
const queue = [];

const pascal = (s) => s.replace(/(^|[_\s-])(\w)/g, (_, __, c) => c.toUpperCase()).replace(/^\w/, (c) => c.toUpperCase());

/** What kind of thing a TS type node is, after resolving names: 'string' | 'number' | 'boolean' | 'null' | 'object' | 'array' | 'record' | 'other'. */
function kindOf(t, seen = new Set()) {
  if (!t) return 'other';
  switch (t.type) {
    case 'TSStringKeyword': case 'TSTemplateLiteralType': return 'string';
    case 'TSNumberKeyword': return 'number';
    case 'TSBooleanKeyword': return 'boolean';
    case 'TSNullKeyword': case 'TSUndefinedKeyword': return 'null';
    case 'TSLiteralType': {
      const v = t.literal.value;
      return typeof v === 'string' ? 'string' : typeof v === 'number' ? 'number' : typeof v === 'boolean' ? 'boolean' : 'other';
    }
    case 'TSTypeLiteral': return 'object';
    case 'TSArrayType': case 'TSTupleType': return 'array';
    case 'TSTypeOperator': return kindOf(t.typeAnnotation, seen);
    case 'TSParenthesizedType': return kindOf(t.typeAnnotation, seen);
    case 'TSIndexedAccessType': return 'string'; // (typeof LOOKS)[number] and the like: the const arrays are strings
    case 'TSTypeQuery': return 'other';
    case 'TSFunctionType': return 'function';
    case 'TSUnionType': {
      const ks = new Set(t.types.map((u) => kindOf(u, seen)).filter((k) => k !== 'null'));
      return ks.size === 1 ? [...ks][0] : 'mixed';
    }
    case 'TSIntersectionType': return 'object';
    case 'TSTypeReference': {
      const name = refName(t);
      if (name === 'Record') return 'record';
      if (name === 'Array' || name === 'ReadonlyArray') return 'array';
      if (['Partial', 'Required', 'Readonly'].includes(name)) return kindOf(t.typeArguments.params[0], seen);
      if (['Pick', 'Omit'].includes(name)) return 'object';
      if (name === 'Exclude' || name === 'Extract') return kindOf(t.typeArguments?.params[0], seen);
      if (HAND.has(name)) return 'object';
      const d = decls.get(name);
      if (!d || seen.has(name)) return 'other';
      seen.add(name);
      return d.node.type === 'TSInterfaceDeclaration' ? 'object' : kindOf(d.node.typeAnnotation, seen);
    }
    case 'TSTypeOperatorType': return 'string';
    default: return 'other';
  }
}

function refName(t) {
  return t.typeName.type === 'Identifier' ? t.typeName.name : t.typeName.right.name;
}

function nullable(t) {
  if (t.type === 'TSUnionType') return t.types.some((u) => u.type === 'TSNullKeyword' || u.type === 'TSUndefinedKeyword');
  return false;
}

/** The object-type nodes a type stands for (members of a union, the parts of an intersection, an interface). */
function objectParts(t) {
  if (!t) return [];
  switch (t.type) {
    case 'TSTypeLiteral': return [{ members: t.body ?? t.members, base: null }];
    case 'TSParenthesizedType': return objectParts(t.typeAnnotation);
    case 'TSUnionType': return t.types.flatMap(objectParts);
    case 'TSIntersectionType': return t.types.flatMap(objectParts);
    case 'TSTypeReference': {
      const name = refName(t);
      if (['Partial', 'Required', 'Readonly'].includes(name)) return objectParts(t.typeArguments.params[0]);
      if (name === 'Omit' || name === 'Pick') return objectParts(t.typeArguments.params[0]);
      const d = decls.get(name);
      if (!d) return [];
      if (d.node.type === 'TSInterfaceDeclaration') {
        const own = [{ members: d.node.body.body, base: null }];
        const ext = (d.node.extends ?? []).flatMap((h) => objectParts({ type: 'TSTypeReference', typeName: h.expression }));
        return [...ext, ...own];
      }
      return objectParts(d.node.typeAnnotation);
    }
    default: return [];
  }
}

/** The C# type for a TS type node, naming inline object types after `hint`. */
function csType(t, hint, intField) {
  const q = nullable(t) ? '?' : '';
  let core = t;
  if (t.type === 'TSUnionType') {
    const rest = t.types.filter((u) => u.type !== 'TSNullKeyword' && u.type !== 'TSUndefinedKeyword');
    core = rest.length === 1 ? rest[0] : { ...t, types: rest };
  }
  const k = kindOf(core);
  switch (k) {
    case 'string': return 'string' + q;
    case 'number': return (intField ? 'int' : 'double') + q;
    case 'boolean': return 'bool' + q;
    case 'array': {
      let c = deref(core);
      c = c.type === 'TSTypeOperator' ? c.typeAnnotation : c;
      if (c.type === 'TSTupleType') {
        const ks = new Set(c.elementTypes.map((e) => kindOf(e)));
        return (ks.size === 1 && ks.has('number') ? 'List<double>' : ks.size === 1 && ks.has('string') ? 'List<string>' : 'List<System.Text.Json.Nodes.JsonNode>') + q;
      }
      const el = c.type === 'TSArrayType' ? c.elementType : c.typeArguments.params[0];
      return `List<${csType(el, hint + 'Item', false).replace(/\?$/, '')}>` + q;
    }
    case 'record': {
      const r = deref(core);
      return `Dictionary<string, ${csType(r.typeArguments.params[1], hint + 'Value', false)}>` + q;
    }
    case 'object': {
      // A Record by another name (Priorities = Record<JobType, Priority>).
      if (core.type === 'TSTypeReference') {
        const d = decls.get(refName(core));
        if (d && d.node.type === 'TSTypeAliasDeclaration' && kindOf(d.node.typeAnnotation) === 'record') return csType(d.node.typeAnnotation, hint, false) + q;
        if (HAND.has(refName(core))) return refName(core) + q;
      }
      return classFor(core, hint) + q;
    }
    case 'mixed': return 'System.Text.Json.Nodes.JsonNode' + q;
    default: return 'System.Text.Json.Nodes.JsonNode' + q;
  }
}

/** Follows type aliases (not interfaces or Record/Array) to what they stand for. */
function deref(t) {
  for (let k = 0; k < 20 && t.type === 'TSTypeReference'; k++) {
    const name = refName(t);
    if (['Record', 'Array', 'ReadonlyArray'].includes(name)) return t;
    if (['Partial', 'Required', 'Readonly'].includes(name)) { t = t.typeArguments.params[0]; continue; }
    if (name === 'Exclude' || name === 'Extract') { t = t.typeArguments.params[0]; continue; }
    const d = decls.get(name);
    if (!d || d.node.type !== 'TSTypeAliasDeclaration') return t;
    t = d.node.typeAnnotation;
  }
  return t;
}

/** The class for an object type: an interface or alias by its name, an inline type by the hint. */
function classFor(t, hint) {
  let name = hint;
  if (t.type === 'TSTypeReference' && ['Partial', 'Required', 'Readonly', 'Pick', 'Omit'].includes(refName(t))) return classFor(t.typeArguments.params[0], hint);
  if (t.type === 'TSTypeReference') name = refName(t);
  if (HAND.has(name)) return name;
  if (!classes.has(name)) {
    classes.set(name, null);
    queue.push([name, t]);
  }
  return name;
}

function build(name, t) {
  const fields = new Map();
  const parts = objectParts(t);
  const union = parts.length > 1 && (t.type === 'TSUnionType' || (t.type === 'TSTypeReference' && decls.get(refName(t))?.node.typeAnnotation?.type === 'TSUnionType'));
  for (const part of parts) {
    for (const m of part.members) {
      if (m.type !== 'TSPropertySignature' || !m.typeAnnotation) continue;
      const field = m.key.name ?? m.key.value;
      const intField = INTS.has(`${name}.${field}`);
      let type = csType(m.typeAnnotation.typeAnnotation, name + pascal(field), intField);
      if ((m.optional || union) && !type.endsWith('?')) type += '?';
      const prev = fields.get(field);
      if (prev) {
        if (prev.type !== type) prev.type = prev.type.replace(/\?$/, '') === type.replace(/\?$/, '') ? prev.type.replace(/\?$/, '') + '?' : 'System.Text.Json.Nodes.JsonNode?';
        continue;
      }
      const decl = [...decls.values()].find((d) => d.node.start <= m.start && m.end <= d.node.end);
      const before = decl ? decl.src.slice(0, m.start).match(/\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*$/) : null;
      fields.set(field, { type, doc: before ? clean(before[1]) : '' });
    }
  }
  classes.set(name, fields);
}

const roots = [['SimState'], ['Command'], ['LoggedCommand']];
for (const [name] of roots) classFor({ type: 'TSTypeReference', typeName: { type: 'Identifier', name } }, name);
while (queue.length) {
  const [name, t] = queue.shift();
  build(name, t);
}

const kw = new Set(['base', 'checked', 'class', 'default', 'event', 'fixed', 'lock', 'object', 'operator', 'out', 'override', 'params', 'ref', 'string', 'this', 'internal', 'is', 'as', 'new', 'in', 'readonly', 'static', 'var', 'void', 'from', 'where', 'select', 'delegate', 'goto', 'implicit', 'explicit', 'namespace', 'sizeof', 'struct', 'typeof', 'unchecked', 'using', 'virtual', 'volatile', 'while', 'abstract', 'case', 'catch', 'char', 'const', 'continue', 'decimal', 'do', 'double', 'else', 'enum', 'extern', 'false', 'finally', 'float', 'for', 'foreach', 'if', 'int', 'interface', 'long', 'null', 'private', 'protected', 'public', 'return', 'sbyte', 'sealed', 'short', 'stackalloc', 'switch', 'throw', 'true', 'try', 'uint', 'ulong', 'ushort', 'byte', 'bool', 'break']);
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const lines = [
  "// <auto-generated> by cs/tools/gen-state.mjs from the TypeScript sim (src/sim). Don't edit: run it again.",
  "// The sim state's types, field for field, so the C# state reads and writes the same JSON as the TypeScript saves.",
  '#nullable enable',
  'namespace LoonieRoo.Sim;',
  '',
];
for (const [name, fields] of [...classes].sort((a, b) => a[0].localeCompare(b[0]))) {
  const d = decls.get(name);
  if (d?.doc) lines.push(`/// <summary>${esc(d.doc)}</summary>`);
  lines.push(`public partial class ${name}`, '{');
  for (const [field, f] of fields) {
    if (f.doc) lines.push(`    /// <summary>${esc(f.doc)}</summary>`);
    const id = kw.has(field) ? '@' + field : field;
    const t = f.type;
    const init = t.endsWith('?') || ['double', 'int', 'bool'].includes(t) ? ';' : t === 'string' ? ' = "";' : ' = new();';
    lines.push(`    public ${t} ${id}${init}`);
  }
  lines.push('}', '');
}
writeFileSync('cs/Sim/State.g.cs', lines.join('\n'));
console.log(`${classes.size} classes`);
