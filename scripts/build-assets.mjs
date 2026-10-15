import { mkdir, copyFile } from "node:fs/promises";
const target = new URL(
  "../src/DigitalSorter.Web/wwwroot/vendor/",
  import.meta.url,
);
await mkdir(target, { recursive: true });
await copyFile(
  new URL("../node_modules/heic-to/dist/csp/heic-to.js", import.meta.url),
  new URL("heic-to.js", target),
);
