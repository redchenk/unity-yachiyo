# Sleeve bone cloth

The current sleeve driver embeds SPARK-inc/SPCRJointDynamics 2.0.11 (MIT), pinned to `7ebe63eb48e7737f3c443511ad2dc2caf1bf96b9`. Source, license and a list of local compatibility changes are in `UnityProject/Packages/inc.sparkcreative.spcrjointdynamics`. No Asset Store purchase is needed.

## Runtime ownership

- `SpcrBoneCloth` simulates separate runtime proxies for the left and right sleeves. Each side has an open A–B–C–D–E strip with structural, shear and bending constraints, plus the existing longitudinal hanging-panel chain. A and E are not joined. There are 90 transverse, shear and transverse bending constraints across both sleeves.
- `GarmentPhysics` steps the sleeves after body animation at 120 Hz. Animated attachment and capsule poses are interpolated into each substep; rendered rotations interpolate the two physics states. Large jumps, cloth toggles and discontinuities reset the simulation.
- `AuthoredSleevePhysics` transfers bounded rotations only. The original shoulder connections, local bone positions, full sleeve topology, UVs and skin weights are retained. The solver never transfers proxy translations to the avatar. Skin contact probes supplement the body capsules and skirt envelope.
- Original sleeve cutting/hem generation and sleeve vertex extrusion are disabled when this driver is available. Native Unity Cloth still drives the skirts; hair keeps its existing driver. The automatic SPCR job manager does not update these proxy controllers.
- `--sleeve-legacy` selects the earlier spring driver for comparison. `--sleeve-rigid` disables sleeve deformation for inspection. Normal launches use SPCR automatically; the existing cloth switch controls it.

## Native regression

Run the built player with `--director-url disabled --no-auto-connect --verify-bone-cloth <absolute-output-folder>`. Add `--bone-cloth-preview` to export a 10 fps rendered lift/wave/return preview. The test checks original topology and weights, unchanged bone translations, exact bone lengths, settling after a wave, cloth off/on and matching analytic motion at 30, 60 and 144 FPS.

The separate VMD regression in the local dance integration exercises continuous playback and emits real rendered frames. It is not part of the isolated sleeve test.

## Acceptance limits

This preserves the existing bone layout. The large hanging panel has one longitudinal chain, so it cannot acquire fine width-wise folds just by changing solvers. Dense crossed-arm poses may still have local sleeve/sleeve or sleeve/accessory intersections. The skin probes are collision approximations, not triangle-level self-collision. A stable regression is not evidence that all intersections have been removed.
