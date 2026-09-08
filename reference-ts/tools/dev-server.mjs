import { createServer } from "node:http";
import { readFile, stat, writeFile, mkdir } from "node:fs/promises";
import { extname, join, normalize, basename } from "node:path";
import { fileURLToPath } from "node:url";

const root = normalize(join(fileURLToPath(new URL("..", import.meta.url))));
const port = Number(process.env.PORT ?? 4173);
const mime = new Map([[".html", "text/html; charset=utf-8"], [".js", "text/javascript; charset=utf-8"], [".css", "text/css; charset=utf-8"], [".json", "application/json"]]);

const server = createServer(async (req, res) => {
  try {
    const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "localhost"}`);

    // Dev-only: let the viewer POST a `canvas.toDataURL()` so a screenshot lands
    // on disk under captures/ (git-ignored). `?name=` picks the basename.
    if (req.method === "POST" && url.pathname === "/__capture") {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      const body = Buffer.concat(chunks).toString("utf8");
      const b64 = body.includes(",") ? body.slice(body.indexOf(",") + 1) : body;
      const name = basename(url.searchParams.get("name") ?? "viewer-shot.png").replace(/[^\w.-]/g, "_");
      await mkdir(join(root, "captures"), { recursive: true });
      await writeFile(join(root, "captures", name), Buffer.from(b64, "base64"));
      res.writeHead(200, { "content-type": "text/plain" });
      res.end(`captures/${name}`);
      return;
    }

    const requestPath = url.pathname === "/" ? "/apps/viewer/index.html" : url.pathname;
    const filePath = normalize(join(root, requestPath));
    if (!filePath.startsWith(root)) throw new Error("Invalid path");
    const info = await stat(filePath);
    if (!info.isFile()) throw new Error("Not a file");
    const data = await readFile(filePath);
    res.writeHead(200, { "content-type": mime.get(extname(filePath)) ?? "application/octet-stream", "cache-control": "no-store" });
    res.end(data);
  } catch {
    res.writeHead(404, { "content-type": "text/plain" });
    res.end("Not found");
  }
});
server.listen(port, "127.0.0.1", () => console.log(`OBP viewer: http://127.0.0.1:${port}`));
