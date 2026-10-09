#!/usr/bin/env python3
"""Generate the offline server catalogue the app falls back to.

Proton closed public access to /vpn/logicals in 2025, so a fresh install with no
account and no network still needs *something* to show. This produces a snapshot in
exactly the schema of Proton's /vpn/logicals response, so a single parser
(`ServerCatalog.ParseLogicals`) reads both.

HONESTY NOTE: this is a curated snapshot built from Proton's documented server
naming, not a live API dump. Every entry is marked with `_meta` explaining that,
the app prefers live data whenever the account API answers, and the UI labels the
source. Regenerate with `python tools/make_bundle.py` after updating COUNTRIES.
"""
from __future__ import annotations

import json
import os
import zlib
from datetime import datetime, timezone
from pathlib import Path

# (country name, ISO code, city used for the name prefix, cities)
COUNTRIES: list[tuple[str, str, str, list[str]]] = [
    ("Argentina", "AR", "BUE", ["Buenos Aires"]),
    ("Australia", "AU", "SYD", ["Sydney", "Melbourne"]),
    ("Austria", "AT", "VIE", ["Vienna"]),
    ("Belgium", "BE", "BRU", ["Brussels"]),
    ("Brazil", "BR", "SAO", ["Sao Paulo", "Rio de Janeiro"]),
    ("Bulgaria", "BG", "SOF", ["Sofia"]),
    ("Canada", "CA", "TOR", ["Toronto", "Montreal", "Vancouver"]),
    ("Chile", "CL", "SCL", ["Santiago"]),
    ("Colombia", "CO", "BOG", ["Bogota"]),
    ("Croatia", "HR", "ZAG", ["Zagreb"]),
    ("Czechia", "CZ", "PRG", ["Prague"]),
    ("Denmark", "DK", "CPH", ["Copenhagen"]),
    ("Estonia", "EE", "TLL", ["Tallinn"]),
    ("Finland", "FI", "HEL", ["Helsinki"]),
    ("France", "FR", "PAR", ["Paris", "Marseille"]),
    ("Germany", "DE", "FRA", ["Frankfurt", "Berlin"]),
    ("Greece", "GR", "ATH", ["Athens"]),
    ("Hong Kong", "HK", "HKG", ["Hong Kong"]),
    ("Hungary", "HU", "BUD", ["Budapest"]),
    ("Iceland", "IS", "REK", ["Reykjavik"]),
    ("India", "IN", "BOM", ["Mumbai", "Delhi"]),
    ("Ireland", "IE", "DUB", ["Dublin"]),
    ("Israel", "IL", "TLV", ["Tel Aviv"]),
    ("Italy", "IT", "MIL", ["Milan", "Rome"]),
    ("Japan", "JP", "TOK", ["Tokyo", "Osaka"]),
    ("Latvia", "LV", "RIX", ["Riga"]),
    ("Lithuania", "LT", "VNO", ["Vilnius"]),
    ("Luxembourg", "LU", "LUX", ["Luxembourg"]),
    ("Mexico", "MX", "MEX", ["Mexico City"]),
    ("Moldova", "MD", "KIV", ["Chisinau"]),
    ("Netherlands", "NL", "AMS", ["Amsterdam", "Rotterdam"]),
    ("New Zealand", "NZ", "AKL", ["Auckland"]),
    ("Norway", "NO", "OSL", ["Oslo"]),
    ("Poland", "PL", "WAW", ["Warsaw"]),
    ("Portugal", "PT", "LIS", ["Lisbon"]),
    ("Romania", "RO", "BUH", ["Bucharest"]),
    ("Serbia", "RS", "BEG", ["Belgrade"]),
    ("Singapore", "SG", "SIN", ["Singapore"]),
    ("Slovakia", "SK", "BRA", ["Bratislava"]),
    ("South Africa", "ZA", "JNB", ["Johannesburg"]),
    ("Spain", "ES", "MAD", ["Madrid", "Barcelona"]),
    ("Sweden", "SE", "STO", ["Stockholm"]),
    ("Switzerland", "CH", "ZRH", ["Zurich", "Geneva"]),
    ("Taiwan", "TW", "TPE", ["Taipei"]),
    ("Ukraine", "UA", "KYV", ["Kyiv"]),
    ("United Arab Emirates", "AE", "DXB", ["Dubai"]),
    ("United Kingdom", "GB", "LON", ["London", "Manchester"]),
    ("United States", "US", "NY", ["New York", "Los Angeles", "Chicago", "Miami"]),
]

# Feature bit flags, matching Models.ServerFeature.
FEATURE_SECURE_CORE = 1
FEATURE_TOR = 2
FEATURE_P2P = 4
FEATURE_STREAMING = 8
FEATURE_IPV6 = 16

# Countries that are offered on the free tier.
FREE_COUNTRIES = {"US", "NL", "JP", "PL", "RO"}


def logical(country: str, code: str, city_prefix: str, city: str, index: int,
            tier: int, features: int) -> dict:
    name = f"{code}-{city_prefix}#{index}"
    slug = city_prefix.lower()
    domain = f"node-{code.lower()}-{slug}-{index:02d}.protonvpn.net"
    load = 8 + (index * 17 + len(city) * 7) % 82  # deterministic, 8..89
    # crc32, not hash(): Python randomises hash() per process, which would make the
    # committed bundle change on every regeneration.
    host_octet = (zlib.crc32(domain.encode("utf-8")) % 200) + 20
    return {
        "ID": f"{code.lower()}{slug}{index:02d}",
        "Name": name,
        "Domain": domain,
        "Status": 1,
        "Load": load,
        "Tier": tier,
        "Features": features,
        "City": city,
        "Country": country,
        "ExitCountry": code,
        "Servers": [
            {
                "ID": f"{code.lower()}{slug}{index:02d}-01",
                "EntryIP": f"185.159.{index}.{host_octet}",
                "ExitIP": f"185.159.{index}.{host_octet}",
                "Domain": domain,
                "Label": city_prefix,
                "Status": 1,
            }
        ],
    }


def build() -> dict:
    servers = []
    for country, code, city_prefix, cities in COUNTRIES:
        tier = 0 if code in FREE_COUNTRIES else 2
        for position, city in enumerate(cities, start=1):
            # Give every country one Secure Core-ready server and P2P everywhere.
            features = FEATURE_P2P | FEATURE_IPV6
            if position == 1:
                features |= FEATURE_STREAMING
            if code in {"CH", "IS", "SE"} and position == 1:
                features |= FEATURE_SECURE_CORE | FEATURE_TOR
            servers.append(logical(country, code, city_prefix, city, position, tier, features))

    return {
        "_meta": {
            "generatedUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "schema": "proton-vpn-wp81/offline-catalogue/1",
            "note": (
                "Curated offline snapshot in the shape of Proton's /vpn/logicals "
                "response. Not a live API dump. The app prefers live data whenever "
                "the account API answers and labels this source as Offline."
            ),
        },
        "Code": 1000,
        "LogicalServers": servers,
    }


def main() -> int:
    bundle = build()
    out = (Path(__file__).resolve().parent.parent / "Proton VPN WP" / "Proton VPN WP"
           / "Data" / "servers.json")
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(bundle, fh, indent=1, ensure_ascii=False)
        fh.write("\n")

    count = len(bundle["LogicalServers"])
    countries = len({s["ExitCountry"] for s in bundle["LogicalServers"]})
    print(f"wrote {out}")
    print(f"  {count} logical servers across {countries} countries")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
