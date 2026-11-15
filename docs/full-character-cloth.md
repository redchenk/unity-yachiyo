# Full character secondary motion

The private application uses the user's licensed **Magica Cloth 2.15.0**. The package and its source are excluded from Git. The public repository contains only Yachiyo integration code and local compatibility tooling. Importing the asset is required to reproduce the Magica build; without it, the existing SPCR/Unity Cloth implementation remains available.

## Local setup

1. Run `python3 tools/magica/import_licensed_package.py /path/to/licensed.unitypackage` in a checkout without an existing modified Magica asset folder, or import the licensed core through Unity.
2. For this project's Unity 6000.6 editor, run `python3 tools/magica/prepare_unity66.py`. It adapts the removed InstanceID APIs using collision-free internal integer handles backed by EntityId, and updates editor APIs. It also preserves explicit fixed/excluded vertex painting during reduced-proxy mapping, so the shoulder/neck region remains on its original skinning. This is a local compatibility patch, not a new version of Magica. Do not re-import over the patched folder.
3. Open Unity and allow package resolution and script compilation. `MagicaBuildSupport` detects the actual plugin assembly and enables `YACHIYO_MAGICA` for standalone builds.
4. Build the **saved Studio scene**. Do not recreate the avatar scene or cloth meshes.

## Ownership and collision configuration

- `FullCharacterCloth` replaces the old secondary-motion writers before they initialize. The mesh, skin weights, UVs, shoulder constraints, humanoid animation, face and head/neck attachment stay authored.
- Sleeves, hanging panels, skirt layers and ribbons use one MeshCloth component. Fixed vertices are selected from actual skin weights and attachment positions; only free garment areas move. The component enables full surface self-collision, including sleeve-to-skirt contact, on a reduced simulation mesh.
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

The long `Acs_CenterSleeve` panels are included in the garment MeshCloth. The native regression now separately checks both panels' high-confidence weighted vertices and their deformation relative to authored skinning; body movement alone does not pass that check. Per-side coverage and actual simulation offsets are included in the report.

The garment profile explicitly permits 90% tether compression, uses lower triangle/angle restoration and a larger movement envelope. This lets the hanging panels fold inward instead of being held at 60% of their rest tether length by the package default. Collision and the original fixed shoulder boundary remain enabled. All original topology, UVs and weights remain unchanged; runtime remeshing is not used.

For a reproducible lift/hold/lower comparison, run `--verify-full-cloth <directory> --cloth-sleeve-stress --idle-seed 7381`. Add `--diagnose-magica-sleeves` to export actual/skinned vertices and selected/proxy data alongside the images. This isolates physical response with the rest of the body frozen; it is separate from the full regression report.
