import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "rac1-movement-contact-validation.py"
SPEC = importlib.util.spec_from_file_location("rac1_movement_contact_validation", SCRIPT)
VALIDATION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATION)


class Rac1MovementContactValidationTests(unittest.TestCase):
    def test_retained_evidence_passes_acceptance_error_budget(self):
        report = VALIDATION.build_summary(ROOT / "research" / "generated")

        self.assertEqual(report["status"], "passed")
        self.assertTrue(all(report["checks"].values()))
        metrics = report["retailVsModel"]
        self.assertLess(metrics["yawMaximumRmsResidualRad"], 1e-6)
        self.assertLess(metrics["runningJumpHorizontalErrorNativeUnits"], 1e-4)
        self.assertLess(metrics["slopeRunMagnitudeMaximumErrorNativeUnits"], 1e-5)
        self.assertGreaterEqual(metrics["stationarySupportedSlopeTailTicks"], 45)
        reachability = report["retailReachability"]
        self.assertEqual(reachability["novalisWadeTriangles"], 3936)
        self.assertEqual(reachability["aridiaMudTriangles"], 1975)
        self.assertEqual(reachability["hovenIceTriangles"], 2777)
        self.assertEqual(reachability["quartuClass1250AuthoredInstances"], 18)
        self.assertIn("MaxSlides", report["boundary"])

    def test_summary_keeps_special_motion_fail_closed_boundary_explicit(self):
        report = VALIDATION.build_summary(ROOT / "research" / "generated")

        self.assertIn(
            "fails closed",
            report["portableRuntimeContracts"]["specialSurfaces"],
        )
        self.assertIn(
            "1/24",
            report["portableRuntimeContracts"]["conveyor"],
        )


if __name__ == "__main__":
    unittest.main()
