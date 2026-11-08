import unittest
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
from service.motion import convert, joint_names

ROOT=Path(__file__).resolve().parents[1]

class ConversionTests(unittest.TestCase):
    def data(self):
        return {"global_rot_mats":np.tile(np.eye(3),(2,1,1,1)),"root_positions":np.array([[1,1,2],[2,1,3]],float)}
    def test_identity_and_handedness(self):
        result=convert(self.data(),["Hips"])
        self.assertEqual(result["bones"],["Hips"])
        self.assertEqual(result["frames"][0]["root"],{"x":-1,"y":1,"z":2})
        self.assertEqual(result["frames"][0]["rotations"][0],{"x":0,"y":0,"z":0,"w":1})
    def test_rotation_basis_change(self):
        data=self.data();data["global_rot_mats"][:]=Rotation.from_euler("y",90,degrees=True).as_matrix()
        result=convert(data,["Hips"]);q=result["frames"][0]["rotations"][0]
        v=Rotation.from_quat([q[k]for k in "xyzw"]).apply([0,0,1])
        np.testing.assert_allclose(v,[-1,0,0],atol=1e-6)
    def test_rejects_nonfinite_and_invalid_matrices(self):
        data=self.data();data["root_positions"][0,0]=np.nan
        with self.assertRaises(ValueError):convert(data,["Hips"])
        data=self.data();data["global_rot_mats"][0]*=2
        with self.assertRaises(ValueError):convert(data,["Hips"])
    def test_supported_skeletons(self):
        for n in (30,77):self.assertEqual(len(joint_names(ROOT/"vendor/kimodo",n)),n)
        with self.assertRaises(ValueError):joint_names(ROOT/"vendor/kimodo",22)
    def test_rejects_multiple_clips(self):
        data=self.data();data["global_rot_mats"]=np.tile(np.eye(3),(2,2,1,1,1))
        with self.assertRaises(ValueError):convert(data,["Hips"])
    def test_new_soma77_six_contact_output(self):
        data=self.data();data["foot_contacts"]=np.array([[[1,0,1,0,1,0],[0,0,0,1,0,1]]])
        result=convert(data,["Hips"])
        self.assertEqual(result["frames"][0]["contacts"],[1,1,0,1])
        self.assertEqual(result["frames"][1]["contacts"],[0,0,1,1])

if __name__=="__main__":unittest.main()
