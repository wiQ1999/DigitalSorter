import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";
import { X509Certificate, createHash } from "node:crypto";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { setTimeout as delay } from "node:timers/promises";
import { _android, chromium } from "playwright";

const app = "pl.wiq1999.digitalsorter.androidtv";
const execute = promisify(execFile);
async function adb(...args) {
  return (await execute("adb", args, { timeout: 20000 })).stdout;
}

async function tapNativeControl(id) {
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline) {
    await adb("shell", "uiautomator", "dump", "/sdcard/digitalsorter-ui.xml");
    const xml = await adb("exec-out", "cat", "/sdcard/digitalsorter-ui.xml");
    writeFileSync("test-results/native-ui.txt", xml);
    for (const [node] of xml.matchAll(/<node\b[^>]*>/g)) {
      const attributes = Object.fromEntries(
        [...node.matchAll(/([\w:-]+)="([^"]*)"/g)].map((match) => [
          match[1],
          match[2],
        ]),
      );
      if (
        attributes["content-desc"] !== id &&
        attributes["resource-id"] !== id &&
        !attributes["resource-id"]?.endsWith(":id/" + id)
      )
        continue;
      const bounds = attributes.bounds?.match(
        /^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$/,
      );
      assert.ok(bounds, "Native control must have screen bounds: " + id);
      const [, left, top, right, bottom] = bounds.map(Number);
      await adb(
        "shell",
        "input",
        "tap",
        String((left + right) / 2),
        String((top + bottom) / 2),
      );
      return;
    }
    await delay(500);
  }
  assert.fail("Native control was not visible: " + id);
}

const certificate = new X509Certificate(
  readFileSync(process.env.TEST_CERTIFICATE_PEM),
);
const pin = createHash("sha256")
  .update(certificate.publicKey.export({ type: "spki", format: "der" }))
  .digest("base64");

test(
  "Android TV: native startup, phone approval, SecureStorage after restart and remote navigation",
  { timeout: 300000 },
  async () => {
    const [device] = await _android.devices();
    assert.ok(device, "A booted Android TV device is required");
    device.setDefaultTimeout(60000);
    const browser = await chromium.launch({
      channel: "chrome",
      args: ["--no-sandbox", "--ignore-certificate-errors-spki-list=" + pin],
    });
    let page;
    const errors = [];
    async function attach() {
      const webview = await device.webView({ pkg: app }, { timeout: 90000 });
      const result = await webview.page();
      result.setDefaultTimeout(60000);
      result.on("pageerror", (error) => errors.push(error.message));
      return result;
    }
    async function launch() {
      await device.shell(
        `monkey -p ${app} -c android.intent.category.LEANBACK_LAUNCHER 1`,
      );
    }
    async function next() {
      const counter = await page.getByTestId("counter").innerText();
      await page
        .getByRole("button", { name: "Następny →", exact: true })
        .click();
      await page.waitForFunction(
        (previous) =>
          document.querySelector('[data-testid="counter"]').innerText !==
          previous,
        counter,
      );
    }
    try {
      await device.shell(`pm clear ${app}`);
      await launch();
      console.log("Configuring the server through the native Android screen");
      await tapNativeControl("broker-origin");
      await adb("shell", "input", "text", "https://10.0.2.2:7443");
      await device.shell("input keyevent KEYCODE_ESCAPE");
      await tapNativeControl("open-broker");
      page = await attach();
      console.log("Approving the TV from the controlled phone browser");
      await page.getByRole("button", { name: "Połącz przez telefon" }).click();
      const code = (await page.locator(".pair-code").innerText()).trim();
      assert.match(code, /^[A-Z2-9]{4}-[A-Z2-9]{4}$/);
      const phone = await browser.newPage();
      await phone.goto(process.env.TEST_ORIGIN + "/pair?code=" + code);
      await phone.getByRole("link", { name: "Połącz konto Google" }).click();
      await phone
        .getByRole("button", { name: "Zatwierdź dostęp testowy" })
        .click();
      await phone.getByRole("button", { name: "Zatwierdź telewizor" }).click();
      await page.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      assert.equal(
        await page.evaluate(() => document.body.classList.contains("tv")),
        true,
      );
      assert.equal(
        await page.evaluate(() => localStorage.length + sessionStorage.length),
        0,
      );
      await device.shell(`am force-stop ${app}`);
      await launch();
      page = await attach();
      await page.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      console.log(
        "Native SecureStorage restored the session after process restart",
      );
      await phone.evaluate(async () => {
        const response = await fetch("/test-drive/control", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ unavailableThumbnails: true }),
        });
        if (!response.ok)
          throw new Error("Controlled Drive configuration failed");
      });
      await page.getByRole("checkbox").first().check();
      await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
      await page.waitForFunction(
        () => document.querySelector(".media-surface img")?.naturalWidth > 0,
      );
      assert.match(await page.getByTestId("counter").innerText(), /1\s*\/\s*9/);
      await page.waitForFunction(() => {
        const images = [...document.querySelectorAll(".thumbnail img")];
        return (
          images.length === 5 &&
          images.every((image) => image.complete && image.naturalWidth > 0)
        );
      });
      console.log(
        "Native photo and video previews work without Drive thumbnail links",
      );
      assert.equal(
        await page.evaluate(() =>
          [".media-stage", ".counter", ".thumbnail-strip"].every((selector) => {
            const bounds = document
              .querySelector(selector)
              .getBoundingClientRect();
            return (
              bounds.top >= 0 &&
              bounds.bottom <= innerHeight &&
              bounds.right <= innerWidth
            );
          }),
        ),
        true,
        "Native TV keeps the material and upcoming previews inside the screen",
      );
      await device.screenshot({ path: "test-results/native-tv-photos.png" });
      await page.getByTestId("media-stage").focus();
      await device.shell("input keyevent KEYCODE_DPAD_RIGHT");
      await page.waitForFunction(() =>
        document
          .querySelector('[data-testid="counter"]')
          .innerText.trim()
          .startsWith("2"),
      );
      for (let i = 0; i < 3; i++) {
        await next();
        await page.waitForFunction(
          () => document.querySelector(".media-surface img")?.naturalWidth > 0,
        );
      }
      for (const name of [
        "Zachód słońca.mp4",
        "Chwila nad wodą.webm",
        "Spacer.mov",
      ]) {
        console.log("Opening native TV video: " + name);
        await next();
        await page.waitForSelector(".media-surface video");
        assert.equal(
          await page.locator(".media-caption strong").innerText(),
          name,
        );
        await page.getByTestId("media-stage").focus();
        await device.shell("input keyevent KEYCODE_MEDIA_PLAY_PAUSE");
        await page.waitForFunction(
          () => document.querySelector("video")?.currentTime > 0.2,
        );
      }
      await page
        .getByRole("button", { name: "Pełny ekran", exact: true })
        .click();
      await page.waitForSelector(".app-shell.fullscreen");
      await device.shell("input keyevent KEYCODE_BACK");
      await page.waitForSelector(".app-shell:not(.fullscreen)");
      await device.screenshot({ path: "test-results/native-tv-gallery.png" });
      await page.getByRole("button", { name: "Wyloguj", exact: true }).click();
      await page
        .getByRole("button", { name: "Połącz przez telefon" })
        .waitFor();
      await device.shell(`am force-stop ${app}`);
      await launch();
      page = await attach();
      await page
        .getByRole("button", { name: "Połącz przez telefon" })
        .waitFor();
      console.log("Native logout cleared the persisted session");
      assert.deepEqual(errors, []);
      writeFileSync(
        "test-results/native-tv-result.json",
        JSON.stringify({ passed: true, device: device.model() }),
      );
    } finally {
      await device
        .screenshot({ path: "test-results/native-tv-final.png" })
        .catch(() => {});
      await browser.close();
      await device.close();
    }
  },
);
