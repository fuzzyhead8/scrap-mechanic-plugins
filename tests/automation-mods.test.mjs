import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

async function readModule(relativePath) {
  return readFile(path.join(repoRoot, relativePath), "utf8");
}

function functionBody(source, qualifiedName) {
  const escaped = qualifiedName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const startPattern = new RegExp(`function\\s+${escaped}\\s*\\([^)]*\\)`);
  const start = source.search(startPattern);
  assert.notEqual(start, -1, `Missing Lua function ${qualifiedName}`);

  const nextFunction = source.indexOf("\nfunction ", start + 1);
  return source.slice(start, nextFunction === -1 ? source.length : nextFunction);
}

test("Beehive creates real hvs_loot output for wax", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );

  assert.match(
    source,
    /sm\.harvestable\.createHarvestable\(\s*hvs_loot\s*,/,
    "Beehive must create a real loot harvestable",
  );
  assert.match(
    source,
    /setParams\(\s*\{\s*uuid\s*=\s*ITEMS\.obj_resource_beewax\s*,\s*quantity\s*=/,
    "Beehive loot must represent wax with an explicit quantity",
  );
  assert.match(
    source,
    /self\.shape\.worldPosition\s*\+\s*\(\s*self\.shape\.worldRotation\s*\*\s*sm\.vec3\.new\(\s*0\s*,\s*LootSpawnHeightOffset\s*,\s*0\s*\)\s*\)/,
    "Beehive loot must spawn above the machine in local space",
  );
});

test("Beehive compensates the vanilla loot visual height offset", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const spawn = functionBody(
    source,
    "InteractableBeehive.sv_spawnPhysicalOutput",
  );

  assert.match(source, /local LootVisualHeightOffset = 0\.375/);
  assert.match(
    spawn,
    /local desiredVisualPosition\s*=\s*self\.shape\.worldPosition\s*\+\s*\(\s*self\.shape\.worldRotation\s*\*\s*sm\.vec3\.new\(\s*0\s*,\s*LootSpawnHeightOffset\s*,\s*0\s*\)\s*\)/,
    "Beehive loot must keep the intended visual point above the machine in local space",
  );
  assert.match(
    spawn,
    /local position\s*=\s*desiredVisualPosition\s*-\s*sm\.vec3\.new\(\s*0\s*,\s*0\s*,\s*LootVisualHeightOffset\s*\)/,
    "Physical loot origin must compensate for LootHarvestable's global visual offset",
  );
});

test("Beehive uses the vanilla upright loot collision rotation", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const spawn = functionBody(
    source,
    "InteractableBeehive.sv_spawnPhysicalOutput",
  );

  assert.match(
    spawn,
    /local rotation\s*=\s*sm\.vec3\.getRotation\(\s*sm\.vec3\.new\(\s*0\s*,\s*1\s*,\s*0\s*\)\s*,\s*sm\.vec3\.new\(\s*0\s*,\s*0\s*,\s*1\s*\)\s*\)/,
    "Physical loot collision must use the vanilla upright hvs_loot rotation",
  );
  assert.doesNotMatch(
    spawn,
    /self\.shape\.worldRotation\s*\*\s*sm\.vec3\.getRotation/,
    "Machine rotation must not separate the vanilla loot hitbox from its visual",
  );
});

test("Beehive consolidates overlapping physical wax before spawning a new stack", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const collect = functionBody(
    source,
    "InteractableBeehive.sv_collectPhysicalOutput",
  );
  const spawn = functionBody(
    source,
    "InteractableBeehive.sv_spawnPhysicalOutput",
  );

  assert.match(
    collect,
    /sm\.physics\.getSphereContacts\(\s*position\s*,\s*LootMergeRadius\s*,/,
    "Beehive must inspect existing harvestables at its exact output point",
  );
  assert.match(collect, /candidate\.uuid\s*==\s*hvs_loot/);
  assert.match(
    collect,
    /publicData\.uuid\s*==\s*ITEMS\.obj_resource_beewax/,
    "Only physical beeswax loot may be consolidated",
  );
  assert.match(
    collect,
    /sm\.vec3\.getDistance\(\s*candidate\.worldPosition\s*,\s*position\s*\)\s*<=\s*LootMergePositionTolerance/,
    "Nearby unrelated wax must not be consolidated",
  );
  assert.match(collect, /not\s+publicData\.harvested/);
  assert.match(
    collect,
    /quantity\s*=\s*quantity\s*\+\s*publicData\.quantity/,
    "Every overlapping x1 loot must contribute to the replacement stack quantity",
  );
  assert.match(collect, /candidate\.publicData\.harvested\s*=\s*true/);
  assert.match(collect, /candidate:destroy\(\)/);
  assert.ok(
    spawn.indexOf("self:sv_collectPhysicalOutput( position )") <
      spawn.indexOf("sm.harvestable.createHarvestable"),
    "Existing wax must be consolidated before the replacement stack is created",
  );
});

test("Beehive does not rebuild a physical wax stack without new output", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const spawn = functionBody(
    source,
    "InteractableBeehive.sv_spawnPhysicalOutput",
  );

  const noOutputGuard =
    /if\s+self\.sv\.saved\.pendingPhysicalOutput\s*<=\s*0\s+then\s+return\s+end/;
  assert.match(
    spawn,
    noOutputGuard,
    "Existing loot must only be consolidated when freshly committed output is pending",
  );
  assert.ok(
    spawn.search(noOutputGuard) <
      spawn.indexOf("self:sv_collectPhysicalOutput( position )"),
    "The no-output guard must run before scanning existing loot",
  );
});

test("Beehive queues committed production and splits wax by stack size", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const update = functionBody(source, "InteractableBeehive.sv_updateProgress");

  assert.match(source, /pendingPhysicalOutput\s*=\s*0/);
  assert.match(source, /sm\.item\.getStackSize\(\s*ITEMS\.obj_resource_beewax\s*\)/);
  assert.match(source, /math\.min\(\s*stackSize\s*,\s*self\.sv\.saved\.pendingPhysicalOutput\s*\)/);
  assert.match(update, /if\s+sm\.container\.endTransaction\(\)\s+then/);
  const queueAssignment =
    "self.sv.saved.pendingPhysicalOutput = self.sv.saved.pendingPhysicalOutput + produced";
  assert.ok(
    update.indexOf(queueAssignment) > update.indexOf("sm.container.endTransaction()"),
    "Physical output must only be queued after a successful container transaction",
  );
  assert.doesNotMatch(
    update,
    /self\.sv\.saved\.beewax\s*=\s*min\(/,
    "New production must not be mixed into the legacy manual-output field",
  );
});

test("Beehive does not double-count committed output when resetting progress", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const update = functionBody(source, "InteractableBeehive.sv_updateProgress");

  assert.match(update, /local canProduce = function\( additionalOutput \)/);
  assert.match(update, /while remainingProgress >= ProduceTickTime and canProduce\( produced \) do/);
  assert.match(update, /if not canProduce\( 0 \) then/);
});

test("Beehive destruction does not discard queued physical wax", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );
  const destroy = functionBody(source, "InteractableBeehive.server_onDestroy");
  const update = functionBody(source, "InteractableBeehive.sv_updateProgress");

  assert.match(update, /self\.position = self\.shape\.worldPosition/);
  assert.match(update, /self\.rotation = self\.shape\.worldRotation/);
  assert.match(
    destroy,
    /self\.sv\.saved\.beewax\s*\+\s*self\.sv\.saved\.pendingPhysicalOutput/,
  );
  assert.match(destroy, /while remainingOutput > 0 do/);
});

test("Beehive preserves the vanilla recipe, timing, and legacy wax pickup", async () => {
  const source = await readModule(
    "mods/beehive-automation/InteractableBeehive.lua",
  );

  assert.match(source, /local ProduceTickTime = DAYCYCLE_TIME_TICKS \* 0\.12/);
  assert.match(source, /local NumConsumed = 1/);
  assert.match(source, /local NumProduced = 1/);
  assert.match(source, /function InteractableBeehive\.sv_n_collect/);
  assert.match(source, /self\.sv\.saved\.beewax/);
  assert.match(source, /sm\.areaTrigger\.createAttachedSphere/);
  assert.match(source, /Loot - GlowItem/);
});

test("Freezer creates real hvs_loot output for ice", async () => {
  const source = await readModule("mods/freezer-automation/Freezer.lua");

  assert.match(
    source,
    /sm\.harvestable\.createHarvestable\(\s*hvs_loot\s*,/,
    "Freezer must create a real loot harvestable",
  );
  assert.match(
    source,
    /setParams\(\s*\{\s*uuid\s*=\s*ITEMS\.blk_ice\s*,\s*quantity\s*=/,
    "Freezer loot must represent ice with an explicit quantity",
  );
  assert.match(
    source,
    /self\.shape\.worldPosition\s*\+\s*\(\s*self\.shape\.worldRotation\s*\*\s*sm\.vec3\.new\(\s*0\s*,\s*LootSpawnHeightOffset\s*,\s*0\s*\)\s*\)/,
    "Freezer loot must spawn above the machine in local space",
  );
});

test("Freezer queues committed production and splits ice by stack size", async () => {
  const source = await readModule("mods/freezer-automation/Freezer.lua");
  const update = functionBody(source, "Freezer.sv_updateProgress");

  assert.match(source, /pendingPhysicalOutput\s*=\s*0/);
  assert.match(source, /sm\.item\.getStackSize\(\s*ITEMS\.blk_ice\s*\)/);
  assert.match(source, /math\.min\(\s*stackSize\s*,\s*self\.sv\.saved\.pendingPhysicalOutput\s*\)/);
  assert.match(update, /if\s+sm\.container\.endTransaction\(\)\s+then/);
  const queueAssignment =
    "self.sv.saved.pendingPhysicalOutput = self.sv.saved.pendingPhysicalOutput + produced";
  assert.ok(
    update.indexOf(queueAssignment) > update.indexOf("sm.container.endTransaction()"),
    "Physical output must only be queued after a successful container transaction",
  );
  assert.doesNotMatch(
    update,
    /self\.sv\.saved\.ice\s*=\s*min\(/,
    "New production must not be mixed into the legacy manual-output field",
  );
});

test("Freezer does not double-count committed output when resetting progress", async () => {
  const source = await readModule("mods/freezer-automation/Freezer.lua");
  const update = functionBody(source, "Freezer.sv_updateProgress");

  assert.match(update, /local canProduce = function\( additionalOutput \)/);
  assert.match(update, /while remainingProgress >= ProduceTickTime and canProduce\( produced \) do/);
  assert.match(update, /if not canProduce\( 0 \) then/);
});

test("Freezer destruction does not discard queued physical ice", async () => {
  const source = await readModule("mods/freezer-automation/Freezer.lua");
  const destroy = functionBody(source, "Freezer.server_onDestroy");
  const update = functionBody(source, "Freezer.sv_updateProgress");

  assert.match(update, /self\.position = self\.shape\.worldPosition/);
  assert.match(update, /self\.rotation = self\.shape\.worldRotation/);
  assert.match(
    destroy,
    /self\.sv\.saved\.ice\s*\+\s*self\.sv\.saved\.pendingPhysicalOutput/,
  );
  assert.match(destroy, /while remainingOutput > 0 do/);
});

test("Freezer preserves the vanilla recipe, timing, and legacy ice pickup", async () => {
  const source = await readModule("mods/freezer-automation/Freezer.lua");

  assert.match(source, /local ProduceTickTime = DAYCYCLE_TIME_TICKS \* 0\.06/);
  assert.match(source, /local NumConsumed = 1/);
  assert.match(source, /local NumProduced = 20/);
  assert.match(source, /function Freezer\.sv_n_collect/);
  assert.match(source, /self\.sv\.saved\.ice/);
  assert.match(source, /sm\.areaTrigger\.createAttachedSphere/);
  assert.match(source, /Loot - GlowItem/);
});
