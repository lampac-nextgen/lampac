import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "../..");
const roles = [
  "lampac-admin",
  "lampac-clients",
  "lampac-community",
  "lampac-core",
  "lampac-deploy",
  "lampac-jacred",
  "lampac-media",
  "lampac-modules",
  "lampac-music",
  "lampac-online",
  "lampac-shared",
  "lampac-sisi",
  "lampac-sync",
  "mintlify-docsops",
];
const headings = [
  "## Triggers",
  "## Inputs",
  "## Allowed tools",
  "## Working directories",
  "## Deliverables",
  "## Does not own",
];
const errors = [];

function listMd(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir).filter((name) => name.endsWith(".md")).sort();
}

const roleFiles = listMd(join(root, ".agents/agents"));
const cursorFiles = listMd(join(root, ".cursor/agents"));
const expected = roles.map((name) => `${name}.md`);

if (roleFiles.join() !== expected.join()) {
  errors.push(`role cards ${roleFiles.join(", ") || "(none)"} != ${expected.join(", ")}`);
}
if (cursorFiles.join() !== expected.join()) {
  errors.push(`cursor agents ${cursorFiles.join(", ") || "(none)"} != ${expected.join(", ")}`);
}

for (const name of roles) {
  const cardPath = join(root, ".agents/agents", `${name}.md`);
  const cursorPath = join(root, ".cursor/agents", `${name}.md`);
  if (existsSync(cardPath)) {
    const card = readFileSync(cardPath, "utf8");
    for (const heading of headings) {
      if (!card.includes(heading)) errors.push(`${name} role card missing ${heading}`);
    }
  }
  if (existsSync(cursorPath)) {
    const cursor = readFileSync(cursorPath, "utf8");
    if (!cursor.includes(`.agents/agents/${name}.md`)) {
      errors.push(`${name} cursor agent does not point at its role card`);
    }
    if (!/^name:\s*\S+/m.test(cursor) || !/^description:/m.test(cursor)) {
      errors.push(`${name} cursor agent missing name or description`);
    }
  }
}

const skillRoot = join(root, ".agents/skills");
for (const dir of readdirSync(skillRoot).sort()) {
  const folder = join(skillRoot, dir);
  if (!statSync(folder).isDirectory()) continue;
  const skillPath = join(folder, "SKILL.md");
  if (!existsSync(skillPath)) {
    errors.push(`${dir} missing SKILL.md`);
    continue;
  }
  const text = readFileSync(skillPath, "utf8");
  const front = text.match(/^---\n([\s\S]*?)\n---/);
  if (!front) {
    errors.push(`${dir} SKILL.md missing frontmatter`);
    continue;
  }
  if (!/^name:\s*\S+/m.test(front[1])) errors.push(`${dir} SKILL.md missing name`);
  if (!/^description:/m.test(front[1])) errors.push(`${dir} SKILL.md missing description`);
  if (dir.startsWith("lampac-") && dir !== "lampac-repo") {
    if (!text.includes("lampac-repo")) errors.push(`${dir} SKILL.md missing lampac-repo link`);
    if (!text.includes(`.agents/agents/${dir}.md`)) errors.push(`${dir} SKILL.md missing role card pointer`);
  }
  for (const entry of readdirSync(folder)) {
    if (/\.(py|sh|cjs)$/.test(entry)) errors.push(`${dir} ships helper ${entry}`);
  }
}

const mintlify = join(root, ".agents/skills/mintlify/SKILL.md");
const mintlifyDocs = join(root, ".agents/skills/mintlify-docs/SKILL.md");
if (existsSync(mintlify) && existsSync(mintlifyDocs)) {
  const hash = (path) => createHash("sha256").update(readFileSync(path)).digest("hex");
  if (hash(mintlify) !== hash(mintlifyDocs)) {
    errors.push("mintlify and mintlify-docs SKILL.md hashes differ");
  }
} else {
  errors.push("mintlify skill pair missing");
}

if (errors.length) {
  console.error(errors.join("\n"));
  process.exit(1);
}

console.log(`ok ${roles.length} roles`);
