// graphql-js validity oracle. Reads a JSONL corpus (one JSON-encoded GraphQL
// operation string per line) and validates each against schema.graphql using the
// spec rule set. Output: one JSON object per line {i, parseError, valid,
// errorCount, messages}. This is the "passes validation" side of the differential.
import { readFileSync } from 'node:fs';
import { buildSchema, parse, validate, specifiedRules } from 'graphql';

const [schemaPath, corpusPath] = process.argv.slice(2);
if (!schemaPath || !corpusPath) {
  console.error('usage: node node-runner.mjs <schema.graphql> <corpus.jsonl>');
  process.exit(2);
}

const schema = buildSchema(readFileSync(schemaPath, 'utf8'));
const lines = readFileSync(corpusPath, 'utf8').split('\n').filter(l => l.trim().length > 0);

lines.forEach((line, i) => {
  let query;
  try {
    query = JSON.parse(line);
  } catch {
    query = line; // tolerate bare (non-JSON) lines
  }

  let out;
  try {
    const doc = parse(query, { noLocation: true });
    const errors = validate(schema, doc, specifiedRules);
    out = {
      i,
      parseError: null,
      valid: errors.length === 0,
      errorCount: errors.length,
      messages: errors.map(e => e.message),
    };
  } catch (ex) {
    out = { i, parseError: String(ex.message ?? ex), valid: false, errorCount: 0, messages: [] };
  }
  process.stdout.write(JSON.stringify(out) + '\n');
});
