"""
Regenerates the entity-relationship diagrams from the live database.

    python docs/erd/generate.py
    python docs/erd/generate.py --server localhost --database TheeBuhleDev

Reads sys.foreign_keys and sys.columns through sqlcmd (Windows authentication),
writes one Mermaid source per diagram, and renders each to SVG and to a 2x PNG
with Mermaid 11 in a headless Chromium browser — Edge, which every Windows 11
machine has, or whatever the BROWSER environment variable points at.

Nothing is installed. Mermaid is loaded from the jsDelivr CDN by the page the
browser renders, so the machine needs a network connection.

Run it after any migration that changes a table or a relationship: an ERD that
has drifted from the schema is worse than none, because it is believed.
"""

import argparse
import collections
import io
import json
import math
import os
import re
import subprocess
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))

BROWSER = os.environ.get(
    "BROWSER", r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")

MERMAID = "https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.min.js"

# The Heritage palette, so the diagrams match the site, the console and the app.
CONFIG = {
    "startOnLoad": False,
    "theme": "base",
    "er": {"layoutDirection": "TB", "minEntityWidth": 120, "entityPadding": 12, "useMaxWidth": False},
    "themeVariables": {
        "fontFamily": "Inter, Segoe UI, Arial, sans-serif",
        "primaryColor": "#FAF6EE",
        "primaryBorderColor": "#2E4034",
        "primaryTextColor": "#17150F",
        "lineColor": "#A87C32",
        "tertiaryColor": "#FFFFFF",
    },
}

AREAS = collections.OrderedDict([
    ("people", ["users", "learners", "parent_guardians", "guardian_links", "mentors", "staff",
                "refresh_tokens", "account_codes", "audit_log"]),
    ("programmes", ["programmes", "venues", "enrolments", "sessions", "attendance"]),
    ("mentorship", ["mentor_assignments", "check_ins", "compliance_documents"]),
    ("learning", ["subjects", "resources", "resource_progress", "assessment_results", "badges",
                  "learner_badges", "learner_summaries", "peer_groups", "group_memberships",
                  "buddy_requests"]),
    ("communication", ["permission_forms", "consents", "announcements", "message_recipients",
                       "notifications", "notification_preferences", "device_tokens"]),
    ("giving", ["donors", "donations", "sponsorships", "news_posts", "events", "event_registrations",
                "contact_messages", "volunteer_applications", "newsletter_subscribers",
                "impact_metrics"]),
])

CORE = ["users", "learners", "parent_guardians", "guardian_links", "mentors", "staff",
        "mentor_assignments", "check_ins", "programmes", "enrolments", "sessions",
        "attendance", "permission_forms", "consents", "donors", "donations"]

# Left to right where a single table fans out to many children; top to bottom
# elsewhere, which is closer to a 16:9 slide for these shapes.
LEFT_TO_RIGHT = {"overview", "people"}

BOILERPLATE = {"created_at", "updated_at", "updated_by"}

# "Who did this" references. All point at users; drawing them would turn every
# diagram into a starburst around one table. They stay marked FK in the columns.
ACTORS = {"published_by", "captured_by", "recorded_by", "approved_by", "closed_by",
          "escalated_to", "verified_by", "handled_by", "created_by", "reviewed_by"}

COLUMNS_SQL = """SET NOCOUNT ON;
SELECT t.name, c.name, ty.name,
  CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE i.is_primary_key=1 AND ic.object_id=c.object_id AND ic.column_id=c.column_id) THEN 1 ELSE 0 END AS bit),
  CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_key_columns f WHERE f.parent_object_id=c.object_id AND f.parent_column_id=c.column_id) THEN 1 ELSE 0 END AS bit),
  CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE i.is_unique=1 AND i.is_primary_key=0 AND ic.object_id=c.object_id AND ic.column_id=c.column_id) THEN 1 ELSE 0 END AS bit)
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.name <> '__EFMigrationsHistory' ORDER BY t.name, c.column_id;"""

FOREIGN_KEYS_SQL = """SET NOCOUNT ON;
SELECT tp.name, cp.name, tr.name,
  CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id
        WHERE i.is_unique=1 AND ic.object_id=fk.parent_object_id AND ic.column_id=fkc.parent_column_id
          AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id)=1) THEN 1 ELSE 0 END AS bit),
  cp.is_nullable
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
ORDER BY tr.name, tp.name;"""

PAGE = """<!doctype html><html><head><meta charset="utf-8"><script src="%s"></script></head>
<body style="margin:0;background:#fff"><div id="out"></div><script>
mermaid.initialize(%s);
mermaid.render('d', %s).then(r => { document.getElementById('out').innerHTML = r.svg; })
  .catch(e => { document.getElementById('out').textContent = 'ERROR: ' + e; });
</script></body></html>"""


def query(server, database, sql):
    result = subprocess.run(
        ["sqlcmd", "-S", server, "-d", database, "-E", "-h", "-1", "-W", "-s", "|", "-Q", sql],
        capture_output=True, text=True, check=True)
    return [[part.strip() for part in line.split("|")] for line in result.stdout.splitlines() if "|" in line]


def is_actor(child, column):
    return column in ACTORS or (child == "audit_log" and column == "user_id")


def diagram(tables, columns, keys, with_columns):
    lines = ["erDiagram"]
    for child, column, parent, one, nullable in keys:
        if is_actor(child, column) or not (child in tables or parent in tables):
            continue
        if not with_columns and not (child in tables and parent in tables):
            continue
        lines.append(f'    {parent} {"|o" if nullable else "||"}--{"o|" if one else "o{"} {child} : "{column}"')
    if with_columns:
        for table in columns:
            if table not in tables:
                continue
            lines.append(f"    {table} {{")
            for column, sqltype, pk, fk, uk in columns[table]:
                if column in BOILERPLATE:
                    continue
                marks = ",".join(k for k, on in (("PK", pk), ("FK", fk), ("UK", uk)) if on)
                lines.append(f"        {sqltype} {column}" + (f" {marks}" if marks else ""))
            lines.append("    }")
    return "\n".join(lines) + "\n"


def render(name, source, workdir):
    config = json.loads(json.dumps(CONFIG))
    if name in LEFT_TO_RIGHT:
        config["er"]["layoutDirection"] = "LR"

    page = os.path.join(workdir, f"{name}.html")
    io.open(page, "w", encoding="utf-8").write(PAGE % (MERMAID, json.dumps(config), json.dumps(source)))

    dom = subprocess.run([BROWSER, "--headless=new", "--disable-gpu", "--virtual-time-budget=15000",
                          "--dump-dom", "file:///" + page.replace(os.sep, "/")],
                         capture_output=True, text=True, encoding="utf-8", timeout=180).stdout
    match = re.search(r"<svg.*?</svg>", dom, re.S)
    if not match:
        raise RuntimeError(f"{name}: Mermaid did not render. {dom[:300]}")

    svg = match.group(0)
    io.open(os.path.join(HERE, f"{name}.svg"), "w", encoding="utf-8", newline="\n").write(svg)

    width, height = (math.ceil(float(v)) for v in re.search(r'viewBox="0 0 ([\d.]+) ([\d.]+)"', svg).groups())
    frame = os.path.join(workdir, f"{name}.frame.html")
    io.open(frame, "w", encoding="utf-8").write(
        f'<html><body style="margin:0;background:#fff;width:{width}px;height:{height}px">{svg}</body></html>')
    subprocess.run([BROWSER, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    f"--window-size={width},{height}", "--force-device-scale-factor=2",
                    f"--screenshot={os.path.join(HERE, name + '.png')}",
                    "file:///" + frame.replace(os.sep, "/")], capture_output=True, timeout=180)
    return width, height


def main():
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--server", default="localhost")
    parser.add_argument("--database", default="TheeBuhleDev")
    args = parser.parse_args()

    columns = collections.OrderedDict()
    for table, column, sqltype, pk, fk, uk in query(args.server, args.database, COLUMNS_SQL):
        columns.setdefault(table, []).append((column, sqltype, pk == "1", fk == "1", uk == "1"))

    keys = [(c, col, p, one == "1", null == "1")
            for c, col, p, one, null in query(args.server, args.database, FOREIGN_KEYS_SQL)]

    unplaced = set(columns) - {t for tables in AREAS.values() for t in tables}
    if unplaced:
        raise SystemExit(f"Tables in no area — add them to AREAS: {sorted(unplaced)}")

    sources = {"core": diagram(set(CORE), columns, keys, with_columns=False),
               "overview": diagram(set(columns), columns, keys, with_columns=False)}
    for name, tables in AREAS.items():
        sources[name] = diagram(set(tables), columns, keys, with_columns=True)

    with tempfile.TemporaryDirectory() as workdir:
        for name, source in sources.items():
            io.open(os.path.join(HERE, f"{name}.mmd"), "w", encoding="utf-8", newline="\n").write(source)
            width, height = render(name, source, workdir)
            print(f"{name:<14} {width} x {height}")

    print(f"{len(columns)} tables, {sum(len(c) for c in columns.values())} columns, {len(keys)} foreign keys")


if __name__ == "__main__":
    main()
