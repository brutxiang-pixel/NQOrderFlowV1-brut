import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "Analyze-OPFV400ZoneBehavior.py"
SPEC = importlib.util.spec_from_file_location("v400_zone_behavior", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def event(snapshot, zone, kind, time, bar, ordinal, direction, low, high):
    return {
        "SnapshotID": snapshot, "ZoneID": zone, "EventType": kind, "Time": time, "Bar": str(bar),
        "TouchOrdinal": str(ordinal), "Direction": direction, "ZoneLow": str(low), "ZoneHigh": str(high),
    }


def bar(snapshot, zone, time, number, high, low, close, buy=60, sell=40, zone_buy=20, zone_sell=10):
    return {
        "SnapshotID": snapshot, "ZoneID": zone, "Time": time, "Bar": str(number), "High": str(high),
        "Low": str(low), "Close": str(close), "TickDataAvailable": "True",
        "CumulativeBuyVolume": str(buy), "CumulativeSellVolume": str(sell),
        "CumulativeZoneBuyVolume": str(zone_buy), "CumulativeZoneSellVolume": str(zone_sell),
        "MaxFavorableExcursion": "2", "MaxAdverseExcursion": "1",
    }


class FirstTouchLabelTests(unittest.TestCase):
    def test_terminal_before_twelve_bars_is_censored_even_when_rows_are_present(self):
        planned = {"2026-01-02"}
        events = [
            event("S", "terminal", "Birth", "2026-01-02T10:00:00", 10, 0, "Bull", 96, 100),
            event("S", "terminal", "Touch", "2026-01-02T10:05:00", 11, 1, "Bull", 96, 100),
            event("S", "terminal", "Invalidated", "2026-01-02T10:15:00", 13, 1, "Bull", 96, 100),
        ]
        bars = [
            bar("S", "terminal", "2026-01-02T10:00:00", 10, 101, 97, 99),
            bar("S", "terminal", "2026-01-02T10:05:00", 11, 101, 96, 98),
            *[bar("S", "terminal", f"2026-01-02T10:{10 + index * 5:02d}:00", 12 + index, 120, 80, 99) for index in range(12)],
        ]

        row = MODULE.build_first_touch_rows(events, bars, planned)[0]

        self.assertEqual("Censored", row["LabelStatus"])
        self.assertIsNone(row["FavorableResponsePoints"])

    def test_excludes_touch_bar_and_labels_bull_and_bear_directionally(self):
        planned = {"2026-01-02"}
        events = [
            event("S", "bull", "Birth", "2026-01-02T10:00:00", 10, 0, "Bull", 96, 100),
            event("S", "bull", "Touch", "2026-01-02T10:05:00", 11, 1, "Bull", 96, 100),
            event("S", "bear", "Birth", "2026-01-02T11:00:00", 20, 0, "Bear", 200, 204),
            event("S", "bear", "Touch", "2026-01-02T11:05:00", 21, 1, "Bear", 200, 204),
        ]
        bars = [
            bar("S", "bull", "2026-01-02T10:00:00", 10, 101, 97, 99),
            bar("S", "bull", "2026-01-02T10:05:00", 11, 120, 80, 98),
            *[bar("S", "bull", f"2026-01-02T10:{10 + index * 5:02d}:00", 12 + index, 101 + index, 95, 99) for index in range(12)],
            bar("S", "bear", "2026-01-02T11:00:00", 20, 203, 201, 202),
            bar("S", "bear", "2026-01-02T11:05:00", 21, 220, 180, 202),
            *[bar("S", "bear", f"2026-01-02T11:{10 + index * 5:02d}:00", 22 + index, 205, 199 - index, 202) for index in range(12)],
        ]

        rows = MODULE.build_first_touch_rows(events, bars, planned)
        by_zone = {row["ZoneID"]: row for row in rows}

        self.assertEqual(2, len(rows))
        self.assertEqual("Complete", by_zone["bull"]["LabelStatus"])
        self.assertEqual(12.0, by_zone["bull"]["FavorableResponsePoints"])
        self.assertEqual(1.0, by_zone["bull"]["AdverseResponsePoints"])
        self.assertEqual("false", by_zone["bull"]["InvalidatedWithin12Bars"])
        self.assertEqual(12.0, by_zone["bear"]["FavorableResponsePoints"])
        self.assertEqual(1.0, by_zone["bear"]["AdverseResponsePoints"])


if __name__ == "__main__":
    unittest.main()
