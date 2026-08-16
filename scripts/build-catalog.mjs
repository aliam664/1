#!/usr/bin/env node
/**
 * Validate every database/*.json file and emit dist/catalog.json + dist/manifest.json.
 * This is what the GitHub Action should run — keep the Action thin.
 */
import { createHash } from 'node:crypto';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateCatalogItem } from '../src/main/catalog/validateItem.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DB = path.join(ROOT, 'database');
const OUT = path.join(ROOT, 'dist');

function readJson(file) {
  return JSON.parse(readFileSync(file, 'utf8'));
}

function collect(dir, expectedType) {
  const folder = path.join(DB, dir);
  const files = readdirSync(folder).filter((name) => name.endsWith('.json'));
  const items = [];
  for (const name of files) {
    const file = path.join(folder, name);
    const raw = readJson(file);
    const result = validateCatalogItem(raw, `${dir}/${name}`);
    if (!result.ok) {
      throw new Error(result.error);
    }
    if (result.value.type !== expectedType) {
      throw new Error(`${dir}/${name}: type must be ${expectedType}`);
    }
    if (!result.value.downloadUrl) {
      throw new Error(`${dir}/${name}: empty downloadUrl`);
    }
    items.push(result.value);
  }
  return items;
}

const cars = collect('cars', 'car');
const tracks = collect('tracks', 'track');
const mods = collect('mods', 'mod');
const items = [...cars, ...tracks, ...mods];
const ids = new Set();
for (const item of items) {
  if (ids.has(item.id)) {
    throw new Error(`duplicate id ${item.id}`);
  }
  ids.add(item.id);
}

const categories = readJson(path.join(DB, 'categories.json'));
const featuredDoc = readJson(path.join(DB, 'featured.json'));
for (const id of featuredDoc.featured || []) {
  if (!ids.has(id)) {
    throw new Error(`featured id not found: ${id}`);
  }
}

const catalog = {
  catalogVersion: new Date().toISOString().slice(0, 10).replace(/-/g, '.'),
  generatedAt: new Date().toISOString(),
  categories: (categories.categories || []).map((c) => c.id),
  featured: featuredDoc.featured || [],
  items
};

mkdirSync(OUT, { recursive: true });
const catalogJson = `${JSON.stringify(catalog, null, 2)}\n`;
writeFileSync(path.join(OUT, 'catalog.json'), catalogJson);
const sha256 = createHash('sha256').update(catalogJson).digest('hex');
const manifest = {
  catalogVersion: catalog.catalogVersion,
  generatedAt: catalog.generatedAt,
  itemCount: items.length,
  sha256,
  catalogUrl: 'dist/catalog.json'
};
writeFileSync(path.join(OUT, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);

const demo = {
  catalogVersion: catalog.catalogVersion,
  generatedAt: catalog.generatedAt,
  categories: catalog.categories,
  featured: catalog.featured,
  items
};
writeFileSync(
  path.join(ROOT, 'src/renderer/services/demo-catalog.json'),
  `${JSON.stringify(demo, null, 2)}\n`
);

process.stdout.write(`catalog: ${items.length} items, sha256=${sha256}\n`);
