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
/** Field names that are always whole tile numbers (coordinates and sizes), in any class: int. */
const INT_FIELDS = new Set(['x', 'y', 'x0', 'y0', 'x1', 'y1', 'w', 'h', 'width', 'height', 'ox', 'oy', 'cx', 'cy', 'u', 'v', 'ax', 'ay', 'bx', 'by']);

// Every interface and type alias in the sim, by name (a name declared in two modules is kept per module,
// and gets its module's name in front in C#: csNames, written to cs/tools/typenames.json for the translator).
const decls = new Map();
const byFile = new Map(); // file -> Map(name -> decl)
const seenNames = new Map(); // name -> [files]
for (const f of readdirSync(SIM).filter((f) => f.endsWith('.ts'))) {
  const src = readFileSync(join(SIM, f), 'utf8');
  const ast = parseAst(src, { lang: 'ts' }, f);
  const visit = (n) => {
    if (!n) return;
    const d = n.type === 'ExportNamedDeclaration' ? n.declaration : n;
    if (!d) return;
    if (d.type === 'TSInterfaceDeclaration' || d.type === 'TSTypeAliasDeclaration') {
      const leading = src.slice(0, d.start).match(/\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*(export\s+)?$/);
      const entry = { node: d, file: f, src, doc: leading ? clean(leading[1]) : '' };
      if (!byFile.has(f)) byFile.set(f, new Map());
      byFile.get(f).set(d.id.name, entry);
      seenNames.set(d.id.name, [...(seenNames.get(d.id.name) ?? []), f]);
      decls.set(d.id.name, entry);
    }
  };
  for (const n of ast.body) visit(n);
}

const pascalFile = (f) => f.replace(/\.ts$/, '').replace(/^\w/, (c) => c.toUpperCase());
/** The C# class name for a type declared in a file. */
function csName(name, file) {
  return (seenNames.get(name)?.length ?? 0) > 1 ? pascalFile(file) + name : name;
}
/** The declaration a name means inside a file: its own first, else the one the file imports, else any. */
function lookup(name, file) {
  const own = byFile.get(file)?.get(name);
  if (own) return own;
  const src = file ? readFileSync(join(SIM, file), 'utf8') : '';
  const m = src.match(new RegExp("import[^;]*\\b" + name + "\\b[^;]*from '\\./(\\w+)'"));
  if (m) return byFile.get(m[1] + '.ts')?.get(name) ?? decls.get(name);
  return decls.get(name);
}

function clean(doc) {
  return doc.split('\n').map((l) => l.replace(/^\s*\*\s?/, '').trim()).filter(Boolean).join(' ');
}

const classes = new Map(); // C# name -> Map(field -> { type, doc })
const classFile = new Map(); // C# name -> the file it was declared in
const queue = [];
/** The file whose types are being resolved (a name means what it means there). */
let currentFile = null;

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
      if (['Int32Array', 'Uint32Array', 'Float64Array', 'Uint8Array', 'Int8Array', 'Uint16Array', 'Float32Array'].includes(name)) return 'typed';
      if (name === 'Map' || name === 'WeakMap') return 'map';
      if (name === 'Set') return 'set';
      if (name === 'Array' || name === 'ReadonlyArray') return 'array';
      if (['Partial', 'Required', 'Readonly'].includes(name)) return kindOf(t.typeArguments.params[0], seen);
      if (['Pick', 'Omit'].includes(name)) return 'object';
      if (name === 'Exclude' || name === 'Extract') return kindOf(t.typeArguments?.params[0], seen);
      if (HAND.has(name)) return 'object';
      const d = lookup(name, currentFile);
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
      const d = lookup(name, currentFile);
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
      return `JsRecord<${csType(r.typeArguments.params[1], hint + 'Value', false)}>` + q;
    }
    case 'object': {
      // A Record by another name (Priorities = Record<JobType, Priority>).
      if (core.type === 'TSTypeReference') {
        const d = lookup(refName(core), currentFile);
        if (d && d.node.type === 'TSTypeAliasDeclaration' && kindOf(d.node.typeAnnotation) === 'record') return csType(d.node.typeAnnotation, hint, false) + q;
        if (HAND.has(refName(core))) return refName(core) + q;
      }
      return classFor(core, hint) + q;
    }
    case 'typed': return { Int32Array: 'int[]', Uint32Array: 'uint[]', Float64Array: 'double[]', Uint8Array: 'byte[]', Int8Array: 'sbyte[]', Uint16Array: 'ushort[]', Float32Array: 'float[]' }[refName(core)] + q;
    case 'map': return `JsMap<${csType(core.typeArguments.params[0], hint + 'Key', false).replace(/\?$/, '')}, ${csType(core.typeArguments.params[1], hint + 'Value', false)}>` + q;
    case 'set': return `JsSet<${csType(core.typeArguments.params[0], hint + 'Item', false).replace(/\?$/, '')}>` + q;
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
    const d = lookup(name, currentFile);
    if (!d || d.node.type !== 'TSTypeAliasDeclaration') return t;
    t = d.node.typeAnnotation;
  }
  return t;
}

/** The class for an object type: an interface or alias by its name, an inline type by the hint. */
function classFor(t, hint) {
  let name = hint;
  if (t.type === 'TSTypeReference' && ['Partial', 'Required', 'Readonly', 'Pick', 'Omit'].includes(refName(t))) return classFor(t.typeArguments.params[0], hint);
  let file = currentFile;
  if (t.type === 'TSTypeReference') {
    name = refName(t);
    if (HAND.has(name)) return name;
    const d = lookup(name, currentFile);
    if (d) {
      file = d.file;
      name = csName(name, d.file);
    }
  }
  if (!classes.has(name)) {
    classes.set(name, null);
    classFile.set(name, file);
    queue.push([name, t, file]);
  }
  return name;
}

function build(name, t, file) {
  currentFile = file;
  const fields = new Map();
  const parts = objectParts(t);
  const union = parts.length > 1 && (t.type === 'TSUnionType' || (t.type === 'TSTypeReference' && lookup(refName(t), currentFile)?.node.typeAnnotation?.type === 'TSUnionType'));
  for (const part of parts) {
    for (const m of part.members) {
      if (m.type !== 'TSPropertySignature' || !m.typeAnnotation) continue;
      const field = m.key.name ?? m.key.value;
      const intField = INTS.has(`${name}.${field}`) || INT_FIELDS.has(field);
      let type = csType(m.typeAnnotation.typeAnnotation, name + pascal(field), intField);
      const explicitNull = type.endsWith('?');
      // A union member's fields are there when its type says so (TypeScript narrows on it): plain values, unless optional.
      if (m.optional && !type.endsWith('?')) type += '?';
      const prev = fields.get(field);
      if (prev) {
        // Union members: optional only if every member that has it leaves it optional (a command's x is there
        // whenever its type says so: TypeScript narrows on the type, C# can't).
        const a = prev.type.replace(/\?$/, '');
        const b = type.replace(/\?$/, '');
        if (a !== b) prev.type = 'System.Text.Json.Nodes.JsonNode?';
        else if (union) {
          prev.explicitNull ||= explicitNull;
          prev.type = prev.explicitNull ? a + '?' : a;
        }
        continue;
      }
      const decl = [...decls.values()].find((d) => d.node.start <= m.start && m.end <= d.node.end);
      const before = decl ? decl.src.slice(0, m.start).match(/\/\*\*((?:(?!\*\/)[\s\S])*)\*\/\s*$/) : null;
      fields.set(field, { type: union && !explicitNull ? type.replace(/\?$/, '') : type, explicitNull, doc: before ? clean(before[1]) : '' });
    }
  }
  classes.set(name, fields);
}

// Every interface, and every alias for an object type or a union of them, in the sim: function signatures need them all.
for (const [file, names] of byFile) {
  for (const [name, d] of names) {
    currentFile = file;
    if (d.node.type === 'TSInterfaceDeclaration' || (d.node.typeAnnotation && kindOf(d.node.typeAnnotation) === 'object' && !d.node.typeParameters)) classFor({ type: 'TSTypeReference', typeName: { type: 'Identifier', name } }, name);
  }
}
while (queue.length) {
  const [name, t, file] = queue.shift();
  build(name, t, file);
}
// For the translator: what each file calls each type it can see, where that isn't the plain name.
const renames = {};
for (const [file, names] of byFile) for (const name of names.keys()) if (csName(name, file) !== name) (renames[file] ??= {})[name] = csName(name, file);
writeFileSync('cs/tools/typenames.json', JSON.stringify(renames, null, 1));

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
  const d = byFile.get(classFile.get(name))?.get(name.replace(/^[A-Z][a-z]+(?=[A-Z])/, (m) => (byFile.get(classFile.get(name))?.has(name) ? m : ''))) ?? decls.get(name);
  if (d?.doc) lines.push(`/// <summary>${esc(d.doc)}</summary>`);
  const ifaces = [];
  if (fields.get('id')?.type === 'string') ifaces.push('IHasId');
  if (fields.get('at')?.type === 'Point') ifaces.push('IHasAt');
  if (fields.get('pos')?.type === 'Point') ifaces.push('IHasPos');
  // A body (survivor, zombie, animal, dog, resident...): an id, a tile and a precise position.
  if (ifaces.includes('IHasId') && ifaces.includes('IHasAt') && ifaces.includes('IHasPos')) ifaces.push('IBody');
  lines.push(`public partial class ${name}${ifaces.length ? ' : ' + ifaces.join(', ') : ''}`, '{');
  if (fields.get('x')?.type === 'int' && fields.get('y')?.type === 'int') lines.push(`    /// <summary>Anything with x and y stands for a point, as it does in the TypeScript (structural typing).</summary>`, `    public static implicit operator Point(${name} v) => new(v.x, v.y);`);
  for (const [field, f] of fields) {
    if (f.doc) lines.push(`    /// <summary>${esc(f.doc)}</summary>`);
    const id = kw.has(field) ? '@' + field : field;
    const t = f.type;
    const init = t.endsWith('?') || ['double', 'int', 'bool'].includes(t) || t.includes('JsonNode') || t.endsWith('[]') ? (t.includes('JsonNode') || t.endsWith('[]')) && !t.endsWith('?') ? ' = null!;' : ';' : t === 'string' ? ' = "";' : ' = new();';
    lines.push(`    public ${t} ${id} { get; set; }${init === ';' ? '' : init}`);
  }
  lines.push('}', '');
}
writeFileSync('cs/Sim/State.g.cs', lines.join('\n'));
console.log(`${classes.size} classes`);
