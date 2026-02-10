#!/usr/bin/env python3
import json
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

PRIMARY_URL = "https://www.gesetze-im-internet.de/rvg/anlage_2.html"
FALLBACK_URL = "https://raw.githubusercontent.com/QuantLaw/gesetze-im-internet/data/data/items/rvg/BJNR078800004.xml"


def fetch(url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "AkteTimer-RVG-Generator/1.0"})
    with urllib.request.urlopen(req, timeout=30) as response:
        return response.read().decode("utf-8")


def normalize_number(token: str) -> str:
    return token.replace("\xa0", " ").replace(" ", "").replace(".", "").replace(",", ".").strip()


def parse_int(token: str) -> int:
    token = normalize_number(token)
    if "." in token:
        token = token.split(".", 1)[0]
    return int(token)


def parse_decimal(token: str) -> float:
    return float(normalize_number(token))


def text_content(elem: ET.Element) -> str:
    return "".join(elem.itertext()).strip()


def parse_entries_from_xml(content: str):
    root = ET.fromstring(content)
    target_table = None

    for norm in root.findall("norm"):
        enbez = norm.findtext("./metadaten/enbez") or ""
        if "Anlage 2" not in enbez:
            continue

        for table in norm.findall(".//table"):
            tgroup = table.find("tgroup")
            if tgroup is not None and tgroup.attrib.get("cols") == "5":
                target_table = table
                break
        if target_table is not None:
            break

    if target_table is None:
        raise RuntimeError("Anlage-2-Tabelle konnte in XML-Quelle nicht gefunden werden.")

    entries = []
    for row in target_table.findall("./tgroup/tbody/row"):
        cells = [text_content(entry) for entry in row.findall("entry")]
        if len(cells) < 5:
            continue

        left_value, left_fee = cells[0], cells[1]
        right_value, right_fee = cells[3], cells[4]

        if re.search(r"\d", left_value) and re.search(r"\d", left_fee):
            entries.append((parse_int(left_value), parse_decimal(left_fee)))

        if re.search(r"\d", right_value) and re.search(r"\d", right_fee):
            entries.append((parse_int(right_value), parse_decimal(right_fee)))

    return sorted(entries, key=lambda e: e[0])


def main() -> int:
    repo_root = Path(__file__).resolve().parents[2]
    output_path = repo_root / "src" / "AkteTimer" / "Resources" / "rvg_fee_table.json"

    try:
        content = fetch(PRIMARY_URL)
        source = PRIMARY_URL
        raise RuntimeError("HTML parsing not implemented; forcing fallback to structured XML source")
    except Exception as ex:
        print(f"Primärquelle nicht erreichbar/verarbeitbar ({PRIMARY_URL}): {ex}", file=sys.stderr)
        print(f"Verwende Fallback-Quelle: {FALLBACK_URL}", file=sys.stderr)
        content = fetch(FALLBACK_URL)
        source = FALLBACK_URL

    entries = parse_entries_from_xml(content)

    if not entries:
        raise RuntimeError("Keine RVG-Einträge aus Quelle extrahiert.")

    payload = {
        "metadata": {
            "source": source,
            "version_label": "Anlage 2 zu § 13 RVG",
            "version_date": "generated"
        },
        "entries": [
            {"value_to_eur": value, "fee_1_0_eur": round(fee + 1e-9, 2)}
            for value, fee in entries
        ],
    }

    output_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"RVG-Tabelle aktualisiert: {output_path}")
    print(f"Quelle: {source}")
    print(f"Einträge: {len(entries)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
