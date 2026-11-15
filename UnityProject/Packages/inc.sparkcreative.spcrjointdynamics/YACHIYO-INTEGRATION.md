# Embedded upstream source

SPARK-inc/SPCRJointDynamics 2.0.11, commit `7ebe63eb48e7737f3c443511ad2dc2caf1bf96b9`.
Source: https://github.com/SPARK-inc/SPCRJointDynamics
MIT license is retained in LICENSE.md and source headers.

Local formatting: trailing whitespace is normalized.

Local changes: declare Burst 1.8.31 dependency; controller ExternalUpdate option skips automatic job-manager registration, accepts an explicit timestep, starts with an empty accumulator, and exposes an immediate state reset. Yachiyo runs fixed substeps after body animation on runtime proxy transforms. Solver never writes original skin positions or source assets.

Unity 6000.6.4f1 resolves the declared minimum Burst dependency to its bundled Burst 2.0.0; the exact resolution is recorded in packages-lock.json.
