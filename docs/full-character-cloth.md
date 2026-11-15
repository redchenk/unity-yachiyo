# Full character secondary motion

The private application uses the user's licensed **Magica Cloth 2.18.4**. The package and its source are excluded from Git. The public repository contains only Yachiyo integration code and local compatibility tooling. Importing the asset is required to reproduce the Magica build; without it, the existing SPCR/Unity Cloth implementation remains available.

## Local setup

1. Run `python3 tools/magica/import_licensed_package.py /path/to/licensed.unitypackage` in a checkout without an existing modified Magica asset folder, or import the licensed core through Unity.
2. For this project's Unity 6000.6 editor, run `python3 tools/magica/prepare_unity66.py`. Version 2.18.4 supplies its own EntityId support, so the legacy API shim is skipped. The remaining local patch preserves explicit fixed/excluded vertex painting during reduced-proxy mapping. For an upgrade, back up the old licensed folder and import into a clean asset folder; do not overlay versions.
3. Open Unity and allow package resolution and script compilation. `MagicaBuildSupport` detects the actual plugin assembly and enables `YACHIYO_MAGICA` for standalone builds.
4. Build the **saved Studio scene**. Do not recreate the avatar scene or cloth meshes.

## Ownership and collision configuration

- `FullCharacterCloth` replaces the old secondary-motion writers before they initialize. The source assets, geometry, UVs, shoulder constraints, humanoid animation, face and head/neck attachment stay authored. Sleeve skin weights are replaced on a runtime mesh copy; the shoulder roll and original attachment remain protected.
- Skirt layers and waist ribbons use one MeshCloth component. The forearm shells, ruffled cuffs and hanging sleeves use two MeshCloth simulation surfaces paired with this garment component; purple sleeve ties have a separate BoneCloth group. Fixed vertices are selected from actual skin weights and attachment positions; only free garment areas move. The components enable full surface self-collision, including sleeve-to-skirt contact, on reduced simulation meshes.
- Twintails, fringe, side hair and hair ornaments use BoneCloth. The soft obi ornament uses a small-displacement BoneSpring. The BoneCloth groups have individual stiffness/angle limits and collision pairing with the garment surface. These independent hair/accessory groups are not an all-pairs self-collision system.
- Body, arms, palms, legs, feet, head, neck, shoulders, obi and floor receive registered Magica colliders. They do not alter game physics or the source model.
- The fixed-step solver runs at 60 Hz after the application's LateUpdate animation, with at most four catch-up steps. Rendering remains at the application's normal rate. No new background application is required.
- Full-surface self collision has a cost and is not a guarantee of zero penetration in arbitrary fast or deeply intersecting input poses. A globally applied Backstop is deliberately avoided because the authored layered garment includes mixed-facing lining normals.

## Regression and fallback

`--verify-full-cloth /absolute/output/path` performs native build, all-group initialization, finite mesh, original mesh, attachment, action, toggle and frame-time checks and captures front/side/back images. Assess the images as well as the metrics. These checks do not claim real GPU motion inference or provider dialogue validation.

`--legacy-secondary-motion` explicitly selects the previous SPCR sleeves, Unity Cloth skirt and hair spring comparison. Normal licensed builds select Magica. No old solver writes the same bones alongside it.

The obi ornament uses damped, small-angle BoneSpring motion limited to 2.5 mm at its anchor. Its own collision envelope is excluded from its ears/tentacles, and that collider is parented outside the simulated hierarchy to avoid feedback.

The two small ornament ears interpolate their simulated display rotation over 65 ms to suppress fixed-step shimmer; this does not drive body animation or other garments.

## KAGURA example and the sleeve implementation

The supplied 2.18.4 `UnityCHanKAGURA_MC2.prefab` uses separate **MeshCloth** components named `sode L` and `sode R`, plus separate **BoneCloth** for `sode ribbon L/R`. The sleeve profile has animation-pose influence zero, gravity 5, damping 0.158, a sharply falling angle-restoration curve, and no maximum-distance, backstop or angle-limit cage. Its sleeve self-collision is disabled and no sleeve collider list is assigned. Those contact settings are unsuitable for this avatar's layered skirt; Yachiyo keeps body and skirt contact.

Yachiyo's outer fabric, lining and red cuff cannot be simulated as unrelated overlapping sheets: direct full-mesh trials produced crossed lining and distortion during arm raises. Each side now uses a **single-layer, 8 by 11 vertex MeshCloth simulation proxy**. The same surface deforms both walls and cuff through runtime skin weights and 176 display transforms. These transforms display MeshCloth output; they are not independently solved BoneCloth chains. The source geometry, UVs, materials and shoulder attachment remain untouched. A protected transition to the original torso skin avoids pulling the body seam into the free sleeve.

The two purple ties retain their original bone structure and use their own BoneCloth group. Runtime weights remove mixed arm/sleeve influences from those islands. Their roots follow the displayed sleeve sewing points both before simulation and after the mesh update, so the visible attachment is aligned in the same rendered frame. The tie shape uses small-angle, damped secondary motion. Its own contacts are disabled to avoid collision-driven stretching at the sewn root.

The proxy uses the example's rest-pose behavior and restoration curve, adapted to this model's coarser physical mesh. Its distance stiffness stays high to preserve length, and the two sides have body colliders, self-contact and contact with the garment MeshCloth. Three skirt-volume guards are assigned only to the sleeve groups, preventing inside-out recovery through the layered skirt without pushing the skirt itself outward. The garment, sleeve ties, hair and obi groups all run on 2.18.4. Normal operation creates **9 groups**. The reference example and vendor source remain private local assets, excluded from Git.

`--bone-furisode-baseline` selects the preceding BoneCloth sleeve solver for comparison. `--mesh-furisode-baseline` retains the older direct garment MeshCloth comparison. Neither is the normal build.

The full native report checks group initialization, original assets, fixed attachments, both forearms/cuffs/junctions, finite surfaces, idle/wave/turn/motion transitions, off/on, ear settling and frame time. It measures simulation relative to original skinning rather than counting body movement as cloth motion. Display transfer time is included in the cloth timing.

Use `--verify-full-cloth <directory> --cloth-sleeve-attachment-stress --idle-seed 7381` for a 22-second left/right/both-arm lift, elbow bend, torso turn and release sequence. Add `--diagnose-magica-sleeves` to export displayed/skinned vertices. The report checks both ties against authored sewing-point pairs, extra edge stretch at the simulated/original boundary, and protected torso vertex error. Inspect the rendered front, side and back frames as well as the report; these checks do not establish zero penetration in arbitrary input poses.
