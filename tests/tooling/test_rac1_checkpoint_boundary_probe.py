import importlib.util
import pathlib
import struct
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-checkpoint-boundary-probe.py"
SPEC = importlib.util.spec_from_file_location("rac1_checkpoint_probe", SCRIPT)
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class Rac1CheckpointBoundaryProbeTests(unittest.TestCase):
    def make_memory(self):
        size = max(
            max(PROBE.RESET_SIGNATURES) + 4,
            PROBE.PLAYER_MOBY + 0x100,
            PROBE.LOCAL_UID_BITS + PROBE.UID_BYTES,
        )
        memory = bytearray(size)
        for address, word in PROBE.RESET_SIGNATURES.items():
            struct.pack_into("<I", memory, address, word)
        struct.pack_into("<I", memory, PROBE.NANOTECH, 4)
        struct.pack_into("<H", memory, PROBE.PLAYER_MOBY + 0xA6, 0)
        for base in (PROBE.PLAYER_STATE, PROBE.PLAYER_MOBY + 0x10, PROBE.RESET_SNAPSHOT):
            struct.pack_into("<3f", memory, base, 132.09, 115.48, 31.426616668701172)
        struct.pack_into("<f", memory, PROBE.PLAYER_STATE + 0x18, 0.6627014875411987)
        struct.pack_into("<f", memory, PROBE.PLAYER_MOBY + 0x48, 0.6627014875411987)
        struct.pack_into("<f", memory, PROBE.RESET_SNAPSHOT + 0x18, 0.6627014875411987)
        memory[PROBE.LEVEL_UID_BITS:PROBE.LEVEL_UID_BITS + 4] = b"\xe7\x87\x18\xfe"
        memory[PROBE.LOCAL_UID_BITS:PROBE.LOCAL_UID_BITS + 4] = b"\xe7\x87\x18\xfe"
        return memory

    def test_report_keeps_reset_source_separate_from_snapshot(self):
        report = PROBE.checkpoint_report(self.make_memory())

        self.assertEqual(report["loadedResetSignaturesVerified"], len(PROBE.RESET_SIGNATURES))
        self.assertEqual(report["witness"]["liveClassId"], 0)
        self.assertEqual(report["witness"]["nanotech"], 4)
        self.assertEqual(report["witness"]["resetSnapshotGate"], 0)
        self.assertEqual(report["witness"]["playerState"]["position"][0], report["witness"]["class0Moby"]["position"][0])
        self.assertAlmostEqual(report["witness"]["class0Moby"]["yawRadians"], 0.6627014875411987)
        self.assertTrue(report["witness"]["uidPersistence"]["mapsEqual"])
        self.assertEqual(report["resetDataflow"]["routine"], "0x00204c60")
        self.assertFalse(report["resetDataflow"]["snapshotAuthority"])
        self.assertIn("class-0 Moby", report["resetDataflow"]["placement"])

    def test_changed_loaded_signature_fails_closed(self):
        memory = self.make_memory()
        address = next(iter(PROBE.RESET_SIGNATURES))
        struct.pack_into("<I", memory, address, 0)
        with self.assertRaises(RuntimeError):
            PROBE.verify_reset_signatures(memory)


if __name__ == "__main__":
    unittest.main()
