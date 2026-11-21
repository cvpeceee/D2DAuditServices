"""Tiny static server for the demo build (HashRouter, so no SPA fallback is needed).

Serves ``src/web/dist`` under the demo base path (``/D2DAuditServices/app/``) so asset
URLs in index.html resolve exactly as they do on the hosted demo.
"""

from __future__ import annotations

import functools
import http.server
import threading
from pathlib import Path


class _Handler(http.server.SimpleHTTPRequestHandler):
    base_prefix = "/"

    def translate_path(self, path: str) -> str:
        clean = path.split("?", 1)[0].split("#", 1)[0]
        if clean.startswith(self.base_prefix):
            clean = "/" + clean[len(self.base_prefix) :]
        return super().translate_path(clean)

    def log_message(self, *_args) -> None:
        pass


def start(dist: Path, base_prefix: str, port: int = 0) -> tuple[http.server.ThreadingHTTPServer, str]:
    prefix = base_prefix if base_prefix.endswith("/") else base_prefix + "/"
    _Handler.base_prefix = prefix
    handler = functools.partial(_Handler, directory=str(dist))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", port), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, f"http://127.0.0.1:{server.server_address[1]}{prefix}"


if __name__ == "__main__":
    import argparse
    import time

    parser = argparse.ArgumentParser()
    parser.add_argument("--dist", default=str(Path(__file__).resolve().parents[2] / "src" / "web" / "dist"))
    parser.add_argument("--base", default="/D2DAuditServices/app/")
    parser.add_argument("--port", type=int, default=4173)
    args = parser.parse_args()
    _, url = start(Path(args.dist), args.base, args.port)
    print(f"Serving demo build at {url}")
    while True:
        time.sleep(3600)
