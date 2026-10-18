import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
const code = readFileSync(
  "src/DigitalSorter.Data.GoogleDrive/Browser/media-worker.js",
  "utf8",
);
function worker({
  size = 100,
  length = 10,
  contentRange,
  metadataStatus = 200,
} = {}) {
  const listeners = new Map(),
    probes = [],
    thumbnails = [];
  class Channel {
    constructor() {
      this.port1 = { close() {}, onmessage: null };
      this.port2 = {
        postMessage: (data) =>
          queueMicrotask(() => this.port1.onmessage({ data })),
      };
    }
  }
  const self = {
    location: { origin: "https://app.test" },
    addEventListener: (name, handler) => listeners.set(name, handler),
    clients: {
      get: async (id) => ({
        postMessage: (message, [port]) =>
          port.postMessage({ token: "controlled-token" }),
      }),
    },
  };
  const fetch = async (url, options) => {
    if (new URL(url).hostname === "lh3.googleusercontent.com") {
      assert.equal(
        options.headers,
        undefined,
        "Signed preview must not trigger an OAuth preflight",
      );
      assert.equal(options.credentials, "omit");
      assert.equal(options.cache, "no-store");
      thumbnails.push(url);
      return new Response(new Uint8Array(20), {
        headers: { "Content-Type": "image/jpeg" },
      });
    }
    assert.equal(
      options.headers.get("Authorization"),
      "Bearer controlled-token",
    );
    if (new URL(url).searchParams.has("fields")) {
      assert.equal(
        options.headers.has("Range"),
        false,
        "Metadata must not receive the media byte range",
      );
      probes.push(url);
      return new Response(
        JSON.stringify({
          size: String(size),
          thumbnailLink:
            "https://lh3.googleusercontent.com/signed-preview=s220",
        }),
        {
          status: metadataStatus,
        },
      );
    }
    const headers = {
      "Content-Length": String(length),
      "Content-Type": "video/mp4",
    };
    if (contentRange) headers["Content-Range"] = contentRange;
    return new Response(new Uint8Array(length), { status: 206, headers });
  };
  vm.runInNewContext(code, {
    self,
    fetch,
    MessageChannel: Channel,
    Headers,
    Response,
    URL,
    setTimeout,
    clearTimeout,
    console,
  });
  function message(data, client = "client-a") {
    listeners.get("message")({ data, source: { id: client } });
  }
  message({
    type: "configure-source",
    filesEndpoint: "https://app.test/test-drive/files",
  });
  const read = (range, client = "client-a", id = "clip", query = "") =>
    new Promise((resolve) =>
      listeners.get("fetch")({
        request: new Request("https://app.test/drive-content/" + id + query, {
          headers: { Range: range },
        }),
        clientId: client,
        respondWith: resolve,
      }),
    );
  return { read, probes, message, thumbnails };
}
for (const [range, expected] of [
  ["bytes=10-19", "bytes 10-19/100"],
  ["bytes=90-", "bytes 90-99/100"],
  ["bytes=-10", "bytes 90-99/100"],
]) {
  test("Drive CORS: reconstruct " + range, async () => {
    const fixture = worker();
    const response = await fixture.read(range);
    assert.equal(response.status, 206);
    assert.equal(response.headers.get("Content-Range"), expected);
    assert.equal(response.headers.get("Cache-Control"), "no-store");
    assert.equal((await response.arrayBuffer()).byteLength, 10);
  });
}
test("Drive CORS: preserve an exposed range without an extra metadata request", async () => {
  const fixture = worker({ contentRange: "bytes 10-19/100" });
  const response = await fixture.read("bytes=10-19");
  assert.equal(response.headers.get("Content-Range"), "bytes 10-19/100");
  assert.equal(fixture.probes.length, 0);
});
test("Drive CORS: reject inconsistent returned byte counts", async () => {
  for (const range of [
    "bytes=95-",
    "bytes=10-15",
    "bytes=bad",
    "bytes=0-1,3-4",
  ]) {
    const response = await worker().read(range);
    assert.equal(response.status, 502, range);
  }
});
test("Drive CORS: sizes are scoped to a client, bounded and forgotten at logout", async () => {
  const fixture = worker();
  await fixture.read("bytes=10-19");
  await fixture.read("bytes=20-29");
  assert.equal(fixture.probes.length, 1);
  await fixture.read("bytes=10-19", "client-b");
  assert.equal(fixture.probes.length, 2);
  fixture.message({ type: "forget-media" });
  await fixture.read("bytes=10-19");
  assert.equal(fixture.probes.length, 3);
  for (let index = 0; index < 64; index++)
    await fixture.read("bytes=10-19", "client-a", "clip-" + index);
  await fixture.read("bytes=10-19");
  assert.equal(
    fixture.probes.length,
    68,
    "The oldest of 65 file sizes is evicted",
  );
});
test("Drive CORS: invalid file sizes fail without inventing a total", async () => {
  for (const size of [0, -1, "unknown", Number.MAX_SAFE_INTEGER + 1])
    assert.equal((await worker({ size }).read("bytes=10-19")).status, 502);
});

test("Drive preview: signed image URL avoids OAuth preflight and uses the requested TV size", async () => {
  const fixture = worker();
  const response = await fixture.read(
    "bytes=0-0",
    "client-a",
    "photo",
    "?preview=1280",
  );
  assert.equal(response.status, 200);
  assert.equal(response.headers.get("Cache-Control"), "no-store");
  assert.deepEqual(fixture.thumbnails, [
    "https://lh3.googleusercontent.com/signed-preview=s1280",
  ]);
  assert.equal((await response.arrayBuffer()).byteLength, 20);
});
