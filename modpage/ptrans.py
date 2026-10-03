"""Convert page.md -> HTML shell -> premailer -> prosequor.html."""

from __future__ import annotations

import logging
import re
from pathlib import Path

import markdown
from premailer import transform

ROOT = Path(__file__).resolve().parent
PAGE_MD = ROOT / "page.md"
TEMPLATE = ROOT / "template.html"
OUT_HTML = ROOT / "prosequor.html"

BLOCK_RE = re.compile(
    r"^::: *(\w+)([^\n]*)\n(.*?)^:::\s*$",
    re.MULTILINE | re.DOTALL,
)


def md_fragment(text: str) -> str:
    return markdown.markdown(text.strip(), extensions=[]).strip()


def unwrap_single_p(html: str) -> str:
    m = re.fullmatch(r"<p>(.*)</p>", html, flags=re.DOTALL)
    return m.group(1).strip() if m else html


IMG_P_RE = re.compile(
    r"^<p>\s*(<img\b[^>]*>)\s*</p>\s*(.*)$",
    re.DOTALL | re.IGNORECASE,
)


def render_notice(body: str) -> str:
    """A leading image becomes a seal beside the copy, vertically centered."""
    html = md_fragment(body).strip()
    match = IMG_P_RE.match(html)
    if not match:
        return f'<div class="notice">\n{unwrap_single_p(html)}\n</div>'
    img = match.group(1)
    if re.search(r"\bclass=", img, re.IGNORECASE):
        img = re.sub(
            r"\bclass=(['\"])",
            r"class=\1atlas-seal ",
            img,
            count=1,
            flags=re.IGNORECASE,
        )
    else:
        img = re.sub(r"<img\b", '<img class="atlas-seal"', img, count=1, flags=re.IGNORECASE)
    copy = unwrap_single_p(match.group(2).strip())
    return (
        '<div class="notice">\n'
        f"{img}\n"
        f'<div class="notice-copy">\n{copy}\n</div>\n'
        "</div>"
    )


def render_mods(summary: str, body: str) -> str:
    entries = [e.strip() for e in re.split(r"\n\s*\n", body.strip()) if e.strip()]
    items: list[str] = []
    for entry in entries:
        lines = [ln.strip() for ln in entry.splitlines() if ln.strip()]
        if len(lines) < 2:
            raise ValueError(f"mod entry needs name + url (+ notes): {entry!r}")
        name = lines[0]
        second = lines[1]
        if second.startswith(("http://", "https://")) or second == "":
            url = second
            trailing = " ".join(lines[2:]).strip()
        elif len(lines) == 2:
            # name + notes, empty href (e.g. Floral Zones)
            url = ""
            trailing = second
        else:
            url = second
            trailing = " ".join(lines[2:]).strip()
        if trailing:
            items.append(f'<li>\n<a href="{url}">{name}</a>\n{trailing}\n</li>')
        else:
            items.append(f'<li>\n<a href="{url}">{name}</a>\n</li>')
    ul = "<ul>\n" + "\n".join(items) + "\n</ul>"
    return f"<details>\n<summary>{summary}</summary>\n{ul}\n</details>"


def render_details(header: str, body: str) -> str:
    header = header.strip()
    id_attr = ""
    m = re.search(r"^(.*?)(?:\s+(#[\w-]+))\s*$", header)
    if m:
        title = m.group(1).strip()
        id_attr = f' id="{m.group(2)[1:]}"'
    else:
        title = header
    inner = md_fragment(body)
    return f"<details{id_attr}>\n<summary>{title}</summary>\n{inner}\n</details>"


def expand_blocks(source: str) -> str:
    def repl(match: re.Match[str]) -> str:
        kind = match.group(1).lower()
        header = match.group(2).strip()
        body = match.group(3)
        if kind == "lead":
            return f'<p class="lead">\n{unwrap_single_p(md_fragment(body))}\n</p>'
        if kind == "notice":
            return render_notice(body)
        if kind == "muted":
            return f'<p class="muted">\n{unwrap_single_p(md_fragment(body))}\n</p>'
        if kind == "details":
            if not header:
                raise ValueError("::: details requires a title")
            return render_details(header, body)
        if kind == "mods":
            if not header:
                raise ValueError("::: mods requires a summary label")
            return render_mods(header, body)
        raise ValueError(f"unknown block kind: {kind}")

    return BLOCK_RE.sub(repl, source)


def main() -> int:
    source = PAGE_MD.read_text(encoding="utf-8")
    template = TEMPLATE.read_text(encoding="utf-8")
    if "{body}" not in template:
        raise SystemExit(f"{TEMPLATE.name} must contain a {{body}} placeholder")
    expanded = expand_blocks(source)
    body = md_fragment(expanded)
    html = template.replace("{body}", body)
    # cssutils only knows CSS 2.1, so flex/align-items log as invalid even though
    # they are kept on the elements.
    result = transform(
        html,
        remove_classes=False,
        keep_style_tags=False,
        cssutils_logging_level=logging.CRITICAL,
    )
    OUT_HTML.write_text(result, encoding="utf-8")
    print(f"Wrote {OUT_HTML.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
