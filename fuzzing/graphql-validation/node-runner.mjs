// graphql-js (reference) validation runner.
// Usage: node node-runner.mjs <schema.graphql> <corpus.jsonl> > node-results.jsonl
// Input : corpus.jsonl, one JSON-encoded query string per line.
// Output: one JSON record per line, aligned by index `i`:
//         { i, parseError, valid, errorCount, messages: [..] }
import { readFileSync } from 'node:fs';
import { buildSchema, parse, validate, specifiedRules } from 'graphql';

const [schemaPath, corpusPath] = process.argv.slice(2);
if (!schemaPath || !corpusPath) {
  console.error('usage: node node-runner.mjs <schema.graphql> <corpus.jsonl>');
  process.exit(2);
}

const schema = buildSchema(readFileSync(schemaPath, 'utf8'));
const lines = readFileSync(corpusPath, 'utf8').split('\n').filter((l) => l.trim().length > 0);

const results = lines.map((line, i) => {
  const rec = { i, parseError: false, valid: true, errorCount: 0, messages: [] };
  let query;
  try {
    query = JSON.parse(line);
  } catch {
    rec.parseError = true;
    rec.valid = false;
    rec.messages = ['CORPUS_LINE_NOT_JSON'];
    return rec;
  }

  let doc;
  try {
    doc = parse(query);
  } catch (e) {
    rec.parseError = true;
    rec.valid = false;
    rec.messages = [String(e.message)];
    return rec;
  }

  let errors;
  try {
    errors = validate(schema, doc, specifiedRules);
  } catch (e) {
    // validate throws e.g. on too many errors; treat as invalid.
    rec.valid = false;
    rec.errorCount = -1;
    rec.messages = ['VALIDATE_THREW: ' + e.message];
    return rec;
  }

  rec.errorCount = errors.length;
  rec.valid = errors.length === 0;
  rec.messages = errors.slice(0, 5).map((er) => er.message);
  return rec;
});

process.stdout.write(results.map((r) => JSON.stringify(r)).join('\n') + '\n');
