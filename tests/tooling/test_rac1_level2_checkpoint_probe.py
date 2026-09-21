import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-level2-checkpoint-probe.py"
SPEC = importlib.util.spec_from_file_location("rac1_level2_checkpoint_probe", SCRIPT)
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class Rac1Level2CheckpointProbeTests(unittest.TestCase):
    def make_memory(self):
        pool = 0x00100000
        size = max(
            max(PROBE.CONSUMER_SIGNATURES) + 4,
            PROBE.CHECKPOINT_RECORD + 0x100,
            pool + 0x100,
        )
        memory = bytearray(size)
        for address, word in PROBE.CONSUMER_SIGNATURES.items():
            struct.pack_into("<I", memory, address, word)

        struct.pack_into("<I", memory, PROBE.CURRENT_LEVEL, 2)
        struct.pack_into("<I", memory, PROBE.NANOTECH, 4)
        struct.pack_into("<I", memory, PROBE.LIVE_MOBY_POOL, pool)
        struct.pack_into("<H", memory, pool + PROBE.CLASS_ID_OFFSET, 0)
        struct.pack_into(
            "<3f",
            memory,
            pool + PROBE.LIVE_POSITION_OFFSET,
            210.54315185546875,
            170.20379638671875,
            25.33272361755371,
        )
        struct.pack_into("<f", memory, pool + PROBE.LIVE_YAW_OFFSET, 2.309513807296753)

        struct.pack_into("<I", memory, PROBE.CHECKPOINT_RECORD, 1)
        struct.pack_into(
            "<3f",
            memory,
            PROBE.CHECKPOINT_TRANSFORM,
            205.5801544189453,
            163.04751586914062,
            26.059293746948242,
        )
        struct.pack_into("<f", memory, PROBE.CHECKPOINT_YAW, 0.7809665203094482)
        return memory

    def test_report_separates_class0_from_active_checkpoint(self):
        report = PROBE.checkpoint_report(self.make_memory())

        self.assertEqual(
            report["loadedConsumerSignaturesVerified"],
            len(PROBE.CONSUMER_SIGNATURES),
        )
        witness = report["witness"]
        self.assertEqual(witness["currentLevel"], 2)
        self.assertEqual(witness["nanotech"], 4)
        self.assertEqual(witness["liveClass0Index"], 0)
        self.assertEqual(witness["checkpointActiveFlag"], 1)
        self.assertTrue(witness["checkpointTransformDiffersFromClass0"])
        self.assertGreater(witness["checkpointDistanceFromClass0"], 8.0)
        self.assertAlmostEqual(
            witness["checkpointTransform"]["yawRadians"],
            0.7809665203094482,
        )
        consumer = report["loadedConsumer"]
        self.assertEqual(consumer["routine"], "0x00286520")
        self.assertFalse(consumer["activationWriterRecovered"])
        self.assertIn("0x0013f3d0", consumer["placementCopy"])

    def test_changed_loaded_signature_fails_closed(self):
        memory = self.make_memory()
        address = next(iter(PROBE.CONSUMER_SIGNATURES))
        struct.pack_into("<I", memory, address, 0)
        with self.assertRaises(RuntimeError):
            PROBE.verify_consumer_signatures(memory)

    def test_wrong_level_fails_closed(self):
        memory = self.make_memory()
        struct.pack_into("<I", memory, PROBE.CURRENT_LEVEL, 3)
        with self.assertRaises(RuntimeError):
            PROBE.checkpoint_report(memory)

    def test_missing_live_class0_fails_closed(self):
        memory = self.make_memory()
        pool = struct.unpack_from("<I", memory, PROBE.LIVE_MOBY_POOL)[0]
        struct.pack_into("<H", memory, pool + PROBE.CLASS_ID_OFFSET, 0x123)
        with self.assertRaises(RuntimeError):
            PROBE.checkpoint_report(memory)

    def test_inactive_checkpoint_record_fails_closed(self):
        memory = self.make_memory()
        struct.pack_into("<I", memory, PROBE.CHECKPOINT_RECORD, 0)
        with self.assertRaises(RuntimeError):
            PROBE.checkpoint_report(memory)


if __name__ == "__main__":
    unittest.main()
