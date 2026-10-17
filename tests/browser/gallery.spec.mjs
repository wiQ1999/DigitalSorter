import test, { before, after } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { X509Certificate, createHash } from "node:crypto";
import { chromium } from "playwright";
import {
  pauseRequest,
  thumbnailsFit,
  collectionRequest,
  metadataRequest,
} from "./ui-helpers.mjs";

const origin = process.env.TEST_ORIGIN ?? "https://localhost:7443";
const certificate = new X509Certificate(
  readFileSync(process.env.TEST_CERTIFICATE_PEM),
);
const pin = createHash("sha256")
  .update(certificate.publicKey.export({ type: "spki", format: "der" }))
  .digest("base64");
let browser;
const results = [];
before(async () => {
  mkdirSync("test-results", { recursive: true });
  browser = await chromium.launch({
    executablePath: process.env.TEST_CHROMIUM,
    channel: process.env.TEST_CHROMIUM ? undefined : "chrome",
    args: ["--no-sandbox", "--ignore-certificate-errors-spki-list=" + pin],
    headless: true,
  });
});
after(async () => {
  await browser?.close();
  writeFileSync(
    "test-results/browser-results.json",
    JSON.stringify(results, null, 2),
  );
});

async function context(tv = false, viewport = { width: 1440, height: 1000 }) {
  const ctx = await browser.newContext({ viewport });
  const errors = [];
  ctx.on("page", (page) => {
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("console", (message) => {
      if (
        message.type() === "error" &&
        message.text().includes("Unhandled exception")
      )
        errors.push(message.text());
    });
  });
  const storage = new Map();
  if (tv) {
    await ctx.exposeBinding(
      "controlledNativeStorage",
      async ({ page }, payload) => {
        const { id, operation, value } = JSON.parse(payload);
        if (operation === "set") storage.set("session", value);
        if (operation === "clear") storage.delete("session");
        await page.evaluate(
          ({ id, value }) =>
            globalThis.digitalSorterNativeComplete(id, value, false),
          {
            id,
            value:
              operation === "get" ? (storage.get("session") ?? null) : null,
          },
        );
      },
    );
    await ctx.addInitScript(() => {
      globalThis.DigitalSorterNative = {
        postMessage: (payload) => globalThis.controlledNativeStorage(payload),
      };
    });
  }
  return { ctx, storage, errors };
}
async function login(page) {
  await page.goto(origin);
  await page.getByRole("link", { name: "Połącz Google Drive" }).click();
  await page.getByRole("button", { name: "Zatwierdź dostęp testowy" }).click();
  await page
    .getByRole("heading", { name: "Twoje źródła" })
    .waitFor({ timeout: 60000 });
}
async function load(page) {
  await page.getByRole("checkbox").first().check();
  await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
  await page.getByTestId("counter").waitFor();
  await imageReady(page);
}
async function imageReady(page) {
  await page.waitForFunction(
    () => {
      const image = document.querySelector(".media-surface img");
      return image?.complete && image.naturalWidth > 0;
    },
    { timeout: 30000 },
  );
}
async function next(page, count = 1) {
  for (let i = 0; i < count; i++) {
    const old = await page.getByTestId("counter").innerText();
    await page.getByRole("button", { name: "Następny →", exact: true }).click();
    await page.waitForFunction(
      (old) =>
        document.querySelector('[data-testid="counter"]').innerText !== old,
      old,
    );
  }
}
async function checked(name, action) {
  const fixture = await context();
  try {
    await action(await fixture.ctx.newPage());
    assert.deepEqual(fixture.errors, []);
    results.push({ name, passed: true });
  } finally {
    await fixture.ctx.close();
  }
}

test(
  "Web: metadata progress has discovery, known totals and completion",
  { timeout: 90000 },
  async () => {
    await checked("metadata-progress-completion", async (page) => {
      await login(page);
      const discover = await pauseRequest(page, collectionRequest);
      const metadata = await pauseRequest(page, metadataRequest);
      try {
        await page.getByRole("checkbox").first().check();
        await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
        await page.getByText("Wyszukuję materiały…", { exact: true }).waitFor();
        assert.equal(
          await page
            .locator(".metadata-progress progress")
            .getAttribute("value"),
          null,
        );
        await discover();
        await page
          .getByText("Odczytano 8 z 10 materiałów.", { exact: true })
          .waitFor();
        assert.equal(
          await page.locator(".metadata-progress progress").getAttribute("max"),
          "10",
        );
        assert.equal(
          await page
            .locator(".metadata-progress progress")
            .getAttribute("value"),
          "8",
        );
        await page.screenshot({
          path: "test-results/metadata-progress.png",
          fullPage: true,
        });
        await metadata();
        await page
          .getByTestId("metadata-progress")
          .waitFor({ state: "hidden" });
        await page
          .getByText("Odczyt zakończony: 10 materiałów.", { exact: true })
          .waitFor();
        await imageReady(page);
      } finally {
        await discover(true);
        await metadata(true);
      }
    });
  },
);

test(
  "Web: cancellation ends metadata progress in either phase and retry works",
  { timeout: 90000 },
  async () => {
    await checked("metadata-progress-cancellation", async (page) => {
      await login(page);
      await load(page);
      for (const match of [collectionRequest, metadataRequest]) {
        const resume = await pauseRequest(page, match);
        try {
          await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
          await (
            match === collectionRequest
              ? page.getByText("Wyszukuję materiały…", { exact: true })
              : page.getByText("Odczytano 8 z 10 materiałów.", { exact: true })
          ).waitFor();
          await page.getByRole("button", { name: "Anuluj odczyt" }).click();
          await page
            .getByTestId("metadata-progress")
            .waitFor({ state: "hidden" });
          await page
            .getByRole("alert")
            .getByText("Odczyt został anulowany.", { exact: true })
            .waitFor();
          assert.match(
            await page.getByTestId("counter").innerText(),
            /1\s*\/\s*9/,
          );
          assert.equal(
            await page
              .getByRole("button", { name: "Otwórz kolekcję" })
              .isEnabled(),
            true,
          );
        } finally {
          await resume(true);
        }
      }
      await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
      await page
        .getByText("Odczyt zakończony: 10 materiałów.", { exact: true })
        .waitFor();
      assert.equal(await page.getByTestId("metadata-progress").count(), 0);
    });
  },
);

test(
  "Web: metadata errors end progress and preserve the previous collection",
  { timeout: 90000 },
  async () => {
    await checked("metadata-progress-error", async (page) => {
      await login(page);
      await load(page);
      await page.route(metadataRequest, (route) =>
        route.fulfill({ status: 503, body: "Controlled metadata failure" }),
      );
      await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
      await page
        .getByRole("alert")
        .getByText(
          "Nie udało się połączyć z Google Drive. Sprawdź połączenie i spróbuj ponownie.",
          { exact: true },
        )
        .waitFor();
      assert.equal(await page.getByTestId("metadata-progress").count(), 0);
      assert.equal(
        await page.getByRole("button", { name: "Otwórz kolekcję" }).isEnabled(),
        true,
      );
      assert.match(await page.getByTestId("counter").innerText(), /1\s*\/\s*9/);
      assert.equal(await page.locator(".load-status").count(), 0);
    });
  },
);

test(
  "Web: wider folder panel and previews fit resized desktop and mobile layouts",
  { timeout: 120000 },
  async () => {
    await checked("responsive-folders-and-previews", async (page) => {
      await login(page);
      await load(page);
      const sidebar = await page.locator(".sidebar").boundingBox();
      const viewer = await page.getByTestId("viewer").boundingBox();
      assert.ok(sidebar.width >= 320 && sidebar.width <= 380);
      assert.ok(viewer.width >= 700);
      assert.equal(
        await page
          .locator(".folder-row label > span:last-child")
          .first()
          .evaluate((label) => label.scrollWidth <= label.clientWidth),
        true,
      );
      const upcoming = [
        "Wybrzeże.png",
        "Leśna ścieżka.webp",
        "Zdjęcie z telefonu.heic",
        "Drugie zdjęcie.heif",
        "Zachód słońca.mp4",
        "Chwila nad wodą.webm",
        "Spacer.mov",
        "Niedostępne zdjęcie.jpg",
      ];
      const counts = [];
      for (const width of [1920, 1280, 800, 390, 320, 1440]) {
        await page.setViewportSize({ width, height: 1000 });
        await thumbnailsFit(page, 8);
        const titles = await page
          .locator(".thumbnail")
          .evaluateAll((buttons) => buttons.map((button) => button.title));
        counts.push(titles.length);
        assert.deepEqual(titles, upcoming.slice(0, titles.length));
        assert.equal(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
          true,
        );
      }
      assert.ok(
        counts[0] > 5 && counts[0] > counts[1] && counts[1] > counts[4],
      );
      await page.setViewportSize({ width: 1920, height: 1080 });
      await thumbnailsFit(page, 8);
      assert.equal(
        await page.getByRole("img", { name: "Podgląd niedostępny" }).count(),
        1,
      );
      await page.screenshot({
        path: "test-results/desktop-responsive-previews.png",
        fullPage: true,
      });
      await page.locator(".thumbnail").last().click();
      await page.waitForFunction(() =>
        document
          .querySelector('[data-testid="counter"]')
          .innerText.trim()
          .startsWith("9"),
      );
      await thumbnailsFit(page, 0);
      await page
        .getByRole("button", { name: "← Poprzedni", exact: true })
        .click();
      await thumbnailsFit(page, 1);
      assert.deepEqual(
        await page
          .locator(".thumbnail")
          .evaluateAll((buttons) => buttons.map((button) => button.title)),
        ["Niedostępne zdjęcie.jpg"],
      );
      await page.getByRole("tab", { name: /Bez poprawnej daty/ }).click();
      await thumbnailsFit(page, 0);
      await page
        .getByRole("tab", { name: "Chronologicznie", exact: true })
        .click();
      await thumbnailsFit(page, 8);
    });
  },
);

test(
  "Web: OAuth, source selection, chronological gallery and edge-derived ambient colors",
  { timeout: 120000 },
  async () => {
    await checked("web-gallery-and-ambient", async (page) => {
      await login(page);
      await load(page);
      assert.match(await page.getByTestId("counter").innerText(), /1\s*\/\s*9/);
      assert.equal(
        await page.locator(".media-caption strong").innerText(),
        "Góry o świcie.jpg",
      );
      await page.waitForFunction(() =>
        document
          .querySelector(".media-stage")
          .style.getPropertyValue("--ambient-top")
          .startsWith("rgb"),
      );
      const colors = await page
        .getByTestId("media-stage")
        .evaluate((el) =>
          ["top", "right", "bottom", "left"].map((side) =>
            el.style.getPropertyValue("--ambient-" + side),
          ),
        );
      assert.equal(new Set(colors).size >= 3, true);
      await page.screenshot({
        path: "test-results/web-gallery.png",
        fullPage: true,
      });
      await next(page);
      await imageReady(page);
      assert.equal(
        await page.locator(".media-caption strong").innerText(),
        "Wybrzeże.png",
      );
      await page
        .getByRole("button", { name: "← Poprzedni", exact: true })
        .click();
      await imageReady(page);
      assert.equal(
        await page
          .getByRole("button", { name: "← Poprzedni", exact: true })
          .isDisabled(),
        true,
      );
    });
  },
);

test(
  "Web: unavailable Drive thumbnails fall back to original photos and video frames",
  { timeout: 150000 },
  async () => {
    await checked("client-generated-thumbnails", async (page) => {
      await login(page);
      async function unavailableThumbnails(value) {
        await page.evaluate(async (unavailableThumbnails) => {
          const response = await fetch("/test-drive/control", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ unavailableThumbnails }),
          });
          if (!response.ok)
            throw new Error("Controlled Drive configuration failed");
        }, value);
      }
      async function previewsReady() {
        await page.waitForFunction(() => {
          const images = [...document.querySelectorAll(".thumbnail img")];
          return (
            images.length > 0 &&
            images.every((image) => image.complete && image.naturalWidth > 0)
          );
        });
      }
      await unavailableThumbnails(true);
      try {
        await load(page);
        await previewsReady();
        await next(page, 2);
        await imageReady(page);
        await previewsReady();
        await page.screenshot({
          path: "test-results/client-thumbnails.png",
          fullPage: true,
        });
      } finally {
        await unavailableThumbnails(false);
      }
    });
  },
);

test(
  "Web: undated media is separate and unsupported files are ignored",
  { timeout: 120000 },
  async () => {
    await checked("invalid-list", async (page) => {
      await login(page);
      await load(page);
      await page.getByRole("tab", { name: /Bez poprawnej daty/ }).click();
      await imageReady(page);
      assert.equal(
        await page.locator(".media-caption strong").innerText(),
        "Bez daty.jpg",
      );
      assert.match(await page.getByTestId("counter").innerText(), /1\s*\/\s*1/);
      assert.equal(
        await page
          .getByRole("button", { name: "Następny →", exact: true })
          .isDisabled(),
        true,
      );
      await page
        .getByRole("tab", { name: "Chronologicznie", exact: true })
        .click();
      await imageReady(page);
      assert.match(await page.getByTestId("counter").innerText(), /1\s*\/\s*9/);
      assert.equal(
        await page.getByText("Notatki.txt", { exact: true }).count(),
        0,
      );
    });
  },
);

test(
  "Web: fullscreen hides ambient background and preserves keyboard navigation",
  { timeout: 120000 },
  async () => {
    await checked("fullscreen", async (page) => {
      await login(page);
      await load(page);
      await page
        .getByRole("button", { name: "Pełny ekran", exact: true })
        .click();
      await page.waitForSelector(".app-shell.fullscreen");
      assert.equal(
        await page
          .locator(".ambient")
          .evaluate((el) => getComputedStyle(el).display),
        "none",
      );
      await page.getByTestId("media-stage").focus();
      await page.keyboard.press("ArrowRight");
      await page.waitForFunction(
        () =>
          document.querySelector(".media-caption strong")?.textContent ===
          "Wybrzeże.png",
      );
      await page.keyboard.press("Escape");
      await page.waitForSelector(".app-shell:not(.fullscreen)");
    });
  },
);

test(
  "Web: HEIC/HEIF decode and MP4/WebM/MOV playback stream without backend media proxy",
  { timeout: 180000 },
  async () => {
    await checked("all-media-formats", async (page) => {
      await login(page);
      await load(page);
      await next(page, 3);
      await imageReady(page);
      assert.equal(
        await page.locator(".media-caption strong").innerText(),
        "Zdjęcie z telefonu.heic",
      );
      await next(page);
      await imageReady(page);
      assert.equal(
        await page.locator(".media-caption strong").innerText(),
        "Drugie zdjęcie.heif",
      );
      for (const name of [
        "Zachód słońca.mp4",
        "Chwila nad wodą.webm",
        "Spacer.mov",
      ]) {
        await next(page);
        await page.waitForSelector(".media-surface video");
        assert.equal(
          await page.locator(".media-caption strong").innerText(),
          name,
        );
        await page.locator("video").evaluate((video) => video.play());
        await page.waitForFunction(
          () => document.querySelector("video")?.currentTime > 0.2,
        );
        const initialAmbient = await page
          .locator(".media-stage")
          .getAttribute("style");
        await page.waitForFunction(
          (initial) =>
            document.querySelector(".media-stage").getAttribute("style") !==
            initial,
          initialAmbient,
        );
        const url = await page.locator("video").getAttribute("src");
        assert.match(url, /^\/drive-content\//);
        await page.locator("video").evaluate((video) => {
          video.currentTime = 1.5;
        });
        await page.waitForFunction(
          () => document.querySelector("video")?.currentTime > 1.4,
        );
      }
    });
  },
);

test(
  "Web: unavailable material has a visible explanation",
  { timeout: 120000 },
  async () => {
    await checked("unavailable-media", async (page) => {
      await login(page);
      await load(page);
      await next(page, 8);
      await page
        .getByText("Nie można wyświetlić materiału", { exact: true })
        .waitFor();
      assert.equal(
        await page
          .getByRole("button", { name: "Następny →", exact: true })
          .isDisabled(),
        true,
      );
    });
  },
);

test(
  "Web: mobile layout fits the viewport and has the same fixed dark palette",
  { timeout: 120000 },
  async () => {
    const fixture = await context(false, { width: 390, height: 844 });
    try {
      const page = await fixture.ctx.newPage();
      await login(page);
      await load(page);
      assert.equal(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        true,
      );
      assert.equal(
        await page.evaluate(
          () => getComputedStyle(document.body).backgroundColor,
        ),
        "rgb(11, 16, 24)",
      );
      await page.emulateMedia({ colorScheme: "light" });
      assert.equal(
        await page.evaluate(
          () => getComputedStyle(document.body).backgroundColor,
        ),
        "rgb(11, 16, 24)",
      );
      await page.screenshot({
        path: "test-results/web-mobile.png",
        fullPage: true,
      });
      assert.deepEqual(fixture.errors, []);
      results.push({ name: "mobile-dark-palette", passed: true });
    } finally {
      await fixture.ctx.close();
    }
  },
);

test(
  "TV: phone pairing, persistent native session, shared gallery and isolated logout",
  { timeout: 150000 },
  async () => {
    const tv = await context(true, { width: 1920, height: 1080 });
    const phone = await context();
    try {
      const tvPage = await tv.ctx.newPage();
      await tvPage.goto(origin);
      await tvPage
        .getByRole("button", { name: "Połącz przez telefon" })
        .click();
      const code = (await tvPage.locator(".pair-code").innerText()).trim();
      assert.match(code, /^[A-Z2-9]{4}-[A-Z2-9]{4}$/);
      await tvPage.screenshot({
        path: "test-results/tv-pairing.png",
        fullPage: true,
      });
      const phonePage = await phone.ctx.newPage();
      await phonePage.goto(origin + "/pair?code=" + code);
      await phonePage
        .getByRole("link", { name: "Połącz konto Google" })
        .click();
      await phonePage
        .getByRole("button", { name: "Zatwierdź dostęp testowy" })
        .click();
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .waitFor();
      await phonePage.locator("#pair-code").fill("ZZZZ-ZZZZ");
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .waitFor({ state: "hidden" });
      await phonePage.locator("#pair-code").fill(code);
      await phonePage
        .getByRole("button", { name: "Sprawdź urządzenie" })
        .click();
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .click();
      await phonePage
        .getByRole("heading", { name: "Telewizor połączony" })
        .waitFor();
      await tvPage
        .getByRole("heading", { name: "Twoje źródła" })
        .waitFor({ timeout: 30000 });
      assert.equal(tv.storage.get("session")?.length, 64);
      await tvPage.reload();
      await tvPage.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      await tvPage.setViewportSize({ width: 960, height: 540 });
      const resume = await pauseRequest(tvPage, metadataRequest);
      try {
        await tvPage.getByRole("checkbox").first().check();
        await tvPage.getByRole("button", { name: "Otwórz kolekcję" }).click();
        await tvPage
          .getByText("Odczytano 8 z 10 materiałów.", { exact: true })
          .waitFor();
        await tvPage.waitForFunction(() => {
          const bounds = document
            .querySelector(".metadata-progress")
            .getBoundingClientRect();
          return bounds.top >= 0 && bounds.bottom <= innerHeight;
        });
        await tvPage.screenshot({ path: "test-results/tv-progress.png" });
      } finally {
        await resume();
      }
      await tvPage.getByTestId("counter").waitFor();
      await imageReady(tvPage);
      for (const viewport of [
        { width: 1920, height: 1080 },
        { width: 960, height: 540 },
      ]) {
        await tvPage.setViewportSize(viewport);
        await thumbnailsFit(tvPage, 8);
        assert.equal(
          await tvPage.evaluate(() =>
            [".media-stage", ".counter", ".thumbnail-strip"].every(
              (selector) => {
                const bounds = document
                  .querySelector(selector)
                  .getBoundingClientRect();
                return (
                  bounds.top >= 0 &&
                  bounds.bottom <= innerHeight &&
                  bounds.right <= innerWidth
                );
              },
            ),
          ),
          true,
          "TV keeps material, navigation and thumbnails on screen at both display densities",
        );
        assert.equal(
          await tvPage.evaluate(() => {
            const stage = document
              .querySelector(".media-stage")
              .getBoundingClientRect();
            const image = document
              .querySelector(".media-surface img")
              .getBoundingClientRect();
            return (
              image.top >= stage.top &&
              image.bottom <= stage.bottom &&
              image.left >= stage.left &&
              image.right <= stage.right
            );
          }),
          true,
          "TV contains the complete photo inside the viewer without clipping",
        );
      }
      await tvPage.screenshot({
        path: "test-results/tv-gallery.png",
        fullPage: true,
      });
      await tvPage.getByTestId("media-stage").focus();
      await tvPage.keyboard.press("ArrowRight");
      await tvPage.waitForFunction(
        () =>
          document.querySelector(".media-caption strong")?.textContent ===
          "Wybrzeże.png",
      );
      await tvPage
        .getByRole("button", { name: "Wyloguj", exact: true })
        .click();
      await tvPage
        .getByRole("button", { name: "Połącz przez telefon" })
        .waitFor();
      assert.equal(tv.storage.has("session"), false);
      assert.equal(
        await phonePage.evaluate(
          async () =>
            (await (await fetch("/api/session")).json()).authenticated,
        ),
        true,
      );
      assert.deepEqual(tv.errors, []);
      assert.deepEqual(phone.errors, []);
      results.push({
        name: "tv-phone-pairing-session-and-remote",
        passed: true,
      });
    } finally {
      await tv.ctx.close();
      await phone.ctx.close();
    }
  },
);

test(
  "Web: disconnect explicitly confirms and invalidates another browser session",
  { timeout: 120000 },
  async () => {
    const first = await context();
    const second = await context();
    try {
      const a = await first.ctx.newPage(),
        b = await second.ctx.newPage();
      await login(a);
      await login(b);
      await a
        .getByRole("button", { name: "Odłącz konto", exact: true })
        .click();
      await a
        .getByRole("button", { name: "Odłącz wszędzie", exact: true })
        .click();
      await a.getByRole("link", { name: "Połącz Google Drive" }).waitFor();
      await b.reload();
      await b.getByRole("link", { name: "Połącz Google Drive" }).waitFor();
      assert.deepEqual(first.errors, []);
      assert.deepEqual(second.errors, []);
      results.push({ name: "disconnect-all-clients", passed: true });
    } finally {
      await first.ctx.close();
      await second.ctx.close();
    }
  },
);

test(
  "Phone: revoked Google authorization requires consent and preserves the TV code",
  { timeout: 150000 },
  async () => {
    const tv = await context(true);
    const phone = await context();
    const tvPage = await tv.ctx.newPage();
    const phonePage = await phone.ctx.newPage();
    async function mode(value) {
      await phonePage.evaluate(async (mode) => {
        const response = await fetch("/test-oauth/control", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ mode }),
        });
        if (!response.ok)
          throw new Error("Controlled OAuth configuration failed");
      }, value);
    }
    try {
      await login(phonePage);
      await tvPage.goto(origin);
      await tvPage
        .getByRole("button", { name: "Połącz przez telefon" })
        .click();
      const code = (await tvPage.locator(".pair-code").innerText()).trim();
      await phonePage.goto(origin + "/pair?code=" + code);
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .waitFor();
      await mode("revoked");
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .click();
      const loginLink = phonePage.getByRole("link", {
        name: "Połącz konto Google",
      });
      await loginLink.waitFor();
      assert.equal(await phonePage.locator("#pair-code").inputValue(), code);
      assert.match(await loginLink.getAttribute("href"), /consent=true/);
      await mode("normal");
      await loginLink.click();
      assert.equal(
        new URL(phonePage.url()).searchParams.get("prompt"),
        "consent",
      );
      await phonePage
        .getByRole("button", { name: "Zatwierdź dostęp testowy" })
        .click();
      assert.equal(await phonePage.locator("#pair-code").inputValue(), code);
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .click();
      await tvPage.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      assert.equal(tv.storage.get("session")?.length, 64);
      const firstCredential = tv.storage.get("session");
      await tvPage.getByRole("checkbox").first().check();
      await mode("revoked");
      await tvPage.getByRole("button", { name: "Otwórz kolekcję" }).click();
      await tvPage
        .getByRole("button", { name: "Połącz ponownie", exact: true })
        .click();
      await tvPage.locator(".pair-code").waitFor();
      assert.equal(await tvPage.locator(".toast").count(), 0);
      const replacement = (
        await tvPage.locator(".pair-code").innerText()
      ).trim();
      await phonePage.goto(origin + "/pair?code=" + replacement);
      await mode("normal");
      await phonePage
        .getByRole("link", { name: "Połącz konto Google" })
        .click();
      await phonePage
        .getByRole("button", { name: "Zatwierdź dostęp testowy" })
        .click();
      await phonePage
        .getByRole("button", { name: "Zatwierdź telewizor" })
        .click();
      await tvPage.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      assert.notEqual(tv.storage.get("session"), firstCredential);
      await load(tvPage);
      assert.deepEqual(phone.errors, []);
      assert.deepEqual(tv.errors, []);
      results.push({
        name: "phone-reauthorization-preserves-tv-pairing",
        passed: true,
      });
    } finally {
      await mode("normal").catch(() => {});
      await tv.ctx.close();
      await phone.ctx.close();
    }
  },
);
