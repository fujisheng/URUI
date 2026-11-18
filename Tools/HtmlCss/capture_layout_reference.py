"""Capture browser geometry without injecting script or CSS into author fixtures."""
import argparse
import hashlib
import html
import json
import pathlib
import re
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--browser", required=True)
    parser.add_argument("--fixture", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    fixture = args.fixture.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    ids = ["root", "body", "panel", "percent", "rem", "border", "font-reset", "flex-item", "flex-item-zero",
           "flex-sized-item", "flex-scroll-item", "column-item", "media"]
    with tempfile.TemporaryDirectory(prefix="urui-browser-", dir=args.output.resolve()) as scratch:
        wrapper = pathlib.Path(scratch) / "probe.html"
        # The iframe loads the exact source used by the native test. Probe code is
        # outside that document, so supported HTML never requires a script tag.
        script = """
const frame = document.querySelector('iframe');
const rows = [];
const delay = () => new Promise(resolve => setTimeout(resolve, 20));
async function capture() {
  await frame.contentDocument.fonts.ready;
  for (const [width, size] of [[400,null],[640,null],[640,20]]) {
    frame.style.width = width + 'px';
    if (size !== null) frame.contentDocument.documentElement.style.fontSize = size + 'px';
    await delay();
    for (const id of IDS) {
      const e = frame.contentDocument.getElementById(id), r = e.getBoundingClientRect(), s = frame.contentWindow.getComputedStyle(e);
      rows.push({viewport:width, rootFont:parseFloat(frame.contentWindow.getComputedStyle(frame.contentDocument.documentElement).fontSize), id, x:r.x, y:r.y, width:r.width, height:r.height,
        fontSize:parseFloat(s.fontSize), borderTop:parseFloat(s.borderTopWidth), color:s.color});
    }
  }
  document.querySelector('pre').textContent = JSON.stringify(rows);
}
frame.addEventListener('load', () => capture().catch(e => document.querySelector('pre').textContent = JSON.stringify({error:String(e)})));
""".replace("IDS", json.dumps(ids))
        wrapper.write_text('<!doctype html><html><head></head><body><pre id="result">pending</pre>'
                           '<iframe style="height:400px;border:0" src="' + html.escape(fixture.as_uri(), quote=True) + '"></iframe>'
                           '<script>' + script + '</script></body></html>')
        result = subprocess.run([args.browser, "--headless", "--disable-gpu", "--no-first-run", "--allow-file-access-from-files",
                                 "--user-data-dir=" + str(pathlib.Path(scratch) / "profile"), "--virtual-time-budget=1500",
                                 "--dump-dom", wrapper.as_uri()], capture_output=True, text=True, timeout=40)
        (args.output / "browser.stderr.log").write_text(result.stderr)
        result.check_returncode()
        found = re.search(r'<pre id="result">(.*?)</pre>', result.stdout, re.S)
        if not found:
            raise RuntimeError("Browser did not return the geometry probe")
        rows = json.loads(html.unescape(found.group(1)))
        if not isinstance(rows, list) or len(rows) != len(ids) * 3:
            raise RuntimeError(f"Incomplete browser geometry: {rows}")
    evidence = {"browser": subprocess.check_output([args.browser, "--version"], text=True).strip(),
                "inputs": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [fixture, fixture.with_suffix('.css')]},
                "rows": rows}
    (args.output / "layout-browser.json").write_text(json.dumps(evidence, indent=2) + "\n")
    (args.output / "layout-browser.tsv").write_text("".join("\t".join(str(row[key]) for key in
        ["viewport", "rootFont", "id", "x", "y", "width", "height", "fontSize", "borderTop"]) + "\n" for row in rows))
    print(json.dumps({"browser": evidence["browser"], "rows": len(rows), "output": str(args.output)}))


if __name__ == "__main__":
    main()
