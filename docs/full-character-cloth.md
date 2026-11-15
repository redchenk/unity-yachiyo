# Full character secondary motion

The private application uses the user's licensed **Magica Cloth 2.15.0**. The package and its source are excluded from Git. The public repository contains only Yachiyo integration code and local compatibility tooling. Importing the asset is required to reproduce the Magica build; without it, the existing SPCR/Unity Cloth implementation remains available.

## Local setup

1. Run `python3 tools/magica/import_licensed_package.py /path/to/licensed.unitypackage` in a checkout without an existing modified Magica asset folder, or import the licensed core through Unity.
2. For this project's Unity 6000.6 editor, run `python3 tools/magica/prepare_unity66.py`. It adapts the removed InstanceID APIs using collision-free internal integer handles backed by EntityId, and updates editor APIs. It also preserves explicit fixed/excluded vertex painting during reduced-proxy mapping, so the shoulder/neck region remains on its original skinning. This is a local compatibility patch, not a new version of Magica. Do not re-import over the patched folder.
3. Open Unity and allow package resolution and script compilation. `MagicaBuildSupport` detects the actual plugin assembly and enables `YACHIYO_MAGICA` for standalone builds.
4. Build the **saved Studio scene**. Do not recreate the avatar scene or cloth meshes.

## Ownership and collision configuration

- `FullCharacterCloth` replaces the old secondary-motion writers before they initialize. The source assets, geometry, UVs, shoulder constraints, humanoid animation, face and head/neck attachment stay authored. Only the hanging sleeve skin weights are replaced on a runtime mesh copy.
- Upper sleeves, skirt layers and ribbons use one MeshCloth component. The long hanging sleeves use two BoneCloth surfaces paired with this garment component. Fixed vertices are selected from actual skin weights and attachment positions; only free garment areas move. The component enables full surface self-collision, including sleeve-to-skirt contact, on a reduced simulation mesh.
- Twintails, fringe, side hair and hair ornaments use BoneCloth. The soft obi ornament uses a small-displacement BoneSpring. The BoneCloth groups have individual stiffness/angle limits and collision pairing with the garment surface. These independent hair/accessory groups are not an all-pairs self-collision system.
- Body, arms, palms, legs, feet, head, neck, shoulders, obi and floor receive registered Magica colliders. They do not alter game physics or the source model.
- The fixed-step solver runs at 60 Hz after the application's LateUpdate animation, with at most four catch-up steps. Rendering remains at the application's normal rate. No new background application is required.
- Full-surface self collision has a cost and is not a guarantee of zero penetration in arbitrary fast or deeply intersecting input poses. A globally applied Backstop is deliberately avoided because the authored layered garment includes mixed-facing lining normals.

## Regression and fallback

`--verify-full-cloth /absolute/output/path` performs native build, all-group initialization, finite mesh, original mesh, attachment, action, toggle and frame-time checks and captures front/side/back images. Assess the images as well as the metrics. These checks do not claim real GPU motion inference or provider dialogue validation.

`--legacy-secondary-motion` explicitly selects the previous SPCR sleeves, Unity Cloth skirt and hair spring comparison. Normal licensed builds select Magica. No old solver writes the same bones alongside it.

The obi ornament uses damped, small-angle BoneSpring motion limited to 2.5 mm at its anchor. Its own collision envelope is excluded from its ears/tentacles, and that collider is parented outside the simulated hierarchy to avoid feedback.

The two small ornament ears interpolate their simulated display rotation over 65 ms to suppress fixed-step shimmer; this does not drive body animation or other garments.

## Hanging sleeve flexibility

Each long sleeve uses an **8-column by 7-row Magica BoneCloth grid** (112 control points total). `SequentialNonLoopMesh` connects neighboring columns, while 8 anchored roots per sleeve preserve the seam. The roots belong to the original support bones and track the authored seam after body animation. Toggle checks sample at the end of the frame: Magica first restores animation transforms in EarlyUpdate and solves after LateUpdate, so sampling between those stages would incorrectly report the unsimulated pose.

The original front/back walls and their lining share the same grid deformation. No faces, UVs, textures or original asset weights are replaced. A runtime copy changes only the hanging region's weights, with a 9 cm transition at the original seam; the upper sleeve and shoulder remain authored. The original MeshCloth excludes these driven vertices, preventing competing writers. The grid has body colliders, self-contact and contact with the garment MeshCloth; it does not add an all-pairs hair-to-sleeve contact system.

The grid uses 0.035 bending stiffness, 0.015 angle restoration fading toward the hem, 0.13 damping and 95% tether compression. Skirt/cuff parameters retain their existing profile. Merely softening the original thin-shell MeshCloth made its lining cross the outer fabric, and runtime triangulation experiments were rejected. Normal builds use the coupled bone surfaces; `--mesh-furisode-baseline` retains the preceding MeshCloth configuration for comparison.

The native report checks all 8 groups, both sleeves' high-confidence vertex coverage, unchanged fixed attachments, local joint bending excluding root rotation, finite surfaces, and off/on after a moving clip. Simulation offsets have an upper bound as well as a lower bound so a sleeve returning to the wrong world location cannot pass.

For a reproducible lift/hold/lower comparison, run `--verify-full-cloth <directory> --cloth-sleeve-stress --idle-seed 7381`. Add `--diagnose-magica-sleeves` to export actual/skinned vertices and selected/proxy data alongside the images. This isolates physical response with the rest of the body frozen; it is separate from the full regression report.
