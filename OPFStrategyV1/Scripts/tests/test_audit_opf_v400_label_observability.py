import importlib.util
import unittest
from pathlib import Path


PATH = Path(__file__).parents[1] / "Audit-OPFV400LabelObservability.py"
SPEC = importlib.util.spec_from_file_location("v400_label_observability", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class LabelObservabilityTests(unittest.TestCase):
    def test_terminal_does_not_censor_future_global_bars(self):
        touch = {"SnapshotID": "S", "Time": "2026-01-02T10:00:00", "Bar": "10", "Direction": "Bull", "ZoneLow": "96", "ZoneHigh": "100"}
        market = [
            {"SnapshotID": "S", "Time": f"2026-01-02T10:{minute:02d}:00", "Bar": str(10 + index), "High": str(101 + index), "Low": "95"}
            for index, minute in enumerate(range(0, 35, 5))
        ]
        row = MODULE.label_touch(touch, market, terminal_bar=11)
        self.assertEqual("Complete", row["LabelStatus"])
        self.assertEqual(7.0, row["FavorableResponsePoints"])
        self.assertEqual(1.0, row["AdverseResponsePoints"])
        self.assertTrue(row["InvalidatedWithin6Bars"])


if __name__ == "__main__":
    unittest.main()
